// <mirror source="Assets/Systems/Networking/State/SpaceStateSdkBridge.cs" sha256="130ff7be5e004265b58850cf476bb9f9ffa179914c62537db2a4175b40fd1b6e" mode="port" />
// Ported line for line. Substitutions: the MonoBehaviour is a plain class SpaceStateModule ticks; the service is
// handed in instead of polled from SpaceStateService.Instance; BSScene.Instance() -> BSScene.Current (never builds a
// scene during teardown); the GetSpaceStateValue delegate is put back on unhook when it is still ours.
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Events;

namespace BS.LocalMultiplayer.State
{
    /// <summary>
    /// Translates <see cref="SpaceStateService"/> to and from the Creator SDK's surfaces: the
    /// page's string bus and the Visual Scripting event bus.
    ///
    /// This half owns everything SDK-shaped — sanitisation, the legacy wire format, the client-local
    /// sentinels — so the service underneath stays SDK-free.
    /// </summary>
    public sealed class SpaceStateSdkBridge
    {
        private BSScene _scene;
        private BSSceneEvents _events;
        private SpaceStateService _service;
        private bool _serviceHooked;

        // Cached so AddListener/RemoveListener always see the same delegate instances.
        private readonly UnityAction<string, string> _onPagePublicWrite;
        private readonly UnityAction<string, string> _onPageProtectedWrite;
        private readonly UnityAction<BSStateRequest> _onStateRequest;
        private readonly UnityAction _onSceneReady;
        private readonly UnityAction _onSpaceUnload;
        private readonly Func<string, string> _readForVisualScripting;
        private Func<string, string> _previousGetSpaceStateValue;

        private readonly Action<SpaceStateChange> _onChanged;
        private readonly Action _pushFull;
        private readonly Action<string, string, string> _onWriteFailed;

        public SpaceStateSdkBridge(SpaceStateService service)
        {
            _onPagePublicWrite = OnPagePublicWrite;
            _onPageProtectedWrite = OnPageProtectedWrite;
            _onStateRequest = OnStateRequest;
            _onSceneReady = OnSceneReady;
            _onSpaceUnload = OnSpaceUnload;
            _readForVisualScripting = ReadForVisualScripting;
            _onChanged = OnChanged;
            _pushFull = PushFull;
            _onWriteFailed = OnWriteFailed;
            HookService(service);
        }

        /// <summary>Production's Update: re-hooks the scene whenever the SDK swaps it.</summary>
        public void Tick()
        {
            EnsureSceneHooked();
        }

        /// <summary>Production's OnDestroy.</summary>
        public void Dispose()
        {
            UnhookScene();
            UnhookService();
        }

        private void HookService(SpaceStateService service)
        {
            if (service == null || ReferenceEquals(service, _service)) return;

            UnhookService();
            _service = service;
            _service.Changed += _onChanged;
            _service.Snapshotted += _pushFull;
            _service.ScopesChanged += _pushFull;
            _service.WriteFailed += _onWriteFailed;
            _serviceHooked = true;
        }

        private void UnhookService()
        {
            if (!_serviceHooked || _service == null) return;
            _service.Changed -= _onChanged;
            _service.Snapshotted -= _pushFull;
            _service.ScopesChanged -= _pushFull;
            _service.WriteFailed -= _onWriteFailed;
            _service = null;
            _serviceHooked = false;
        }

        /// <summary>
        /// Re-subscribe whenever the SDK swaps scene or events objects.
        /// <c>BSScene.Destroy()</c> calls <c>events.RemoveAllListeners()</c> AND nulls the
        /// singleton, and a fresh scene brings a fresh <c>BSSceneEvents</c> — so a
        /// bridge that latched a "subscribed" flag once goes permanently
        /// deaf after a scene teardown, silently, with no error anywhere.
        /// </summary>
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
            _events.OnPublicSpaceStateChanged.AddListener(_onPagePublicWrite);
            _events.OnProtectedSpaceStateChanged.AddListener(_onPageProtectedWrite);
            _events.OnStateRequest.AddListener(_onStateRequest);
            _events.OnSceneReady.AddListener(_onSceneReady);
            _events.OnLoad.AddListener(_onSpaceUnload);
            // Synchronous mirror read for the Get Space Value unit, which has no control flow to
            // await on.
            _previousGetSpaceStateValue = _events.GetSpaceStateValue;
            _events.GetSpaceStateValue = _readForVisualScripting;
            Debug.Log("[LocalMP][SpaceState] Bridge subscribed - SDK space state <-> local relay room state.");
        }

        private void UnhookScene()
        {
            if (_events == null) return;
            _events.OnPublicSpaceStateChanged.RemoveListener(_onPagePublicWrite);
            _events.OnProtectedSpaceStateChanged.RemoveListener(_onPageProtectedWrite);
            _events.OnStateRequest.RemoveListener(_onStateRequest);
            _events.OnSceneReady.RemoveListener(_onSceneReady);
            _events.OnLoad.RemoveListener(_onSpaceUnload);
            if (_events.GetSpaceStateValue == _readForVisualScripting)
            {
                _events.GetSpaceStateValue = _previousGetSpaceStateValue;
            }
            _previousGetSpaceStateValue = null;
            _events = null;
            _scene = null;
        }

        // ------------------------------------------------------------------ page -> server

        // Legacy writes are strings, always. Auto-detecting JSON here would silently change
        // behaviour for pages that legitimately store JSON-looking text; the new API is where real
        // JSON enters.
        private void OnPagePublicWrite(string key, string value) =>
            _service?.Set(key, SdkWireCodec.ParseLegacy(value), RoomStateScope.Public);

        private void OnPageProtectedWrite(string key, string value) =>
            _service?.Set(key, SdkWireCodec.ParseLegacy(value), RoomStateScope.Protected);

        // ------------------------------------------------------------------ server -> page

        private void OnChanged(SpaceStateChange change)
        {
            if (_scene == null || _scene.link == null || change == null) return;

            string newValue = change.Deleted ? string.Empty : SdkWireCodec.Render(change.Value);
            string oldValue = SdkWireCodec.Render(change.PreviousValue);

            // Echoes of our own writes are delivered and must be: scene.ts's SetProp only emits, it
            // never updates the page's local copy, so the echo is how the page learns.
            _scene.link.OnSpaceStateChanged(
                SdkWireCodec.San(change.Key), SdkWireCodec.San(newValue), SdkWireCodec.San(oldValue), change.IsPublic);

            // The JSON surface carries the real value, unsanitised, alongside the legacy string.
            _scene.link.OnSpaceStateJson(SdkWireCodec.ToBase64Json(new JObject
            {
                ["revision"] = _service != null ? _service.Revision : 0,
                ["full"] = false,
                ["changes"] = new JArray
                {
                    new JObject
                    {
                        ["path"] = change.Key,
                        ["value"] = change.Deleted ? null : change.Value,
                        ["oldValue"] = change.PreviousValue,
                        ["scope"] = change.IsPublic ? "public" : "protected",
                        ["deleted"] = change.Deleted
                    }
                }
            }));

            TriggerVisualScripting(change.Key, newValue, change.IsPublic);
            EventBus.Trigger("OnSpaceStateValueChanged", new CustomEventArgs(change.Key, new object[]
            {
                newValue,
                change.Deleted || change.Value == null ? string.Empty : change.Value.ToString(Formatting.None),
                change.IsPublic,
                change.Deleted
            }));
        }

        private void OnSceneReady()
        {
            // A page reload re-fires scene-ready and needs the whole state again.
            if (_service != null && _service.HasSnapshot) PushFull();
        }

        private void OnSpaceUnload() => _service?.ClearAll();

        /// <summary>
        /// Send the entire state as one message.
        ///
        /// This is also the ONLY way a delete or a public/protected reclassification reaches the
        /// page: the incremental <c>spc!</c> message assigns into one scope map and can never
        /// remove from the other, whereas <c>fss!</c> replaces <c>spaceState</c> wholesale and the
        /// page diffs it, emitting <c>newValue: null</c> for whatever vanished.
        /// </summary>
        private void PushFull()
        {
            if (_scene == null || _scene.link == null || _service == null || !_service.HasSnapshot) return;

            var publicObj = new JObject();
            var protectedObj = new JObject();

            foreach (var entry in _service.Entries())
            {
                // A room-state key that happens to spell a sentinel must never reach the page as
                // one: it would let any writer forge an ss_err, or fake the revision.
                if (SdkWireCodec.IsSentinel(entry.Key)) continue;
                string rendered = SdkWireCodec.San(SdkWireCodec.Render(entry.Value));
                string key = SdkWireCodec.San(entry.Key);
                if (string.IsNullOrEmpty(key)) continue;
                if (_service.IsKeyPublic(entry.Key)) publicObj[key] = rendered;
                else protectedObj[key] = rendered;
            }

            // Client-local only; stripped from anything arriving from the page so they can never
            // round-trip to the server.
            publicObj[SdkWireCodec.RevisionKey] = _service.Revision.ToString();
            publicObj[SdkWireCodec.ProtectedKeysKey] = SdkWireCodec.San(string.Join(",", _service.ProtectedKeys));
            publicObj[SdkWireCodec.CanProtectKey] = _service.CanWriteProtected ? "1" : "0";

            var root = new JObject { ["protected"] = protectedObj, ["public"] = publicObj };
            _scene.link.OnFullSpaceState(root.ToString(Formatting.None));

            // Visual Scripting has no full-state message, only per-key events -- so without this a
            // graph joining a populated room (or reconnecting) sees nothing at all until some key
            // happens to change. The previous bridge fired these on every snapshot; keep that.
            foreach (var entry in _service.Entries())
            {
                bool entryIsPublic = _service.IsKeyPublic(entry.Key);
                string rendered = SdkWireCodec.Render(entry.Value);
                TriggerVisualScripting(entry.Key, rendered, entryIsPublic);
                EventBus.Trigger("OnSpaceStateValueChanged", new CustomEventArgs(entry.Key, new object[]
                {
                    rendered,
                    entry.Value == null ? string.Empty : entry.Value.ToString(Formatting.None),
                    entryIsPublic,
                    false
                }));
            }

            // Same snapshot, real JSON values, for the typed mirror.
            var jsonPublic = new JObject();
            var jsonProtected = new JObject();
            foreach (var entry in _service.Entries())
                (_service.IsKeyPublic(entry.Key) ? jsonPublic : jsonProtected)[entry.Key] = entry.Value;

            _scene.link.OnFullSpaceStateJson(SdkWireCodec.ToBase64Json(new JObject
            {
                ["revision"] = _service.Revision,
                ["full"] = true,
                ["canProtect"] = _service.CanWriteProtected,
                ["public"] = jsonPublic,
                ["protected"] = jsonProtected
            }));
        }

        /// <summary>
        /// Surface a refusal the author can act on. The legacy <c>Set*Props</c> path is
        /// fire-and-forget and cannot return anything, so failures ride a reserved property on the
        /// existing wire — the page just filters <c>space-state-changed</c> for <c>ss_err</c>.
        /// </summary>
        private void OnWriteFailed(string key, string code, string message)
        {
            if (_scene == null || _scene.link == null) return;

            var payload = new JObject { ["key"] = key, ["code"] = code, ["message"] = message };
            _scene.link.OnSpaceStateChanged(
                SdkWireCodec.ErrorKey, SdkWireCodec.San(payload.ToString(Formatting.None)), string.Empty, true);

            EventBus.Trigger("OnSpaceStateError", new CustomEventArgs(key, new object[] { code, message }));
        }

        // ------------------------------------------------------------------ JSON API

        /// <summary>
        /// Serve one <c>!sso!</c> op. <see cref="BSStateRequest.Handled"/> must be set SYNCHRONOUSLY
        /// — after Invoke returns, an unset flag is how BSScene detects that nothing is listening
        /// and answers the page itself rather than leaving its promise hanging.
        /// </summary>
        private void OnStateRequest(BSStateRequest request)
        {
            if (request == null || request.Target != BSStateTarget.Space) return;
            request.Handled = true;
            ServeAsync(request);
        }

        private async void ServeAsync(BSStateRequest request)
        {
            try
            {
                if (_service == null) { request.Respond(Envelope(false, StateErrors.NotConnected)); return; }

                JObject body;
                try { body = JObject.Parse(request.Json ?? "{}"); }
                catch (JsonException) { request.Respond(Envelope(false, StateErrors.InvalidValue)); return; }

                string path = (string)body["path"];
                bool isProtected = (bool?)body["protected"] ?? false;
                var scope = isProtected ? RoomStateScope.Protected : RoomStateScope.Public;
                JToken value = body["value"];

                switch (request.Op)
                {
                    case "get":
                    {
                        bool found = _service.TryGet(path, out JToken current);
                        var reply = new JObject { ["ok"] = true };
                        if (found) reply["value"] = current;
                        request.Respond(reply.ToString(Formatting.None));
                        return;
                    }
                    case "getAll":
                    {
                        var pub = new JObject();
                        var prot = new JObject();
                        foreach (var entry in _service.Entries())
                            (_service.IsKeyPublic(entry.Key) ? pub : prot)[entry.Key] = entry.Value;
                        request.Respond(new JObject
                        {
                            ["ok"] = true,
                            ["revision"] = _service.Revision,
                            ["public"] = pub,
                            ["protected"] = prot
                        }.ToString(Formatting.None));
                        return;
                    }
                    case "set":
                    {
                        // Replace semantics: the server's object-set MERGES, so a true replace has
                        // to clear the subtree first.
                        var result = await _service.ReplaceAsync(path, value, scope);
                        request.Respond(Envelope(result));
                        return;
                    }
                    case "merge":
                    {
                        var result = await _service.SetAsync(path, value, scope);
                        request.Respond(Envelope(result));
                        return;
                    }
                    case "delete":
                    {
                        var result = await _service.DeleteAsync(path, scope);
                        request.Respond(Envelope(result));
                        return;
                    }
                    default:
                        request.Respond(Envelope(false, StateErrors.UnknownMessageType));
                        return;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LocalMP][SpaceState] '{request.Op}' failed: {ex.Message}");
                request.Respond(Envelope(false, StateErrors.InternalError));
            }
        }

        private string ReadForVisualScripting(string key)
        {
            if (_service == null || string.IsNullOrEmpty(key)) return "{\"exists\":false}";
            if (!_service.TryGet(key, out JToken value)) return "{\"exists\":false}";
            return new JObject
            {
                ["exists"] = true,
                ["value"] = SdkWireCodec.Render(value),
                ["json"] = value == null ? "null" : value.ToString(Formatting.None),
                ["isPublic"] = _service.IsKeyPublic(key)
            }.ToString(Formatting.None);
        }

        private static string Envelope(bool ok, string error) =>
            new JObject { ["ok"] = ok, ["error"] = error }.ToString(Formatting.None);

        private static string Envelope(SpaceStateWriteResult result)
        {
            var reply = new JObject { ["ok"] = result.Ok };
            if (!result.Ok)
            {
                reply["error"] = result.Error;
                reply["message"] = result.Message;
            }
            if (result.Value != null) reply["value"] = result.Value;
            return reply.ToString(Formatting.None);
        }

        private static void TriggerVisualScripting(string key, string value, bool isPublic)
        {
            // Argument order the OnSpaceStatePropsChanged node reads: (value, isPublic).
            // BSStarterUpper's offline loopback steps aside while BSNetworkHost.Active, so this is
            // the only producer, as under GREENFIELD_PROJECT.
            EventBus.Trigger("OnSpaceStatePropsChanged", new CustomEventArgs(key, new object[] { value, isPublic }));
        }
    }
}
