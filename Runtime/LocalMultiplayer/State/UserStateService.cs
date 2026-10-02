// <mirror source="Assets/Systems/Networking/State/UserStateService.cs" sha256="2aae33b0f429c6a20568d31ef5ffa8a67cd98b96463948c6a09aaeede6f0f711" mode="port" />
// Ported line for line. Substitutions: the MonoBehaviour is a plain class UserStateModule ticks (Awake/Update/OnDestroy
// -> constructor/Tick/Dispose, Time.unscaledTime -> an injectable clock); PacketPartyClient -> UserStateClient over
// ILocalSession (OnRoomJoined -> ILocalSession.RoomJoined, State == Connected -> SessionState.Joined);
// PacketPartyException -> LocalRelayException; UnityMainThread.Post -> MainThreadQueue.Post; destroyCancellationToken ->
// the module's lifetime. TryBuildPath, TryParsePath and CountLeaves are internal (not private) for the tests.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace BS.LocalMultiplayer.State
{
    /// <summary>
    /// Who may write a per-user prop.
    ///
    /// Deliberately NOT called public/protected: room state's scopes mean the opposite thing
    /// (there, "protected" plus authorization means you MAY write). Neither scope affects
    /// readability — every participant can read every other participant's state, in both domains.
    /// </summary>
    public enum UserPropScope
    {
        /// <summary>Only the owning user, ever. Enforced by the server today.</summary>
        OwnerOnly,
        /// <summary>The owner, plus authorized moderators once the server supports it.</summary>
        ModeratorWritable
    }

    /// <summary>
    /// Per-player props on PacketParty user state over the local relay: ephemeral, room-session scoped, and
    /// owner-writes-only (the relay derives the owner from the connection and ignores any
    /// client-supplied one).
    ///
    /// Content-authored props live under a reserved <c>props.{prot|pub}</c> subtree so they are
    /// STRUCTURALLY unable to collide with the engine's own root keys (<c>pilot</c>,
    /// <c>attachment_*</c>, ...), which travel on the session's engine user-state channel.
    ///
    /// Page props get their own non-atomic batch, so a single page key the server refuses can never fail the
    /// engine's atomic batch and stop attachments propagating for the rest of the session.
    /// </summary>
    public sealed class UserStateService
    {
        /// <summary>Reserved root for content-authored props.</summary>
        public const string PropsRoot = "props";
        private const string OwnerOnlySegment = "prot";
        private const string ModeratorWritableSegment = "pub";

        /// <summary>Server cap is 128 leaves per user; leave headroom for the engine's own keys.</summary>
        public const int MaxPropLeaves = 64;

        private const float FlushInterval = 0.1f;

        private readonly object _inboxGate = new object();
        private readonly Dictionary<string, JToken> _inboxSet = new Dictionary<string, JToken>(StringComparer.Ordinal);
        private readonly HashSet<string> _inboxDelete = new HashSet<string>(StringComparer.Ordinal);

        // Main-thread only.
        private readonly Dictionary<string, JToken> _own = new Dictionary<string, JToken>(StringComparer.Ordinal);
        private readonly Dictionary<string, Dictionary<string, JToken>> _peers =
            new Dictionary<string, Dictionary<string, JToken>>(StringComparer.Ordinal);
        private readonly WarnOnce _warned = new WarnOnce();

        private readonly UserStateClient _client;
        private readonly ILocalSession _session;
        private readonly Action<Action> _post;
        private readonly CancellationToken _lifetime;
        private readonly Func<float> _clock;
        private bool _subscribed;
        private float _nextFlushAt;
        private bool _flushInFlight;
        /// <summary>Server leaves currently claimed by <see cref="_own"/>; guarded by _inboxGate.</summary>
        private int _ownLeaves;

        private readonly Action<UserStateSnapshotEvent> _onSnapshot;
        private readonly Action<UserStateChangedEvent> _onChanged;
        private readonly Action<UserStateRemovedEvent> _onRemoved;
        private readonly Action _onRoomJoined;

        /// <summary>(roomSessionId, key, value, previous). Value is null when the prop was removed.</summary>
        public event Action<string, string, JToken, JToken> PropChanged;
        /// <summary>A participant's state was dropped wholesale (they left past the grace window).</summary>
        public event Action<string> PropsRemoved;
        public event Action<string, string, string> WriteFailed; // key, code, message

        /// <param name="post">Runs an action on the main thread later (production: UnityMainThread.Post).</param>
        /// <param name="lifetime">Cancelled when the owner goes away (production: destroyCancellationToken).</param>
        /// <param name="clock">Seconds, production's <c>Time.unscaledTime</c>; tests pass their own.</param>
        public UserStateService(UserStateClient client, Action<Action> post, CancellationToken lifetime, Func<float> clock = null)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _session = client.Session;
            _post = post ?? throw new ArgumentNullException(nameof(post));
            _lifetime = lifetime;
            _clock = clock ?? (() => Time.unscaledTime);
            _onSnapshot = OnSnapshot;
            _onChanged = OnChanged;
            _onRemoved = OnRemoved;
            _onRoomJoined = OnRoomJoined;
            Subscribe();
        }

        private float Now => _clock();

        /// <summary>Production's OnDestroy.</summary>
        public void Dispose()
        {
            Unsubscribe();
        }

        /// <summary>Production's Update; the owning module calls it once a frame.</summary>
        public void Tick()
        {
            if (!_subscribed) return;
            if (_flushInFlight || Now < _nextFlushAt) return;

            lock (_inboxGate)
            {
                if (_inboxSet.Count == 0 && _inboxDelete.Count == 0) return;
            }
            if (_session.State != SessionState.Joined) return;
            FlushAsync();
        }

        private void Subscribe()
        {
            if (_subscribed) return;
            _subscribed = true;
            _client.OnUserStateSnapshot += _onSnapshot;
            _client.OnUserStateChanged += _onChanged;
            _client.OnUserStateRemoved += _onRemoved;
            _session.RoomJoined += _onRoomJoined;
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            _subscribed = false;
            _client.OnUserStateSnapshot -= _onSnapshot;
            _client.OnUserStateChanged -= _onChanged;
            _client.OnUserStateRemoved -= _onRemoved;
            _session.RoomJoined -= _onRoomJoined;
        }

        // ------------------------------------------------------------------ paths

        private static string ScopeSegment(UserPropScope scope) =>
            scope == UserPropScope.ModeratorWritable ? ModeratorWritableSegment : OwnerOnlySegment;

        /// <summary>Build the server path for a content key. Never inspects the key for an existing
        /// prefix — a caller's literal "props.x" simply nests, harmlessly, inside the reserved root.</summary>
        internal static bool TryBuildPath(string key, UserPropScope scope, out string path, out string reason)
        {
            path = null;
            if (!StatePathCodec.TryEncodePath(key, out string encoded, out reason)) return false;
            path = PropsRoot + "." + ScopeSegment(scope) + "." + encoded;
            if (path.Length > StatePathCodec.MaxPathChars)
            {
                reason = $"'{key}' is too long once namespaced";
                return false;
            }
            return true;
        }

        /// <summary>Split a server path back into a content key, or false when it isn't ours.</summary>
        internal static bool TryParsePath(string path, out string key, out UserPropScope scope)
        {
            key = null;
            scope = UserPropScope.OwnerOnly;
            if (string.IsNullOrEmpty(path)) return false;

            string ownerPrefix = PropsRoot + "." + OwnerOnlySegment + ".";
            string moderatorPrefix = PropsRoot + "." + ModeratorWritableSegment + ".";

            string rest;
            if (path.StartsWith(ownerPrefix, StringComparison.Ordinal))
            {
                rest = path.Substring(ownerPrefix.Length);
                scope = UserPropScope.OwnerOnly;
            }
            else if (path.StartsWith(moderatorPrefix, StringComparison.Ordinal))
            {
                rest = path.Substring(moderatorPrefix.Length);
                scope = UserPropScope.ModeratorWritable;
            }
            else return false;

            return StatePathCodec.TryDecodePath(rest, out key);
        }

        // ------------------------------------------------------------------ writing (own only)

        public void SetOwnProp(string key, JToken value, UserPropScope scope = UserPropScope.OwnerOnly) =>
            TrySetOwnProp(key, value, scope, out _);

        /// <summary>
        /// As <see cref="SetOwnProp"/>, but reports a client-side refusal so an awaited caller can
        /// reject rather than resolving on a write that will never be sent.
        /// </summary>
        public bool TrySetOwnProp(string key, JToken value, UserPropScope scope, out string error)
        {
            error = null;
            if (!TryBuildPath(key, scope, out string path, out string reason))
            {
                ReportFailure(key, StateErrors.InvalidPath, reason);
                error = StateErrors.InvalidPath;
                return false;
            }
            if (value != null && SdkWireCodec.ExceedsValueLimit(value, out _))
            {
                ReportFailure(key, StateErrors.ValueTooLarge, null);
                error = StateErrors.ValueTooLarge;
                return false;
            }
            // A nested object flattens into MANY server leaves, so counting SDK keys would not bound
            // the server's 128-leaves-per-user cap at all. Count what the server will actually store.
            int newLeaves = CountLeaves(value);
            lock (_inboxGate)
            {
                int existing = _own.TryGetValue(path, out var previous) ? CountLeaves(previous) : 0;
                if (_ownLeaves - existing + newLeaves > MaxPropLeaves)
                {
                    ReportFailure(key, StateErrors.TooManyKeys,
                        $"a participant may hold at most {MaxPropLeaves} prop leaves; '{key}' was not set");
                    error = StateErrors.TooManyKeys;
                    return false;
                }
                _ownLeaves = _ownLeaves - existing + newLeaves;
                _inboxDelete.Remove(path);
                _inboxSet[path] = value ?? JValue.CreateNull();
                // Kept so a join past the 30s grace window can re-publish; see OnRoomJoined.
                _own[path] = value ?? JValue.CreateNull();
            }
            return true;
        }

        /// <summary>How many dotted leaves this value becomes once the server flattens it.</summary>
        internal static int CountLeaves(JToken value)
        {
            if (value == null) return 1;
            if (value.Type != JTokenType.Object) return 1;   // arrays and scalars are leaves
            var obj = (JObject)value;
            if (!obj.HasValues) return 1;                    // an empty object is stored as a leaf
            int total = 0;
            foreach (var property in obj.Properties()) total += CountLeaves(property.Value);
            return total;
        }

        public void RemoveOwnProp(string key, UserPropScope scope = UserPropScope.OwnerOnly) =>
            TryRemoveOwnProp(key, scope, out _);

        /// <summary>As <see cref="RemoveOwnProp"/>, reporting a client-side refusal.</summary>
        public bool TryRemoveOwnProp(string key, UserPropScope scope, out string error)
        {
            error = null;
            if (!TryBuildPath(key, scope, out string path, out string reason))
            {
                ReportFailure(key, StateErrors.InvalidPath, reason);
                error = StateErrors.InvalidPath;
                return false;
            }
            lock (_inboxGate)
            {
                _inboxSet.Remove(path);
                _inboxDelete.Add(path);
                // A server delete removes the path AND its descendants, so the local record must
                // shed the whole subtree too -- otherwise the rejoin republish would resurrect
                // children the author just deleted.
                string prefix = path + ".";
                var doomed = new List<string>();
                foreach (var existing in _own.Keys)
                    if (existing == path || existing.StartsWith(prefix, StringComparison.Ordinal)) doomed.Add(existing);
                foreach (var gone in doomed)
                {
                    _ownLeaves -= CountLeaves(_own[gone]);
                    _own.Remove(gone);
                }
                if (_ownLeaves < 0) _ownLeaves = 0;
            }
            return true;
        }

        /// <summary>Leaves the own props hold (diagnostics and tests).</summary>
        internal int OwnLeaves
        {
            get { lock (_inboxGate) return _ownLeaves; }
        }

        private async void FlushAsync()
        {
            _flushInFlight = true;
            Dictionary<string, JToken> sets;
            List<string> deletes;
            lock (_inboxGate)
            {
                sets = new Dictionary<string, JToken>(_inboxSet, StringComparer.Ordinal);
                deletes = new List<string>(_inboxDelete);
                _inboxSet.Clear();
                _inboxDelete.Clear();
            }

            var ops = new JArray();
            foreach (string path in deletes) ops.Add(new JObject { ["op"] = "delete", ["path"] = path });
            foreach (var pair in sets) ops.Add(new JObject { ["op"] = "set", ["path"] = pair.Key, ["value"] = pair.Value });
            if (ops.Count == 0) { _flushInFlight = false; return; }

            try
            {
                // atomic:false so one bad op cannot take the rest of the page's props with it, and
                // the RAW variant so a batch_failed reply reaches ApplyFlushOutcomes with its
                // per-op detail instead of being thrown away as one exception.
                var result = await _client.BatchUserStateRawAsync(ops, false, _lifetime).ConfigureAwait(false);
                _post(() => ApplyFlushOutcomes(result, ops));
            }
            catch (OperationCanceledException) { }
            catch (LocalRelayException ex)
            {
                string code = PacketPartyErrorCodes.FromRelay(ex.Code);
                _post(() =>
                {
                    if (StateErrors.IsTransient(code)) Requeue(sets, deletes);
                    else foreach (var pair in sets) ReportPathFailure(pair.Key, code);
                });
            }
            catch (Exception ex)
            {
                string message = ex.Message;
                _post(() => Debug.LogWarning($"[LocalMP][UserState] Flush failed: {message}"));
            }
            finally
            {
                _post(() =>
                {
                    _flushInFlight = false;
                    _nextFlushAt = Now + FlushInterval;
                });
            }
        }

        private void ApplyFlushOutcomes(UserStateResult result, JArray ops)
        {
            if (result == null || result.Outcomes == null) return;
            for (int i = 0; i < result.Outcomes.Count && i < ops.Count; i++)
            {
                var outcome = result.Outcomes[i];
                if (outcome.Ok) continue;
                // Deleting something that was never set is not an error worth surfacing.
                if (outcome.Error == StateErrors.NotFound && (string)ops[i]["op"] == "delete") continue;
                ReportPathFailure((string)ops[i]["path"], outcome.Error);
            }
        }

        private void Requeue(Dictionary<string, JToken> sets, List<string> deletes)
        {
            lock (_inboxGate)
            {
                // Anything written since the flush started wins.
                foreach (var pair in sets)
                    if (!_inboxSet.ContainsKey(pair.Key) && !_inboxDelete.Contains(pair.Key)) _inboxSet[pair.Key] = pair.Value;
                foreach (string path in deletes)
                    if (!_inboxSet.ContainsKey(path)) _inboxDelete.Add(path);
            }
        }

        /// <summary>Force the pending props out and await the result.</summary>
        public async Task FlushNowAsync(CancellationToken ct = default)
        {
            while (true)
            {
                lock (_inboxGate)
                {
                    if (_inboxSet.Count == 0 && _inboxDelete.Count == 0) return;
                }
                if (_session.State != SessionState.Joined) return;
                if (!_flushInFlight) FlushAsync();
                await Task.Yield();
                ct.ThrowIfCancellationRequested();
            }
        }

        // ------------------------------------------------------------------ reading

        public bool TryGetProp(string roomSessionId, string key, out JToken value, UserPropScope scope = UserPropScope.OwnerOnly)
        {
            value = null;
            if (roomSessionId == null) return false;
            if (!TryBuildPath(key, scope, out string path, out _)) return false;
            return _peers.TryGetValue(roomSessionId, out var props) && props.TryGetValue(path, out value);
        }

        /// <summary>Every content prop for a participant, keyed by decoded content key.</summary>
        public IEnumerable<KeyValuePair<string, JToken>> Props(string roomSessionId)
        {
            if (roomSessionId == null || !_peers.TryGetValue(roomSessionId, out var props)) yield break;
            foreach (var pair in props)
            {
                if (TryParsePath(pair.Key, out string key, out _))
                    yield return new KeyValuePair<string, JToken>(key, pair.Value);
            }
        }

        public string OwnRoomSessionId => _session.OwnRoomSessionId;

        // ------------------------------------------------------------------ inbound

        private void OnSnapshot(UserStateSnapshotEvent snapshot)
        {
            if (snapshot == null) return;
            _peers.Clear();
            foreach (var pair in snapshot.Users)
            {
                var props = new Dictionary<string, JToken>(StringComparer.Ordinal);
                foreach (var leaf in pair.Value)
                {
                    if (TryParsePath(leaf.Key, out _, out _)) props[leaf.Key] = leaf.Value;
                }
                if (props.Count > 0) _peers[pair.Key] = props;
            }

            foreach (var pair in _peers)
            {
                foreach (var leaf in pair.Value)
                {
                    if (TryParsePath(leaf.Key, out string key, out _))
                        PropChanged?.Invoke(pair.Key, key, leaf.Value, null);
                }
            }
        }

        private void OnChanged(UserStateChangedEvent changed)
        {
            if (changed == null || changed.Changes == null || string.IsNullOrEmpty(changed.RoomSessionId)) return;

            if (!_peers.TryGetValue(changed.RoomSessionId, out var props))
            {
                props = new Dictionary<string, JToken>(StringComparer.Ordinal);
                _peers[changed.RoomSessionId] = props;
            }

            foreach (var change in changed.Changes)
            {
                if (change?.Path == null) continue;
                if (!TryParsePath(change.Path, out string key, out _)) continue; // engine key, not ours

                props.TryGetValue(change.Path, out JToken previous);
                if (change.Deleted) props.Remove(change.Path);
                else props[change.Path] = change.Value;

                PropChanged?.Invoke(changed.RoomSessionId, key, change.Deleted ? null : change.Value, previous);
            }
        }

        private void OnRemoved(UserStateRemovedEvent removed)
        {
            if (removed == null || string.IsNullOrEmpty(removed.RoomSessionId)) return;
            _peers.Remove(removed.RoomSessionId);
            PropsRemoved?.Invoke(removed.RoomSessionId);
        }

        /// <summary>
        /// Re-publish our props on every join. User state is dropped when a session ends past the
        /// 30 s grace window, and we do not ride the engine channel's automatic reconnect push,
        /// so this is what makes props survive a long reconnect.
        /// </summary>
        private void OnRoomJoined()
        {
            lock (_inboxGate)
            {
                if (_own.Count == 0) return;
                foreach (var pair in _own)
                {
                    if (!_inboxDelete.Contains(pair.Key)) _inboxSet[pair.Key] = pair.Value;
                }
            }
        }

        public void ClearAll()
        {
            lock (_inboxGate)
            {
                _inboxSet.Clear();
                _inboxDelete.Clear();
                _own.Clear();
                _ownLeaves = 0;
            }
            _peers.Clear();
        }

        private void ReportPathFailure(string path, string code)
        {
            string key = TryParsePath(path, out string decoded, out _) ? decoded : path;
            ReportFailure(key, code, null);
        }

        private void ReportFailure(string key, string code, string detail)
        {
            string message = detail ?? StateErrors.Describe(code);
            _warned.Once($"{code}:{key}", () => Debug.LogWarning($"[LocalMP][UserState] '{key}': {message}"));
            WriteFailed?.Invoke(key, code, message);
        }
    }
}
