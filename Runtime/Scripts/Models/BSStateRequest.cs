using System;
using System.Threading;

namespace BS
{
    /// <summary>Which state store a <see cref="BSStateRequest"/> targets.</summary>
    public enum BSStateTarget
    {
        Space,
        User
    }

    /// <summary>
    /// One space/user state operation on its way from a page or a Visual Scripting unit to whatever
    /// backend the host app provides. The SDK knows nothing about that backend: it raises this on
    /// <c>BSSceneEvents.OnStateRequest</c> and waits for <see cref="Respond"/>.
    ///
    /// One event carrying an op object, rather than a UnityEvent per verb, so adding an operation
    /// is a data change rather than an API change and <c>BSSceneEvents</c> does not grow a dozen
    /// fields.
    /// </summary>
    public class BSStateRequest
    {
        public BSStateTarget Target;

        /// <summary>set | merge | get | delete | increment | decrement | toggle | compareAndSet | protect</summary>
        public string Op;

        /// <summary>Dotted path exactly as the page or graph authored it, before any encoding.</summary>
        public string Path;

        /// <summary>The decoded compact JSON body: value, expected, amount, scope and friends.</summary>
        public string Json;

        /// <summary>Space: protected scope. User: the prop is moderator-writable.</summary>
        public bool IsProtected;

        /// <summary>User target only. Null/empty means the local user.</summary>
        public string UserId;

        /// <summary>Visual Scripting correlation id; null for page-originated ops.</summary>
        public string RequestId;

        /// <summary>
        /// The handler MUST set this synchronously before going async. <c>UnityEvent.Invoke</c> is
        /// synchronous, so an unset flag after it returns means nothing is listening — which is how
        /// a page promise still settles in a build with no networking bridge.
        /// </summary>
        public bool Handled;

        private Action<string> _respond;
        private int _responded;

        public BSStateRequest(Action<string> respond)
        {
            _respond = respond;
        }

        /// <summary>
        /// Reply with a compact JSON envelope: <c>{"ok":true,...}</c> or
        /// <c>{"ok":false,"error":"code"}</c>. Safe from any thread, and idempotent — a handler that
        /// sets <see cref="Handled"/> and then throws must not be able to double-reply and corrupt
        /// the request-id table.
        /// </summary>
        public void Respond(string json)
        {
            if (Interlocked.Exchange(ref _responded, 1) != 0) return;
            _respond?.Invoke(json);
        }
    }
}
