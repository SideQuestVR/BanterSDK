// <mirror source="Assets/Systems/Networking/State/UserStateService.cs" sha256="a0ad8a671d0ae597fb838fcef9482dd31179b924d022c0c9b4958c677e55e65a" mode="port" />
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
        private const float MaxRetryDelay = 4f;

        private readonly object _inboxGate = new object();
        private readonly Dictionary<string, JToken> _inboxSet = new Dictionary<string, JToken>(StringComparer.Ordinal);
        private readonly HashSet<string> _inboxDelete = new HashSet<string>(StringComparer.Ordinal);
        // Sets that clear their path first: sent as delete-then-set, because the server's object set merges.
        private readonly HashSet<string> _inboxReplace = new HashSet<string>(StringComparer.Ordinal);
        // Writers waiting for the server's verdict on the set queued at a path. A flush takes them with the write, so
        // a write made while one is in flight never gets the older write's answer. Guarded by _inboxGate, like the rest.
        private readonly Dictionary<string, List<TaskCompletionSource<string>>> _inboxWaiters =
            new Dictionary<string, List<TaskCompletionSource<string>>>(StringComparer.Ordinal);
        // Paths a merge wrote over an object: _own keeps only the last write, so it can't say what the server has there.
        private readonly HashSet<string> _ownMerged = new HashSet<string>(StringComparer.Ordinal);

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
        private float _retryDelay;
        /// <summary>Server leaves currently claimed by <see cref="_own"/>; guarded by _inboxGate.</summary>
        private int _ownLeaves;
        /// <summary>Bumped by <see cref="ClearAll"/>: a flush from before it belongs to a world that has gone.</summary>
        private int _clearCount;

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
            SettleInbox(StateErrors.NotConnected);
        }

        /// <summary>Production's Update; the owning module calls it once a frame.</summary>
        public void Tick()
        {
            if (!_subscribed) return;
            if (_session.State != SessionState.Joined)
            {
                // These writes now wait for the next join, like one made while offline: accepted locally.
                SettleInbox(null);
                return;
            }
            if (_flushInFlight || Now < _nextFlushAt) return;

            lock (_inboxGate)
            {
                if (_inboxSet.Count == 0 && _inboxDelete.Count == 0) return;
            }
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
        public bool TrySetOwnProp(string key, JToken value, UserPropScope scope, out string error) =>
            TrySetOwnProp(key, value, scope, false, out error);

        /// <param name="replace">Clear what is stored at the key first. Otherwise an object merges into it, as it does
        /// on the server.</param>
        public bool TrySetOwnProp(string key, JToken value, UserPropScope scope, bool replace, out string error)
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
            // Nested member names become path segments on the server, so they need encoding too, and a refusal has
            // to come before a replace sends its delete, or the old value goes with nothing in its place.
            if (!StatePathCodec.TryEncodeMemberNames(value, out JToken encodedValue, out string memberReason))
            {
                ReportFailure(key, StateErrors.InvalidPath, memberReason);
                error = StateErrors.InvalidPath;
                return false;
            }
            value = encodedValue;
            // The same goes for the flattened paths: the server refuses the whole set if one breaks its limits.
            if (!FitsPathLimits(path, value))
            {
                ReportFailure(key, StateErrors.InvalidPath, $"'{key}' nests too deep or too long once flattened");
                error = StateErrors.InvalidPath;
                return false;
            }
            // A nested object flattens into MANY server leaves, so counting SDK keys would not bound
            // the server's 128-leaves-per-user cap at all. Count what the server will actually store.
            int newLeaves = CountLeaves(value);
            lock (_inboxGate)
            {
                int existing = _own.TryGetValue(path, out var previous) ? CountLeaves(previous) : 0;
                // A replace takes everything under the path with it, here as on the server.
                List<string> doomed = null;
                var held = new List<KeyValuePair<string, JToken>>();
                bool underOwnProp = false;
                if (replace)
                {
                    if (previous != null) held.Add(new KeyValuePair<string, JToken>(path, previous));
                    string prefix = path + ".";
                    foreach (var pair in _own)
                    {
                        if (path.StartsWith(pair.Key + ".", StringComparison.Ordinal)) underOwnProp = true;
                        if (!pair.Key.StartsWith(prefix, StringComparison.Ordinal)) continue;
                        (doomed ??= new List<string>()).Add(pair.Key);
                        held.Add(pair);
                        existing += CountLeaves(pair.Value);
                    }
                }
                if (_ownLeaves - existing + newLeaves > MaxPropLeaves)
                {
                    ReportFailure(key, StateErrors.TooManyKeys,
                        $"a participant may hold at most {MaxPropLeaves} prop leaves; '{key}' was not set");
                    error = StateErrors.TooManyKeys;
                    return false;
                }
                _ownLeaves = _ownLeaves - existing + newLeaves;
                // Clearing shows the room a delete for every leaf, so a replace only does it when an old leaf would stay
                // behind, or when _own can't tell: a value inside an object held further up, or a merge's leftovers. A
                // delete still pending stays ahead of this write, whatever kind it is.
                bool clearFirst = _inboxDelete.Remove(path)
                                  || (replace && (underOwnProp || _ownMerged.Contains(path) || LeavesOldLeaf(path, value, held)));
                if (replace) _ownMerged.Remove(path);
                else if (previous is JObject && value is JObject) _ownMerged.Add(path);
                if (doomed != null)
                {
                    foreach (string gone in doomed)
                    {
                        _own.Remove(gone);
                        _ownMerged.Remove(gone);
                        _inboxSet.Remove(gone);
                        _inboxReplace.Remove(gone);
                        MoveWaiters(gone, path);
                    }
                }
                if (clearFirst) _inboxReplace.Add(path);
                _inboxSet[path] = value ?? JValue.CreateNull();
                // Kept so a join past the 30s grace window can re-publish; see OnRoomJoined.
                _own[path] = value ?? JValue.CreateNull();
            }
            return true;
        }

        /// <summary>
        /// Set, completing with the server's verdict on this write: null when accepted, else the error code. A
        /// client-side refusal completes at once. With no room to send it to, the write waits for the join and this
        /// completes at once, accepted locally. Every path settles the task: a Visual Scripting caller has no timeout
        /// of its own.
        /// </summary>
        public Task<string> SetOwnPropAsync(string key, JToken value, UserPropScope scope, bool replace)
        {
            if (!TrySetOwnProp(key, value, scope, replace, out string error)) return Task.FromResult(error);
            if (_session.State != SessionState.Joined) return Task.FromResult<string>(null);
            TryBuildPath(key, scope, out string path, out _);
            var waiter = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_inboxGate)
            {
                if (!_inboxWaiters.TryGetValue(path, out var list)) _inboxWaiters[path] = list = new List<TaskCompletionSource<string>>();
                list.Add(waiter);
            }
            return waiter.Task;
        }

        // Call under _inboxGate.
        private void MoveWaiters(string from, string to)
        {
            if (!_inboxWaiters.TryGetValue(from, out var moved)) return;
            _inboxWaiters.Remove(from);
            if (_inboxWaiters.TryGetValue(to, out var list)) list.AddRange(moved);
            else _inboxWaiters[to] = moved;
        }

        private void SettleInbox(string code)
        {
            Dictionary<string, List<TaskCompletionSource<string>>> waiters;
            lock (_inboxGate)
            {
                if (_inboxWaiters.Count == 0) return;
                waiters = new Dictionary<string, List<TaskCompletionSource<string>>>(_inboxWaiters, StringComparer.Ordinal);
                _inboxWaiters.Clear();
            }
            Settle(waiters, code);
        }

        private static void Settle(Dictionary<string, List<TaskCompletionSource<string>>> waiters, string code)
        {
            foreach (var list in waiters.Values)
                foreach (var waiter in list)
                    waiter.TrySetResult(code);
            waiters.Clear();
        }

        /// <summary>Every leaf <paramref name="value"/> becomes under <paramref name="path"/> once the server flattens it.</summary>
        private static void CollectLeaves(string path, JToken value, HashSet<string> leaves)
        {
            if (value is JObject obj && obj.HasValues)
                foreach (var property in obj.Properties()) CollectLeaves(path + "." + property.Name, property.Value, leaves);
            else leaves.Add(path); // arrays, scalars and an empty object are leaves
        }

        /// <summary>
        /// Would setting <paramref name="value"/> at <paramref name="path"/> leave behind a leaf of what is held now
        /// (path and value, at the path and under it)? The server's set only adds and overwrites, so those are what a
        /// replace has to clear. With none, a delete first would only show the room every leaf going and coming back,
        /// where the set alone broadcasts just what changed.
        /// </summary>
        public static bool LeavesOldLeaf(string path, JToken value, IEnumerable<KeyValuePair<string, JToken>> held)
        {
            var kept = new HashSet<string>(StringComparer.Ordinal);
            CollectLeaves(path, value, kept);
            var old = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pair in held) CollectLeaves(pair.Key, pair.Value, old);
            return !old.IsSubsetOf(kept);
        }

        /// <summary>Does every path <paramref name="value"/> flattens into stay within the server's limits (state/keys.ts)?</summary>
        public static bool FitsPathLimits(string path, JToken value)
        {
            var leaves = new HashSet<string>(StringComparer.Ordinal);
            CollectLeaves(path, value, leaves);
            foreach (string leaf in leaves)
                if (leaf.Length > StatePathCodec.MaxPathChars || leaf.Split('.').Length > StatePathCodec.MaxSegments) return false;
            return true;
        }

        private static void Settle(Dictionary<string, List<TaskCompletionSource<string>>> waiters, string path, string code)
        {
            if (!waiters.TryGetValue(path, out var list)) return;
            waiters.Remove(path);
            foreach (var waiter in list) waiter.TrySetResult(code);
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
            List<TaskCompletionSource<string>> superseded;
            lock (_inboxGate)
            {
                _inboxSet.Remove(path);
                _inboxReplace.Remove(path);
                _inboxDelete.Add(path);
                if (_inboxWaiters.TryGetValue(path, out superseded)) _inboxWaiters.Remove(path);
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
                    _ownMerged.Remove(gone);
                }
                if (_ownLeaves < 0) _ownLeaves = 0;
            }
            // Their write was never sent and this one undoes it, so there is no verdict to wait for.
            if (superseded != null)
                foreach (var waiter in superseded) waiter.TrySetResult(null);
            return true;
        }

        /// <summary>Leaves the own props hold (diagnostics and tests).</summary>
        internal int OwnLeaves
        {
            get { lock (_inboxGate) return _ownLeaves; }
        }

        // What one flush took from the inbox, kept together so a failed one can go back as it came.
        private sealed class PendingFlush
        {
            public readonly List<KeyValuePair<string, JToken>> Sets = new List<KeyValuePair<string, JToken>>();
            public readonly List<string> Deletes = new List<string>();
            public readonly HashSet<string> Replaces = new HashSet<string>(StringComparer.Ordinal);
            public readonly Dictionary<string, List<TaskCompletionSource<string>>> Waiters =
                new Dictionary<string, List<TaskCompletionSource<string>>>(StringComparer.Ordinal);
            public int ClearCount;
        }

        // At most a batch's worth, or the server refuses the whole batch. The rest goes with the next flush. Deletes go
        // first and sets run parent before child, so across flushes too a delete never takes a set already sent.
        private PendingFlush TakeFlush()
        {
            var flush = new PendingFlush();
            int room = SpaceStateWriteQueue.MaxOpsPerBatch;
            lock (_inboxGate)
            {
                flush.ClearCount = _clearCount;
                foreach (string path in _inboxDelete)
                {
                    if (room == 0) break;
                    flush.Deletes.Add(path);
                    room--;
                }
                foreach (string path in flush.Deletes) _inboxDelete.Remove(path);

                var paths = new List<string>(_inboxSet.Keys);
                paths.Sort(StringComparer.Ordinal);
                foreach (string path in paths)
                {
                    bool clearFirst = _inboxReplace.Contains(path);
                    int cost = clearFirst ? 2 : 1;
                    if (cost > room) break;
                    room -= cost;
                    flush.Sets.Add(new KeyValuePair<string, JToken>(path, _inboxSet[path]));
                    if (clearFirst) flush.Replaces.Add(path);
                    if (_inboxWaiters.TryGetValue(path, out var list)) flush.Waiters[path] = list;
                    _inboxSet.Remove(path);
                    _inboxReplace.Remove(path);
                    _inboxWaiters.Remove(path);
                }
            }
            return flush;
        }

        private async void FlushAsync()
        {
            _flushInFlight = true;
            var flush = TakeFlush();

            var ops = new JArray();
            foreach (string path in flush.Deletes) ops.Add(new JObject { ["op"] = "delete", ["path"] = path });
            // A batch's ops apply in order, so every delete lands before any set.
            foreach (var pair in flush.Sets)
                if (flush.Replaces.Contains(pair.Key)) ops.Add(new JObject { ["op"] = "delete", ["path"] = pair.Key });
            foreach (var pair in flush.Sets) ops.Add(new JObject { ["op"] = "set", ["path"] = pair.Key, ["value"] = pair.Value });
            if (ops.Count == 0) { _flushInFlight = false; return; }

            try
            {
                // atomic:false so one bad op cannot take the rest of the page's props with it, and
                // the RAW variant so a batch_failed reply reaches ApplyFlushOutcomes with its
                // per-op detail instead of being thrown away as one exception.
                var result = await _client.BatchUserStateRawAsync(ops, false, _lifetime).ConfigureAwait(false);
                _post(() => ApplyFlushOutcomes(result, ops, flush));
            }
            catch (OperationCanceledException) { Settle(flush.Waiters, StateErrors.NotConnected); }
            catch (LocalRelayException ex)
            {
                string code = PacketPartyErrorCodes.FromRelay(ex.Code);
                _post(() => FailFlush(code, flush));
            }
            catch (Exception ex)
            {
                string message = ex.Message;
                _post(() =>
                {
                    Debug.LogWarning($"[LocalMP][UserState] Flush failed: {message}");
                    Settle(flush.Waiters, StateErrors.InternalError);
                });
            }
            finally
            {
                _post(() =>
                {
                    _flushInFlight = false;
                    _nextFlushAt = Now + Mathf.Max(FlushInterval, _retryDelay);
                });
            }
        }

        private void ApplyFlushOutcomes(UserStateResult result, JArray ops, PendingFlush flush)
        {
            if (result == null || result.Outcomes == null)
            {
                // Answered as a whole: rate_limited and the like come back here, not as an exception.
                if (result != null && result.Ok)
                {
                    _retryDelay = 0f;
                    Settle(flush.Waiters, null);
                }
                else FailFlush(result?.Error ?? StateErrors.InternalError, flush);
                return;
            }
            _retryDelay = 0f;
            for (int i = 0; i < result.Outcomes.Count && i < ops.Count; i++)
            {
                var outcome = result.Outcomes[i];
                string path = (string)ops[i]["path"];
                bool isSet = (string)ops[i]["op"] == "set";
                if (outcome.Ok)
                {
                    if (isSet) Settle(flush.Waiters, path, null);
                    continue;
                }
                // Deleting something that was never set is not an error worth surfacing.
                if (outcome.Error == StateErrors.NotFound && !isSet) continue;
                ReportPathFailure(path, outcome.Error);
                if (isSet) Settle(flush.Waiters, path, outcome.Error);
            }
            Settle(flush.Waiters, StateErrors.InternalError); // only a reply short of outcomes leaves any
        }

        // The whole flush failed: retried when that can help, as space state does, else surfaced.
        private void FailFlush(string code, PendingFlush flush)
        {
            if (StateErrors.IsTransient(code))
            {
                _retryDelay = _retryDelay <= 0f ? 0.25f : Mathf.Min(_retryDelay * 2f, MaxRetryDelay);
                // A few seconds in: tell the caller, inside the page's 10 s, and leave the write trying.
                if (_retryDelay >= MaxRetryDelay) Settle(flush.Waiters, code);
                Requeue(flush);
                return;
            }
            foreach (var pair in flush.Sets) ReportPathFailure(pair.Key, code);
            Settle(flush.Waiters, code);
        }

        private void Requeue(PendingFlush flush)
        {
            lock (_inboxGate)
            {
                if (flush.ClearCount != _clearCount)
                {
                    Settle(flush.Waiters, StateErrors.NotConnected);
                    return;
                }
                // Anything written since the flush started wins, but a delete half that never landed still has to. A set
                // no longer on the books was deleted, or replaced from further up, and must not come back.
                foreach (var pair in flush.Sets)
                {
                    if (!_own.ContainsKey(pair.Key)) continue;
                    if (!_inboxSet.ContainsKey(pair.Key)) _inboxSet[pair.Key] = pair.Value;
                    if (flush.Replaces.Contains(pair.Key)) _inboxReplace.Add(pair.Key);
                }
                foreach (string path in flush.Deletes)
                    if (!_inboxSet.ContainsKey(path)) _inboxDelete.Add(path);
                    else _inboxReplace.Add(path);
                foreach (var pair in flush.Waiters)
                {
                    if (!_inboxSet.ContainsKey(pair.Key))
                    {
                        // Their write is gone either way.
                        foreach (var waiter in pair.Value) waiter.TrySetResult(null);
                    }
                    else if (_inboxWaiters.TryGetValue(pair.Key, out var list)) list.AddRange(pair.Value);
                    else _inboxWaiters[pair.Key] = pair.Value;
                }
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
                _inboxReplace.Clear();
                _own.Clear();
                _ownMerged.Clear();
                _ownLeaves = 0;
                _clearCount++;
            }
            SettleInbox(StateErrors.NotConnected);
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
            // A listener's exception must not stop the flush outcomes behind it, or callers would never be answered.
            try { WriteFailed?.Invoke(key, code, message); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}
