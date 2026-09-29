using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using BS.Utilities.Async;
using UnityEngine;

namespace BS
{
    /// <summary>
    /// A generic, named channel between a space page (or a Visual Scripting graph) and a feature
    /// the host app provides — recording and the newsroom today. The SDK knows nothing about any
    /// extension: an app assembly registers a handler under a name, and requests for that name are
    /// routed to it.
    ///
    /// <para><b>Requests.</b> A page calls <c>scene.HostExtension(command, op, body)</c>, which
    /// travels as <c>APICommands.HOST_EXT_OP</c> and arrives here as a <see cref="BSHostRequest"/>
    /// (<c>Command</c> = the extension name, <c>Op</c>, <c>Json</c>). A handler MUST set
    /// <see cref="BSHostRequest.Handled"/> synchronously when it takes a request, and then call
    /// <see cref="BSHostRequest.Respond"/> exactly once with <c>{"ok":true,...}</c> or
    /// <c>{"ok":false,"error":"code"}</c>. A request nobody takes is answered
    /// <c>{"ok":false,"error":"app_unavailable"}</c>, so a page promise always settles. Handlers
    /// always run on the Unity main thread.</para>
    ///
    /// <para><b>Events.</b> <see cref="Emit"/> pushes <c>{command, evt, data}</c> to the page (a
    /// <c>host-ext</c> event on the scene) and raises <see cref="Emitted"/> for in-process
    /// listeners such as the Visual Scripting event units.</para>
    ///
    /// <para><b>Lifetime.</b> The registry is static rather than a <c>BSSceneEvents</c> UnityEvent,
    /// so registrations survive <c>BSScene.Destroy</c> and space changes. It is cleared at
    /// <c>SubsystemRegistration</c> (for play mode without a domain reload), so register from
    /// <c>AfterAssembliesLoaded</c>/<c>BeforeSceneLoad</c> or later, never from
    /// <c>SubsystemRegistration</c> itself.</para>
    /// </summary>
    public static class BSHostExtensions
    {
        /// <summary>The reply to a request no handler took.</summary>
        public const string UnavailableReply = "{\"ok\":false,\"error\":\"app_unavailable\"}";

        /// <summary>The reply to a request whose handler threw.</summary>
        public const string InternalErrorReply = "{\"ok\":false,\"error\":\"internal_error\"}";

        /// <summary>The reply to a request whose extension name is not usable.</summary>
        public const string BadRequestReply = "{\"ok\":false,\"error\":\"bad_request\"}";

        /// <summary>Longest extension, op or event name accepted.</summary>
        public const int MaxNameLength = 64;

        private static readonly object Gate = new object();

        // Copy-on-write arrays: Dispatch snapshots the array under the lock and invokes outside
        // it, so a handler may register or unregister (itself included) while running.
        private static readonly Dictionary<string, Action<BSHostRequest>[]> Handlers =
            new Dictionary<string, Action<BSHostRequest>[]>(StringComparer.Ordinal);

        private static int _mainThreadId = -1;

        /// <summary>
        /// Raised on the main thread for every <see cref="Emit"/>, with (command, evt, json), before
        /// the page is told. json is never null ("null" when nothing was supplied).
        /// </summary>
        public static event Action<string, string, string> Emitted;

        /// <summary>
        /// Route requests for <paramref name="command"/> to <paramref name="handler"/>. Several
        /// handlers may share a name: they are offered the request in registration order until
        /// one sets <see cref="BSHostRequest.Handled"/>. Registering the same delegate twice is a
        /// no-op.
        /// </summary>
        public static void Register(string command, Action<BSHostRequest> handler)
        {
            if (!IsValidName(command))
                throw new ArgumentException("A host extension name must be 1-" + MaxNameLength + " characters with no bus delimiters.", nameof(command));
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            lock (Gate)
            {
                Handlers.TryGetValue(command, out var current);
                if (current != null && Array.IndexOf(current, handler) >= 0) return;
                var next = new Action<BSHostRequest>[(current?.Length ?? 0) + 1];
                current?.CopyTo(next, 0);
                next[next.Length - 1] = handler;
                Handlers[command] = next;
            }
        }

        /// <summary>Remove a handler added with <see cref="Register"/>. Unknown pairs are ignored.</summary>
        public static void Unregister(string command, Action<BSHostRequest> handler)
        {
            if (command == null || handler == null) return;
            lock (Gate)
            {
                if (!Handlers.TryGetValue(command, out var current)) return;
                var index = Array.IndexOf(current, handler);
                if (index < 0) return;
                if (current.Length == 1)
                {
                    Handlers.Remove(command);
                    return;
                }
                var next = new Action<BSHostRequest>[current.Length - 1];
                Array.Copy(current, 0, next, 0, index);
                Array.Copy(current, index + 1, next, index, current.Length - index - 1);
                Handlers[command] = next;
            }
        }

        /// <summary>True when at least one handler is registered for <paramref name="command"/>.</summary>
        public static bool IsRegistered(string command)
        {
            if (command == null) return false;
            lock (Gate) return Handlers.ContainsKey(command);
        }

        /// <summary>
        /// Push an event to the page as a <c>host-ext</c> event <c>{command, evt, data}</c>, where
        /// data is <paramref name="json"/> parsed, and raise <see cref="Emitted"/>. Safe from any
        /// thread: off the main thread it is marshalled there first. Does nothing but log when
        /// either name is unusable; a missing page is not an error.
        /// </summary>
        public static void Emit(string command, string evt, string json)
        {
            if (!IsValidName(command) || !IsValidName(evt))
            {
                Debug.LogError("[Banter] BSHostExtensions.Emit: invalid extension or event name '" + command + "'/'" + evt + "'");
                return;
            }
            if (OnMainThread)
            {
                EmitNow(command, evt, json);
            }
            else
            {
                UnityMainThreadTaskScheduler.Default.Enqueue(TaskRunner.Track(
                    () => EmitNow(command, evt, json), $"{nameof(BSHostExtensions)}.{nameof(Emit)}"));
            }
        }

        /// <summary>
        /// Send a request to an extension from C# without a page — what the Visual Scripting units
        /// use. <paramref name="onReply"/> receives the handler's JSON envelope (or
        /// <see cref="UnavailableReply"/>); when the handler answers synchronously it is called
        /// before this returns. <paramref name="target"/> travels as <see cref="BSHostRequest.Target"/>.
        /// </summary>
        public static BSHostRequest Request(string command, string op, string json, Action<string> onReply,
            GameObject target = null, string requestId = null)
        {
            var request = new BSHostRequest(command, onReply)
            {
                Op = op ?? "",
                Json = string.IsNullOrEmpty(json) ? "{}" : json,
                Target = target,
                RequestId = requestId
            };
            if (!IsValidName(command))
            {
                request.Respond(BadRequestReply);
                return request;
            }
            DispatchOnMainThread(request);
            return request;
        }

        /// <summary>
        /// The GameObject a page named by instance id (the <c>unityId</c> of a JS GameObject), or
        /// null. Main thread only.
        /// </summary>
        public static GameObject ResolveObject(int instanceId)
        {
            var scene = BSScene.Current;
            return scene?.GetGameObject(instanceId);
        }

        /// <summary>
        /// Extension, op and event names travel unescaped on the bus, so they must be non-empty,
        /// at most <see cref="MaxNameLength"/> characters, and free of every bus delimiter.
        /// </summary>
        public static bool IsValidName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength) return false;
            return name.IndexOf(MessageDelimiters.PRIMARY, StringComparison.Ordinal) < 0
                && name.IndexOf(MessageDelimiters.SECONDARY, StringComparison.Ordinal) < 0
                && name.IndexOf(MessageDelimiters.TERTIARY, StringComparison.Ordinal) < 0
                && name.IndexOf(MessageDelimiters.BATCH, StringComparison.Ordinal) < 0
                && name.IndexOf(MessageDelimiters.WINDOW, StringComparison.Ordinal) < 0;
        }

        /// <summary>Run <see cref="Dispatch"/> on the main thread, inline when already there.</summary>
        internal static void DispatchOnMainThread(BSHostRequest request)
        {
            if (request == null) return;
            if (OnMainThread)
            {
                Dispatch(request);
                return;
            }
            UnityMainThreadTaskScheduler.Default.Enqueue(TaskRunner.Track(
                () => Dispatch(request), $"{nameof(BSHostExtensions)}.{nameof(Dispatch)}"));
        }

        /// <summary>
        /// Offer <paramref name="request"/> to its extension's handlers on the calling thread.
        /// Exactly one reply always results: the handler's own, internal_error when a handler
        /// throws, or app_unavailable when nobody takes it.
        /// </summary>
        internal static void Dispatch(BSHostRequest request)
        {
            if (request == null) return;
            Action<BSHostRequest>[] handlers = null;
            if (request.Command != null)
            {
                lock (Gate) Handlers.TryGetValue(request.Command, out handlers);
            }
            if (handlers != null)
            {
                foreach (var handler in handlers)
                {
                    try
                    {
                        handler(request);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError("[Banter] Host extension '" + request.Command + ":" + request.Op + "' failed: " + e);
                        request.Respond(InternalErrorReply);
                        return;
                    }
                    if (request.Handled || request.Responded) return;
                }
            }
            request.Respond(UnavailableReply);
        }

        private static void EmitNow(string command, string evt, string json)
        {
            json = string.IsNullOrEmpty(json) ? "null" : json;

            var listeners = Emitted;
            if (listeners != null)
            {
                // One failing listener must not starve the rest, or the page.
                foreach (Action<string, string, string> listener in listeners.GetInvocationList())
                {
                    try { listener(command, evt, json); }
                    catch (Exception e) { Debug.LogException(e); }
                }
            }

            var link = BSScene.Current?.link;
            if (link == null) return;
            var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
            link.Send(APICommands.EVENT + APICommands.HOST_EXT_EVENT + MessageDelimiters.PRIMARY
                      + command + MessageDelimiters.SECONDARY + evt + MessageDelimiters.SECONDARY + b64);
        }

        private static bool OnMainThread
        {
            get
            {
                var id = Volatile.Read(ref _mainThreadId);
                if (id >= 0) return Thread.CurrentThread.ManagedThreadId == id;
                // Not captured yet (edit mode before the first reload hook): fall back to the SDK's
                // own record, and failing that assume the caller knows it is on the main thread,
                // which is where Unity calls everything that could reach here that early.
                return UnityGame.OnMainThread || !UnityMainThreadTaskScheduler.Default.IsRunning;
            }
        }

        // SubsystemRegistration runs first on every play-mode entry, including with domain reload
        // disabled, and always on the main thread.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            lock (Gate) Handlers.Clear();
            Emitted = null;
            CaptureMainThread();
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        private static void CaptureMainThread()
        {
            Volatile.Write(ref _mainThreadId, Thread.CurrentThread.ManagedThreadId);
        }
    }
}
