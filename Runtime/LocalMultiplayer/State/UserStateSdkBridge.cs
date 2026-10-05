// <mirror source="Assets/Systems/Networking/State/UserStateSdkBridge.cs" sha256="5e088f3e9c284b785f8bb228090c7767cba4faffc13e7547dee61eef8ff588be" mode="port" />
// Ported line for line. Substitutions: the MonoBehaviour is a plain class UserStateModule ticks; the service is handed
// in instead of polled from UserStateService.Instance; BSScene.Instance() -> BSScene.Current; the LINQ user lookups are
// plain loops with the same first-match semantics (the replay drain runs every frame while anything is pending); the
// GetUserStateValue delegate is put back on unhook when it is still ours.
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Events;

namespace BS.LocalMultiplayer.State
{
    /// <summary>
    /// Projects <see cref="UserStateService"/> onto the Creator SDK's per-user prop surfaces.
    ///
    /// Two long-standing defects are routed around here, as the Greenfield client does:
    /// <list type="bullet">
    /// <item><c>BSScene.UserPropChanged</c> sends <c>UserData.id</c> (the roomSessionId) while the
    /// page keys <c>scene.users</c> by <c>uid</c>, so EVERY user-state change was dropped by the
    /// page with "got user-state-changed event for user that doesn't exist?". This bridge calls
    /// <c>link.OnUserStateChanged</c> directly with the <c>uid</c>.</item>
    /// <item><c>BSScene.SetProps</c> would resolve any user by id and echo to the page anyway — so writing
    /// another user's props appeared to succeed locally. While a network host is active, the SDK raises
    /// <c>OnSetUserProps</c> instead, and writes aimed at anyone else are refused here.</item>
    /// </list>
    /// </summary>
    public sealed class UserStateSdkBridge
    {
        private BSScene _scene;
        private BSSceneEvents _events;
        private UserStateService _service;
        private bool _serviceHooked;

        /// <summary>Participants whose props arrived before their UserData existed.</summary>
        private readonly HashSet<string> _pendingReplay = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<string> _replayScratch = new List<string>();

        // Cached so AddListener/RemoveListener always see the same delegate instances.
        private readonly UnityAction<string, string[]> _onPageUserProps;
        private readonly UnityAction<BSStateRequest> _onStateRequest;
        private readonly UnityAction _onSpaceUnload;
        private readonly Func<string, string, string> _readForVisualScripting;
        private Func<string, string, string> _previousGetUserStateValue;

        private readonly Action<string, string, JToken, JToken> _onPropChanged;
        private readonly Action<string, string, string> _onWriteFailed;

        public UserStateSdkBridge(UserStateService service)
        {
            _onPageUserProps = OnPageUserProps;
            _onStateRequest = OnStateRequest;
            _onSpaceUnload = OnSpaceUnload;
            _readForVisualScripting = ReadForVisualScripting;
            _onPropChanged = OnPropChanged;
            _onWriteFailed = OnWriteFailed;
            HookService(service);
        }

        /// <summary>Production's Update: re-hooks the scene whenever the SDK swaps it, then replays pending props.</summary>
        public void Tick()
        {
            EnsureSceneHooked();
            DrainPendingReplay();
        }

        /// <summary>Production's OnDestroy.</summary>
        public void Dispose()
        {
            UnhookScene();
            UnhookService();
        }

        /// <summary>Re-deliver props for peers whose UserData has since spawned.</summary>
        private void DrainPendingReplay()
        {
            if (_pendingReplay.Count == 0 || _service == null || _scene == null || _scene.users == null) return;

            _replayScratch.Clear();
            foreach (string roomSessionId in _pendingReplay)
            {
                if (FindUserById(roomSessionId) != null) _replayScratch.Add(roomSessionId);
            }

            foreach (string roomSessionId in _replayScratch)
            {
                _pendingReplay.Remove(roomSessionId);
                foreach (var prop in _service.Props(roomSessionId))
                    OnPropChanged(roomSessionId, prop.Key, prop.Value, null);
            }
        }

        private void HookService(UserStateService service)
        {
            if (service == null || ReferenceEquals(service, _service)) return;
            UnhookService();
            _service = service;
            _service.PropChanged += _onPropChanged;
            _service.WriteFailed += _onWriteFailed;
            _serviceHooked = true;
        }

        private void UnhookService()
        {
            if (!_serviceHooked || _service == null) return;
            _service.PropChanged -= _onPropChanged;
            _service.WriteFailed -= _onWriteFailed;
            _service = null;
            _serviceHooked = false;
        }

        private void EnsureSceneHooked()
        {
            var scene = BSScene.Current;
            if (scene == null || scene.events == null) return;
            if (ReferenceEquals(scene, _scene) && ReferenceEquals(scene.events, _events)) return;
            HookScene(scene);
        }

        /// <summary>Subscribes to <paramref name="scene"/> now: the module calls it at install, before any scene object starts.</summary>
        public void HookScene(BSScene scene)
        {
            if (scene == null || scene.events == null) return;
            if (ReferenceEquals(scene, _scene) && ReferenceEquals(scene.events, _events)) return;

            UnhookScene();
            _scene = scene;
            _events = scene.events;
            _events.OnSetUserProps.AddListener(_onPageUserProps);
            _events.OnStateRequest.AddListener(_onStateRequest);
            _events.OnLoad.AddListener(_onSpaceUnload);
            _previousGetUserStateValue = _events.GetUserStateValue;
            _events.GetUserStateValue = _readForVisualScripting;
        }

        private void UnhookScene()
        {
            if (_events == null) return;
            _events.OnSetUserProps.RemoveListener(_onPageUserProps);
            _events.OnStateRequest.RemoveListener(_onStateRequest);
            _events.OnLoad.RemoveListener(_onSpaceUnload);
            if (_events.GetUserStateValue == _readForVisualScripting)
            {
                _events.GetUserStateValue = _previousGetUserStateValue;
            }
            _previousGetUserStateValue = null;
            _events = null;
            _scene = null;
        }

        private void OnSpaceUnload() => _service?.ClearAll();

        // ------------------------------------------------------------------ page -> server

        /// <summary>
        /// A page's <c>SetUserProps(props, id)</c>. The id is meaningless under owner-writes-only
        /// semantics — the server derives the owner from the connection and ignores anything the
        /// client claims — so a write aimed at anyone else is refused rather than silently applied
        /// to ourselves.
        /// </summary>
        private void OnPageUserProps(string targetId, string[] props)
        {
            if (_service == null || props == null) return;

            if (!string.IsNullOrEmpty(targetId) && !IsLocalUser(targetId))
            {
                Debug.LogWarning(
                    "[LocalMP][UserState] Ignoring SetUserProps aimed at another user: user props are owner-writes-only.");
                return;
            }

            foreach (string prop in props)
            {
                if (string.IsNullOrEmpty(prop)) continue;
                string[] parts = prop.Split(MessageDelimiters.TERTIARY.ToCharArray()[0]);
                if (parts.Length != 2) { Debug.LogWarning($"[LocalMP][UserState] Invalid prop: {prop}"); continue; }
                // Legacy props are strings and owner-only; the new API is where scope is chosen.
                _service.SetOwnProp(parts[0], SdkWireCodec.ParseLegacy(parts[1]), UserPropScope.OwnerOnly);
            }
        }

        private bool IsLocalUser(string id)
        {
            if (_service != null && id == _service.OwnRoomSessionId) return true;
            var local = FindLocalUser();
            return local != null && (id == local.id || id == local.uid);
        }

        // ------------------------------------------------------------------ server -> page

        private void OnPropChanged(string roomSessionId, string key, JToken value, JToken previous)
        {
            if (_scene == null || _scene.link == null) return;

            var user = FindUserById(roomSessionId);
            if (user == null)
            {
                // User state routinely arrives before the peer's avatar (and therefore its UserData)
                // exists. Remember them and replay once it does, or the props are lost for good.
                _pendingReplay.Add(roomSessionId);
                return;
            }

            string rendered = value == null ? string.Empty : SdkWireCodec.Render(value);

            // uid, not id: scene.users is keyed by uid on the page.
            string payload = SdkWireCodec.San(user.uid)
                             + MessageDelimiters.SECONDARY
                             + SdkWireCodec.San(key) + MessageDelimiters.TERTIARY + SdkWireCodec.San(rendered);
            _scene.link.OnUserStateChanged(payload);

            _scene.link.OnUserStateJson(SdkWireCodec.ToBase64Json(new JObject
            {
                ["id"] = user.id,
                ["uid"] = user.uid,
                ["full"] = false,
                ["changes"] = new JArray
                {
                    new JObject
                    {
                        ["path"] = key,
                        ["value"] = value,
                        ["oldValue"] = previous,
                        ["deleted"] = value == null
                    }
                }
            }));

            EventBus.Trigger("OnUserStateValueChanged", new CustomEventArgs(key, new object[]
            {
                rendered,
                value == null ? string.Empty : value.ToString(Formatting.None),
                user.id,
                user.isLocal,
                value == null
            }));
        }

        private void OnWriteFailed(string key, string code, string message)
        {
            // The page needs this too: SetUserProps is fire-and-forget and has nothing to reject.
            if (_scene != null && _scene.link != null)
            {
                _scene.link.OnUserStateJson(SdkWireCodec.ToBase64Json(new JObject
                {
                    ["error"] = new JObject
                    {
                        ["key"] = key,
                        ["code"] = code,
                        ["message"] = message
                    }
                }));
            }
            EventBus.Trigger("OnUserStateError", new CustomEventArgs(key, new object[] { code, message }));
        }

        // ------------------------------------------------------------------ JSON API

        private void OnStateRequest(BSStateRequest request)
        {
            if (request == null || request.Target != BSStateTarget.User) return;
            request.Handled = true;

            if (_service == null) { request.Respond(Envelope(false, StateErrors.NotConnected)); return; }

            JObject body;
            try { body = JObject.Parse(request.Json ?? "{}"); }
            catch (JsonException) { request.Respond(Envelope(false, StateErrors.InvalidValue)); return; }

            string key = (string)body["path"];
            string userId = (string)body["userId"];
            bool moderatorWritable = (bool?)body["moderatorsCanWrite"] ?? false;
            var scope = moderatorWritable ? UserPropScope.ModeratorWritable : UserPropScope.OwnerOnly;

            switch (request.Op)
            {
                case "get":
                    {
                        string target = string.IsNullOrEmpty(userId) ? _service.OwnRoomSessionId : ResolveRoomSessionId(userId);
                        bool found = _service.TryGetProp(target, key, out var value, scope);
                        var reply = new JObject { ["ok"] = true };
                        if (found) reply["value"] = value;
                        request.Respond(reply.ToString(Formatting.None));
                        return;
                    }
                case "getAll":
                    {
                        string target = string.IsNullOrEmpty(userId) ? _service.OwnRoomSessionId : ResolveRoomSessionId(userId);
                        var props = new JObject();
                        foreach (var pair in _service.Props(target)) props[pair.Key] = pair.Value;
                        request.Respond(new JObject { ["ok"] = true, ["state"] = props }
                            .ToString(Formatting.None));
                        return;
                    }
                case "set":
                case "merge":
                    {
                        // Writes are owner-only by construction: there is no user-id input, because a
                        // parameter that can only ever hold one legal value is a trap.
                        //
                        // Validate BEFORE answering. These are queued for the next flush, so we cannot
                        // report the server's verdict here -- but answering ok to a key the codec will
                        // refuse would make the page's promise resolve on a write that never happens.
                        if (!_service.TrySetOwnProp(key, body["value"], scope, out string setError))
                        {
                            request.Respond(Envelope(false, setError));
                            return;
                        }
                        request.Respond(Envelope(true, null));
                        return;
                    }
                case "delete":
                    {
                        if (!_service.TryRemoveOwnProp(key, scope, out string delError))
                        {
                            request.Respond(Envelope(false, delError));
                            return;
                        }
                        request.Respond(Envelope(true, null));
                        return;
                    }

                default:
                    request.Respond(Envelope(false, StateErrors.UnknownMessageType));
                    return;
            }
        }

        /// <summary>
        /// Synchronous mirror read for the Get User Value unit. Tries both scopes so a graph author
        /// does not have to know which subtree a prop was written into. The unit passes "me" as "".
        /// </summary>
        private string ReadForVisualScripting(string userId, string key)
        {
            if (_service == null || string.IsNullOrEmpty(key)) return "{\"exists\":false}";
            string target = string.IsNullOrEmpty(userId) ? _service.OwnRoomSessionId : ResolveRoomSessionId(userId);

            if (!_service.TryGetProp(target, key, out var value, UserPropScope.OwnerOnly)
                && !_service.TryGetProp(target, key, out value, UserPropScope.ModeratorWritable))
            {
                return "{\"exists\":false}";
            }

            return new JObject
            {
                ["exists"] = true,
                ["value"] = SdkWireCodec.Render(value),
                ["json"] = value == null ? "null" : value.ToString(Formatting.None)
            }.ToString(Formatting.None);
        }

        /// <summary>Page ids are uids; the service is keyed by roomSessionId.</summary>
        private string ResolveRoomSessionId(string uidOrId)
        {
            var users = _scene != null ? _scene.users : null;
            if (users != null)
            {
                foreach (var u in users)
                {
                    if (u != null && (u.uid == uidOrId || u.id == uidOrId)) return u.id;
                }
            }
            return uidOrId;
        }

        private UserData FindUserById(string roomSessionId)
        {
            var users = _scene != null ? _scene.users : null;
            if (users == null) return null;
            foreach (var u in users)
            {
                if (u != null && u.id == roomSessionId) return u;
            }
            return null;
        }

        private UserData FindLocalUser()
        {
            var users = _scene != null ? _scene.users : null;
            if (users == null) return null;
            foreach (var u in users)
            {
                if (u != null && u.isLocal) return u;
            }
            return null;
        }

        private static string Envelope(bool ok, string error)
        {
            var reply = new JObject { ["ok"] = ok };
            if (!ok) { reply["error"] = error; reply["message"] = StateErrors.Describe(error); }
            return reply.ToString(Formatting.None);
        }
    }
}
