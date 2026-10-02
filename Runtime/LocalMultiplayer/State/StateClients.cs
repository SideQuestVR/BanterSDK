// <mirror source="Packages/com.sidequest.packetparty/Runtime/PacketPartyClient.cs" sha256="6ab88cbf53c894dcdd8273685d80972419478499cfec5f09de9499a52b97367d" mode="port" />
// The room-state and user-state halves of PacketPartyClient that the state services sit on, with the signaling
// socket replaced by ILocalSession:
// - requests: BatchRoomStateAsync / RequestRoomStateRawAsync / PrepareRoomStateRequestAsync (:1985-1999, :2855-2887)
//   and BatchUserStateRawAsync / RequestUserStateRawAsync (:2903-2920);
// - inbound: the revision guards, buffering and gap recovery of OnRoomStateSnapshotSignal / OnRoomStateChangedSignal
//   (:3088-3396) and OnUserState*Signal (:3032-3086). The client's own state mirrors are left out: only the
//   revision bookkeeping decides what reaches the services.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace BS.LocalMultiplayer.State
{
    /// <summary>
    /// PacketParty's error codes for the failures <see cref="ILocalSession.RequestAsync"/> reports with its own
    /// names, so a page or graph sees the code production would give it.
    /// </summary>
    public static class PacketPartyErrorCodes
    {
        public const string NotConnected = "not_connected";
        public const string SocketClosed = "socket_closed";
        public const string SignalTimeout = "signal_timeout";

        /// <summary>"closed" → socket_closed, "timeout" → signal_timeout; every other code is already the server's.</summary>
        public static string FromRelay(string code)
        {
            switch (code)
            {
                case "closed": return SocketClosed;
                case "timeout": return SignalTimeout;
                default: return code;
            }
        }
    }

    /// <summary>
    /// The room-state surface of PacketPartyClient over the local relay: <c>app.state.batch</c> requests, and the
    /// snapshot / change events in strict revision order. A change that skips a revision is held back and a path-less
    /// <c>app.state.get</c> repairs the gap, exactly as the client does.
    /// </summary>
    public sealed class RoomStateClient
    {
        private const int MaxPendingRoomStateChanges = 256;

        private readonly ILocalSession _session;
        private readonly Action<Action> _post;
        private readonly CancellationToken _lifetime;

        private readonly SortedDictionary<long, RoomStateChangedEvent> _pendingRoomStateChanges =
            new SortedDictionary<long, RoomStateChangedEvent>();
        private long _roomStateRevision;
        private int _roomStateSyncGeneration;
        private bool _roomStateResyncInFlight;
        private bool _shuttingDown = true;

        private readonly Action<JObject> _onSnapshotSignal;
        private readonly Action<JObject> _onChangedSignal;
        private readonly Action _onRoomLeft;

        public event Action<RoomStateSnapshotEvent> OnRoomStateSnapshot;
        public event Action<RoomStateChangedEvent> OnRoomStateChanged;

        public RoomStateClient(ILocalSession session, Action<Action> post, CancellationToken lifetime)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _post = post ?? throw new ArgumentNullException(nameof(post));
            _lifetime = lifetime;
            _onSnapshotSignal = OnRoomStateSnapshotSignal;
            _onChangedSignal = OnRoomStateChangedSignal;
            _onRoomLeft = OnRoomLeft;
        }

        public ILocalSession Session => _session;

        /// <summary>The last applied room-state revision (0 until the first snapshot of a session).</summary>
        public long Revision => _roomStateRevision;

        public void Attach()
        {
            if (!_shuttingDown) return;
            _shuttingDown = false;
            _session.On(RoomStateMessages.Snapshot, _onSnapshotSignal);
            _session.On(RoomStateMessages.Changed, _onChangedSignal);
            _session.RoomLeft += _onRoomLeft;
        }

        public void Detach()
        {
            if (_shuttingDown) return;
            _shuttingDown = true;
            InvalidateRoomStateResync();
            _session.Off(RoomStateMessages.Snapshot, _onSnapshotSignal);
            _session.Off(RoomStateMessages.Changed, _onChangedSignal);
            _session.RoomLeft -= _onRoomLeft;
            OnRoomStateSnapshot = null;
            OnRoomStateChanged = null;
        }

        // ------------------------------------------------------------------ requests

        /// <summary>
        /// Non-throwing <c>app.state.batch</c>: a server-reported failure comes back as the result, so a caller can
        /// read the per-op outcomes. Not-connected and cancellation still throw.
        /// <paramref name="atomic"/> must stay true whenever the batch contains a <c>protect</c> op: protection is
        /// irreversible for the room.
        /// </summary>
        public Task<RoomStateResult> BatchRoomStateAsync(
            JArray operations,
            RoomStateScope scope = RoomStateScope.Public,
            bool atomic = true,
            CancellationToken cancellation = default)
        {
            if (operations == null) throw new ArgumentNullException(nameof(operations));
            return RequestRoomStateRawAsync(
                RoomStateMessages.Batch,
                new JObject { ["ops"] = operations, ["atomic"] = atomic },
                scope,
                cancellation);
        }

        private async Task<RoomStateResult> RequestRoomStateRawAsync(string type, JObject payload, RoomStateScope scope, CancellationToken cancellation)
        {
            // PrepareRoomStateRequestAsync: the scope rides every request. There is no runtime JWT locally; the relay
            // authorises protected writes by the role in the hello (RELAY.md 4).
            payload["scope"] = scope == RoomStateScope.Protected ? "protected" : "public";
            var body = await _session.RequestAsync(type, payload, cancellation).ConfigureAwait(false);
            return StateJson.Parse<RoomStateResult>(body)
                ?? new RoomStateResult { Ok = false, Error = "invalid_result" };
        }

        // ------------------------------------------------------------------ inbound

        private void OnRoomStateSnapshotSignal(JObject body)
        {
            if (_shuttingDown) return;
            var snapshot = TryParse<RoomStateSnapshotEvent>(body);
            if (snapshot == null || snapshot.Revision < _roomStateRevision) return;

            // A pushed snapshot supersedes any in-flight repair request from the prior
            // mirror. Its reply may still arrive, but the generation check will ignore it.
            _roomStateSyncGeneration++;
            _roomStateResyncInFlight = false;
            _roomStateRevision = snapshot.Revision;
            try { OnRoomStateSnapshot?.Invoke(snapshot); }
            catch (Exception ex) { Debug.LogException(ex); }
            DiscardPendingRoomStateChangesThrough(snapshot.Revision);
            DrainPendingRoomStateChanges();
            StartRoomStateResyncIfNeeded();
        }

        private void OnRoomStateChangedSignal(JObject body)
        {
            if (_shuttingDown) return;
            var changed = TryParse<RoomStateChangedEvent>(body);
            if (changed == null || changed.Revision <= _roomStateRevision) return;

            if (changed.Revision != _roomStateRevision + 1)
            {
                _pendingRoomStateChanges[changed.Revision] = changed;
                TrimPendingRoomStateChanges();
                StartRoomStateResyncIfNeeded();
                return;
            }

            ApplyRoomStateChange(changed);
            DrainPendingRoomStateChanges();
            StartRoomStateResyncIfNeeded();
        }

        private void ApplyRoomStateChange(RoomStateChangedEvent changed)
        {
            _roomStateRevision = changed.Revision;
            try { OnRoomStateChanged?.Invoke(changed); }
            catch (Exception ex) { Debug.LogException(ex); }
        }

        private void DrainPendingRoomStateChanges()
        {
            DiscardPendingRoomStateChangesThrough(_roomStateRevision);
            while (_pendingRoomStateChanges.TryGetValue(
                _roomStateRevision + 1,
                out RoomStateChangedEvent next))
            {
                _pendingRoomStateChanges.Remove(next.Revision);
                ApplyRoomStateChange(next);
            }
        }

        private void DiscardPendingRoomStateChangesThrough(long revision)
        {
            if (_pendingRoomStateChanges.Count == 0) return;
            var stale = new List<long>();
            foreach (var pair in _pendingRoomStateChanges)
            {
                if (pair.Key > revision) break;
                stale.Add(pair.Key);
            }
            foreach (long key in stale) _pendingRoomStateChanges.Remove(key);
        }

        private void TrimPendingRoomStateChanges()
        {
            while (_pendingRoomStateChanges.Count > MaxPendingRoomStateChanges)
            {
                long firstRevision;
                using (var enumerator = _pendingRoomStateChanges.GetEnumerator())
                {
                    if (!enumerator.MoveNext()) return;
                    firstRevision = enumerator.Current.Key;
                }
                _pendingRoomStateChanges.Remove(firstRevision);
            }
        }

        private bool HasRoomStateRevisionGap()
        {
            if (_pendingRoomStateChanges.Count == 0) return false;
            using (var enumerator = _pendingRoomStateChanges.GetEnumerator())
            {
                return enumerator.MoveNext() && enumerator.Current.Key > _roomStateRevision + 1;
            }
        }

        private void StartRoomStateResyncIfNeeded()
        {
            if (_shuttingDown || _roomStateResyncInFlight || !HasRoomStateRevisionGap()) return;
            _roomStateResyncInFlight = true;
            int generation = _roomStateSyncGeneration;
            FetchRoomStateResync(generation, retryDelayMilliseconds: 0);
        }

        // FetchRoomStateResyncAsync, split in two so the request is always issued on the main thread: the retry
        // delay runs off it and posts the fetch back.
        private void FetchRoomStateResync(int generation, int retryDelayMilliseconds)
        {
            if (retryDelayMilliseconds <= 0)
            {
                _ = RequestRoomStateResyncAsync(generation, retryDelayMilliseconds);
                return;
            }
            _ = DelayRoomStateResyncAsync(generation, retryDelayMilliseconds);
        }

        private async Task DelayRoomStateResyncAsync(int generation, int retryDelayMilliseconds)
        {
            try
            {
                await Task.Delay(retryDelayMilliseconds, _lifetime).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Teardown invalidates this recovery generation.
                return;
            }
            _post(() => { _ = RequestRoomStateResyncAsync(generation, retryDelayMilliseconds); });
        }

        private async Task RequestRoomStateResyncAsync(int generation, int retryDelayMilliseconds)
        {
            CancellationToken cancellation = _lifetime;
            try
            {
                if (_shuttingDown || generation != _roomStateSyncGeneration) return;

                JObject body = await _session.RequestAsync(
                    RoomStateMessages.Get,
                    new JObject { ["scope"] = "public" },
                    cancellation).ConfigureAwait(false);
                var result = StateJson.Parse<RoomStateResult>(body);
                _post(() => CompleteRoomStateResync(
                    generation,
                    retryDelayMilliseconds,
                    result));
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                // Connection teardown invalidates this recovery generation.
            }
            catch (Exception ex)
            {
                _post(() => CompleteRoomStateResyncFailure(
                    generation,
                    retryDelayMilliseconds,
                    ex));
            }
        }

        private void CompleteRoomStateResync(
            int generation,
            int retryDelayMilliseconds,
            RoomStateResult result)
        {
            if (_shuttingDown || generation != _roomStateSyncGeneration) return;
            if (result == null || !result.Ok || result.State == null || result.ProtectedPrefixes == null)
            {
                CompleteRoomStateResyncFailure(
                    generation,
                    retryDelayMilliseconds,
                    new LocalRelayException(
                        result?.Error ?? "invalid_result",
                        "room-state gap recovery did not return an authoritative snapshot"));
                return;
            }

            ApplyRoomStateResyncResult(result);
            if (!HasRoomStateRevisionGap())
            {
                _roomStateResyncInFlight = false;
                return;
            }

            int nextDelay = NextRoomStateResyncDelay(retryDelayMilliseconds);
            FetchRoomStateResync(generation, nextDelay);
        }

        private void CompleteRoomStateResyncFailure(
            int generation,
            int retryDelayMilliseconds,
            Exception error)
        {
            if (_shuttingDown || generation != _roomStateSyncGeneration) return;
            // Production stops when the signaling socket is gone; here, when the session is not joined.
            if (!HasRoomStateRevisionGap() || _session.State != SessionState.Joined)
            {
                _roomStateResyncInFlight = false;
                return;
            }

            Debug.Log($"[LocalMP][PacketParty] room-state gap recovery failed; retrying: {error.Message}");
            int nextDelay = NextRoomStateResyncDelay(retryDelayMilliseconds);
            FetchRoomStateResync(generation, nextDelay);
        }

        private static int NextRoomStateResyncDelay(int previousDelayMilliseconds)
        {
            if (previousDelayMilliseconds <= 0) return 100;
            return Math.Min(previousDelayMilliseconds * 2, 2000);
        }

        /// <summary>
        /// Replaces the revision from a path-less get response and re-announces it as a snapshot (values split by the
        /// returned protected prefixes), then applies any buffered revisions newer than it in strict order.
        /// </summary>
        internal void ApplyRoomStateResyncResult(RoomStateResult result)
        {
            if (result == null || !result.Ok || result.State == null ||
                result.ProtectedPrefixes == null || result.Revision < _roomStateRevision)
            {
                return;
            }

            var publicValues = new Dictionary<string, JToken>(StringComparer.Ordinal);
            var protectedValues = new Dictionary<string, JToken>(StringComparer.Ordinal);
            var protectedPrefixes = new HashSet<string>(
                result.ProtectedPrefixes,
                StringComparer.Ordinal);
            foreach (var pair in result.State)
            {
                if (IsRoomStatePathProtected(pair.Key, protectedPrefixes))
                    protectedValues[pair.Key] = pair.Value;
                else
                    publicValues[pair.Key] = pair.Value;
            }

            _roomStateRevision = result.Revision;
            var snapshot = new RoomStateSnapshotEvent
            {
                Revision = result.Revision,
                Public = publicValues,
                Protected = protectedValues,
                ProtectedPrefixes = new List<string>(result.ProtectedPrefixes)
            };
            try { OnRoomStateSnapshot?.Invoke(snapshot); }
            catch (Exception ex) { Debug.LogException(ex); }
            DiscardPendingRoomStateChangesThrough(result.Revision);
            DrainPendingRoomStateChanges();
        }

        private static bool IsRoomStatePathProtected(
            string path,
            IReadOnlyCollection<string> protectedPrefixes)
        {
            if (string.IsNullOrEmpty(path) || protectedPrefixes == null) return false;
            foreach (string prefix in protectedPrefixes)
            {
                if (string.Equals(path, prefix, StringComparison.Ordinal) ||
                    path.StartsWith(prefix + ".", StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private void InvalidateRoomStateResync()
        {
            _pendingRoomStateChanges.Clear();
            _roomStateSyncGeneration++;
            _roomStateResyncInFlight = false;
        }

        // The socket closed. Production keeps the revision only across a resume of the same session
        // (TeardownPartialAsync) and zeroes it on every hard teardown (TeardownAsync, :5294). A resumed relay session
        // can only send a newer snapshot, so zeroing on every close is the same, and it also lets a fresh session
        // after a hub restart (persisted state, possibly an older revision) through.
        private void OnRoomLeft()
        {
            InvalidateRoomStateResync();
            _roomStateRevision = 0;
        }

        private static T TryParse<T>(JObject body) where T : class
        {
            try
            {
                return StateJson.Parse<T>(body);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LocalMP][PacketParty] dropping a malformed {typeof(T).Name}: {ex.Message}");
                return null;
            }
        }
    }

    /// <summary>
    /// The user-state surface of PacketPartyClient over the local relay: <c>app.userState.batch</c> requests, and the
    /// snapshot / change / removal events, dropping anything older than the last applied revision.
    /// </summary>
    public sealed class UserStateClient
    {
        private readonly ILocalSession _session;
        private long _userStateRevision;
        private bool _attached;

        private readonly Action<JObject> _onSnapshotSignal;
        private readonly Action<JObject> _onChangedSignal;
        private readonly Action<JObject> _onRemovedSignal;
        private readonly Action _onRoomLeft;

        public event Action<UserStateSnapshotEvent> OnUserStateSnapshot;
        public event Action<UserStateChangedEvent> OnUserStateChanged;
        public event Action<UserStateRemovedEvent> OnUserStateRemoved;

        public UserStateClient(ILocalSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _onSnapshotSignal = OnUserStateSnapshotSignal;
            _onChangedSignal = OnUserStateChangedSignal;
            _onRemovedSignal = OnUserStateRemovedSignal;
            _onRoomLeft = OnRoomLeft;
        }

        public ILocalSession Session => _session;

        /// <summary>The last applied user-state revision (one counter per room).</summary>
        public long Revision => _userStateRevision;

        public void Attach()
        {
            if (_attached) return;
            _attached = true;
            _session.On(UserStateMessages.Snapshot, _onSnapshotSignal);
            _session.On(UserStateMessages.Changed, _onChangedSignal);
            _session.On(UserStateMessages.Removed, _onRemovedSignal);
            _session.RoomLeft += _onRoomLeft;
        }

        public void Detach()
        {
            if (!_attached) return;
            _attached = false;
            _session.Off(UserStateMessages.Snapshot, _onSnapshotSignal);
            _session.Off(UserStateMessages.Changed, _onChangedSignal);
            _session.Off(UserStateMessages.Removed, _onRemovedSignal);
            _session.RoomLeft -= _onRoomLeft;
            OnUserStateSnapshot = null;
            OnUserStateChanged = null;
            OnUserStateRemoved = null;
        }

        /// <summary>
        /// Non-throwing <c>app.userState.batch</c>. A <c>batch_failed</c> reply carries the per-operation outcomes
        /// that say WHICH op failed; throwing would discard exactly that. Use with <c>atomic:false</c> so one bad op
        /// cannot take the rest of the batch with it.
        /// </summary>
        public Task<UserStateResult> BatchUserStateRawAsync(JArray operations, bool atomic = true, CancellationToken cancellation = default) =>
            RequestUserStateRawAsync(UserStateMessages.Batch, new JObject
            {
                ["ops"] = operations ?? throw new ArgumentNullException(nameof(operations)),
                ["atomic"] = atomic
            }, cancellation);

        private async Task<UserStateResult> RequestUserStateRawAsync(string type, JObject payload, CancellationToken cancellation)
        {
            var body = await _session.RequestAsync(type, payload, cancellation).ConfigureAwait(false);
            return StateJson.Parse<UserStateResult>(body)
                ?? new UserStateResult { Ok = false, Error = "invalid_result" };
        }

        private void OnUserStateSnapshotSignal(JObject body)
        {
            if (!_attached) return;
            var snapshot = TryParse<UserStateSnapshotEvent>(body);
            if (snapshot == null || snapshot.Revision < _userStateRevision) return;
            _userStateRevision = snapshot.Revision;
            try { OnUserStateSnapshot?.Invoke(snapshot); }
            catch (Exception ex) { Debug.LogException(ex); }
        }

        private void OnUserStateChangedSignal(JObject body)
        {
            if (!_attached) return;
            var changed = TryParse<UserStateChangedEvent>(body);
            if (changed == null || string.IsNullOrEmpty(changed.RoomSessionId) || changed.Revision < _userStateRevision) return;
            _userStateRevision = changed.Revision;
            try { OnUserStateChanged?.Invoke(changed); }
            catch (Exception ex) { Debug.LogException(ex); }
        }

        private void OnUserStateRemovedSignal(JObject body)
        {
            if (!_attached) return;
            var removed = TryParse<UserStateRemovedEvent>(body);
            if (removed == null || string.IsNullOrEmpty(removed.RoomSessionId) || removed.Revision < _userStateRevision) return;
            _userStateRevision = removed.Revision;
            try { OnUserStateRemoved?.Invoke(removed); }
            catch (Exception ex) { Debug.LogException(ex); }
        }

        // Production zeroes the revision on every hard teardown and keeps it across a resume; a resumed relay
        // session only ever sends newer revisions, so zeroing on every close is the same (see RoomStateClient).
        private void OnRoomLeft()
        {
            _userStateRevision = 0;
        }

        private static T TryParse<T>(JObject body) where T : class
        {
            try
            {
                return StateJson.Parse<T>(body);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LocalMP][PacketParty] dropping a malformed {typeof(T).Name}: {ex.Message}");
                return null;
            }
        }
    }
}
