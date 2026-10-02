// <mirror source="Assets/Systems/Networking/State/SpaceStateService.cs" sha256="9cbcdf6f0a1e63966d18e5e1e2d4de947b0fb68033927cda28fb6c3afdc762e1" mode="port" />
// Ported line for line. Substitutions: the MonoBehaviour is a plain class SpaceStateModule ticks (Awake/Update/OnDestroy
// -> constructor/Tick/Dispose, its serialized defaults -> constants, Time.unscaledTime and the backoff's
// Random.Range(0f, 0.1f) jitter -> injectable, defaulting to exactly those);
// PacketPartyClient -> RoomStateClient over ILocalSession (OnConnectionStateChanged(Connected) -> RoomJoined, which the
// client raises right after Connected; Disconnected/Failed -> SessionState.Idle/Failed); PacketPartyException ->
// LocalRelayException; UnityMainThread.Post -> MainThreadQueue.Post; destroyCancellationToken -> the module's lifetime;
// CanWriteProtected's PlatformService.IsSignedIn -> true (every local player counts as signed in).
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace BS.LocalMultiplayer.State
{
    /// <summary>Outcome of an awaited space-state write.</summary>
    public readonly struct SpaceStateWriteResult
    {
        public bool Ok { get; }
        /// <summary>Server error string, or a client-side code (see <see cref="StateErrors"/>).</summary>
        public string Error { get; }
        /// <summary>Author-facing one-liner.</summary>
        public string Message { get; }
        /// <summary>CAS current value / read-back, when the server supplied one.</summary>
        public JToken Value { get; }

        public SpaceStateWriteResult(bool ok, string error, string message, JToken value)
        {
            Ok = ok;
            Error = error;
            Message = message;
            Value = value;
        }

        public static SpaceStateWriteResult Success(JToken value = null) => new SpaceStateWriteResult(true, null, null, value);
        public static SpaceStateWriteResult Fail(string code, JToken value = null) =>
            new SpaceStateWriteResult(false, code, StateErrors.Describe(code), value);
    }

    /// <summary>One applied change, with the caller's decoded key.</summary>
    public sealed class SpaceStateChange
    {
        public string Key;
        public JToken Value;
        public JToken PreviousValue;
        public bool Deleted;
        public bool IsPublic;
    }

    /// <summary>
    /// Greenfield's space state over the local relay: PacketParty room state, addressed by decoded SDK keys.
    ///
    /// Free of any Creator SDK dependency, as in production; <see cref="SpaceStateSdkBridge"/> translates to and from
    /// the page's string bus and Visual Scripting.
    ///
    /// Key mapping is flat: SDK key <c>k</c> is room-state path <c>k</c> (encoded by <see cref="StatePathCodec"/>),
    /// with public/protected derived from the server's own snapshot split rather than from a name prefix.
    /// </summary>
    public sealed class SpaceStateService
    {
        // Sending faster than this starves app.object.* (grabs/ownership), which draws on the same
        // 20 ops/s bucket. 5 msg/s x 64 ops = 320 key-writes/s, far more than any page produces.
        private const float _minSendInterval = 0.2f;

        // Object registration fires one CreateObjectAsync per object at join, so a 50-object space
        // burns the whole burst in the first second. Be politest exactly then.
        private const float _joinBackoffSeconds = 3f;
        private const float _joinSendInterval = 0.5f;

        private readonly SpaceStateWriteQueue _queue = new SpaceStateWriteQueue();

        // Main-thread only.
        private readonly Dictionary<string, JToken> _mirror = new Dictionary<string, JToken>(StringComparer.Ordinal);
        private readonly HashSet<string> _knownProtected = new HashSet<string>(StringComparer.Ordinal);
        private readonly WarnOnce _warned = new WarnOnce();
        private long _revision;
        private bool _haveSnapshot;
        private bool _protectedWritesDisabled;
        private bool _publicBatchDegraded;
        private float _nextSendAt;
        private float _connectedAt = float.NegativeInfinity;
        private double _backoffSeconds;
        private bool _sendInFlight;

        private readonly RoomStateClient _client;
        private readonly ILocalSession _session;
        private readonly Action<Action> _post;
        private readonly CancellationToken _lifetime;
        private readonly Func<float> _clock;
        private readonly Func<float> _jitter;
        private bool _subscribed;

        private readonly Action<RoomStateSnapshotEvent> _onSnapshot;
        private readonly Action<RoomStateChangedEvent> _onChanged;
        private readonly Action _onRoomJoined;
        private readonly Action<SessionState> _onConnectionState;

        public event Action<SpaceStateChange> Changed;
        public event Action Snapshotted;
        /// <summary>A key's public/protected classification changed; the page needs a full push.</summary>
        public event Action ScopesChanged;
        public event Action<string, string, string> WriteFailed; // key, code, message

        /// <param name="post">Runs an action on the main thread later (production: UnityMainThread.Post).</param>
        /// <param name="lifetime">Cancelled when the owner goes away (production: destroyCancellationToken).</param>
        /// <param name="clock">Seconds, production's <c>Time.unscaledTime</c>; tests pass their own.</param>
        /// <param name="jitter">The backoff jitter, production's <c>Random.Range(0f, 0.1f)</c>; tests pass their own.</param>
        public SpaceStateService(RoomStateClient client, Action<Action> post, CancellationToken lifetime,
                                 Func<float> clock = null, Func<float> jitter = null)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _session = client.Session;
            _post = post ?? throw new ArgumentNullException(nameof(post));
            _lifetime = lifetime;
            _clock = clock ?? (() => Time.unscaledTime);
            _jitter = jitter ?? (() => UnityEngine.Random.Range(0f, 0.1f));
            _onSnapshot = OnSnapshot;
            _onChanged = OnChanged;
            _onRoomJoined = OnRoomJoined;
            _onConnectionState = OnConnectionState;
            Subscribe();
        }

        public bool HasSnapshot => _haveSnapshot;
        public long Revision => _revision;

        /// <summary>Writes waiting for the relay (diagnostics and tests).</summary>
        public int PendingWrites => _queue.Count;

        private float Now => _clock();

        /// <summary>Decoded keys currently known to be protected.</summary>
        public IEnumerable<string> ProtectedKeys
        {
            get
            {
                foreach (string path in _knownProtected)
                {
                    if (StatePathCodec.TryDecodePath(path, out string key)) yield return key;
                }
            }
        }

        /// <summary>
        /// Whether a protected write is worth attempting. A denial latches it off until the next join, because the
        /// shared token bucket must not be spent on guaranteed denials. Production also requires a signed-in
        /// player; every local player counts as signed in.
        /// </summary>
        public bool CanWriteProtected => !_protectedWritesDisabled;

        /// <summary>Production's OnDestroy.</summary>
        public void Dispose()
        {
            Unsubscribe();
        }

        /// <summary>Production's Update; the owning module calls it once a frame.</summary>
        public void Tick()
        {
            if (!_subscribed) return;

            bool connected = _session.State == SessionState.Joined
                             && !string.IsNullOrEmpty(_session.OwnRoomSessionId);
            if (!connected)
            {
                foreach (var stale in _queue.DropStale(DateTime.UtcNow))
                    ReportFailure(stale, StateErrors.Dropped);
                return;
            }

            if (_sendInFlight || Now < _nextSendAt) return;
            if (_queue.Count == 0) return;
            PumpAsync();
        }

        private void Subscribe()
        {
            if (_subscribed) return;
            _subscribed = true;
            _client.OnRoomStateSnapshot += _onSnapshot;
            _client.OnRoomStateChanged += _onChanged;
            _session.RoomJoined += _onRoomJoined;
            _session.StateChanged += _onConnectionState;
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            _subscribed = false;
            _client.OnRoomStateSnapshot -= _onSnapshot;
            _client.OnRoomStateChanged -= _onChanged;
            _session.RoomJoined -= _onRoomJoined;
            _session.StateChanged -= _onConnectionState;
        }

        // ------------------------------------------------------------------ reading

        public bool TryGet(string key, out JToken value)
        {
            value = null;
            return StatePathCodec.TryEncodePath(key, out string path, out _) && _mirror.TryGetValue(path, out value);
        }

        public string GetString(string key) => TryGet(key, out var value) ? SdkWireCodec.Render(value) : string.Empty;

        /// <summary>True when the key is under a protected prefix the server has confirmed.</summary>
        public bool IsProtected(string key) =>
            StatePathCodec.TryEncodePath(key, out string path, out _) && IsPathProtected(path);

        private bool IsPathProtected(string path)
        {
            foreach (string prefix in _knownProtected)
            {
                // Mirrors the server's isPathUnderPrefix: segment-aware, so "matches" is not under "match".
                if (path == prefix || path.StartsWith(prefix + ".", StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>True when any known protected prefix sits UNDER this path.</summary>
        private bool HasProtectedDescendant(string path)
        {
            string probe = path + ".";
            foreach (string prefix in _knownProtected)
            {
                if (prefix.StartsWith(probe, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private bool MirrorHasSubtree(string path)
        {
            string probe = path + ".";
            foreach (string existing in _mirror.Keys)
            {
                if (existing == path || existing.StartsWith(probe, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>Every mirrored entry as decoded key + value, for the bridge's page projection.</summary>
        public IEnumerable<KeyValuePair<string, JToken>> Entries()
        {
            foreach (var pair in _mirror)
            {
                if (StatePathCodec.TryDecodePath(pair.Key, out string key))
                    yield return new KeyValuePair<string, JToken>(key, pair.Value);
            }
        }

        public bool IsKeyPublic(string key) => !IsProtected(key);

        // ------------------------------------------------------------------ writing

        public void Set(string key, JToken value, RoomStateScope scope) => EnqueueWrite(key, value, StateWriteKind.Set, scope);
        public void Replace(string key, JToken value, RoomStateScope scope) => EnqueueWrite(key, value, StateWriteKind.Replace, scope);
        public void Delete(string key, RoomStateScope scope) => EnqueueWrite(key, null, StateWriteKind.Delete, scope);

        private void EnqueueWrite(string key, JToken value, StateWriteKind kind, RoomStateScope scope)
        {
            if (SdkWireCodec.IsSentinel(key)) return; // client-local; never leaves the process

            if (!StatePathCodec.TryEncodePath(key, out string path, out string reason))
            {
                ReportFailure(key, StateErrors.InvalidPath, reason);
                return;
            }
            if (value != null && SdkWireCodec.ExceedsValueLimit(value, out int bytes))
            {
                ReportFailure(key, StateErrors.ValueTooLarge,
                    $"value for '{key}' is {bytes / 1024} KB; the limit is {SdkWireCodec.MaxValueBytes / 1024} KB");
                return;
            }
            // Nested member names become path segments server-side, so they need encoding too --
            // and must be rejected BEFORE a replace sends its delete half, or the delete lands and
            // the set is refused, destroying the subtree with nothing to replace it.
            if (!StatePathCodec.TryEncodeMemberNames(value, out JToken encodedValue, out string memberReason))
            {
                ReportFailure(key, StateErrors.InvalidPath, memberReason);
                return;
            }
            _queue.Enqueue(path, encodedValue, kind, scope, DateTime.UtcNow);
        }

        private int SlotsFor(PendingWrite write)
        {
            int slots = write.Kind == StateWriteKind.Replace && MirrorHasSubtree(write.Path) ? 2 : 1;
            if (write.Scope == RoomStateScope.Protected) slots += 1; // the protect op
            return slots;
        }

        private async void PumpAsync()
        {
            _sendInFlight = true;
            List<PendingWrite> taken = null;
            try
            {
                taken = _queue.TakeBatch(_publicBatchDegraded ? 1 : SpaceStateWriteQueue.MaxOpsPerBatch, SlotsFor);
                if (taken.Count == 0) return;

                RoomStateScope scope = taken[0].Scope;
                var sendable = new List<PendingWrite>();
                var ops = new JArray();

                var slots = new List<int>();
                foreach (var write in taken)
                {
                    // A refusal MUST leave the queue. Left pending it would pin _order[0], and
                    // TakeBatch reads the lane's scope from there -- so one permanently-refused
                    // write silently starves the other scope for the rest of the session, while
                    // the empty-batch return below re-entered the pump every frame.
                    if (!PreCheck(write)) { _queue.Drop(write); continue; }

                    int before = ops.Count;
                    AppendOps(ops, write);
                    // Captured now, not recomputed after the round trip: MirrorHasSubtree moves as
                    // soon as this batch's own broadcast lands, which would misalign every later
                    // per-op outcome and blame the wrong key.
                    slots.Add(ops.Count - before);
                    sendable.Add(write);
                }

                if (ops.Count == 0)
                {
                    _nextSendAt = Now + CurrentSendInterval();
                    return;
                }

                // Protected batches MUST be atomic: the store's atomic early-return happens before
                // it persists, so a non-atomic batch whose set fails can still leave the protect
                // raised -- and protection is irreversible for the room.
                bool atomic = scope == RoomStateScope.Protected;
                RoomStateResult result;
                try
                {
                    result = await _client.BatchRoomStateAsync(ops, scope, atomic, _lifetime).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { return; }
                catch (LocalRelayException ex) when (ex.Code == PacketPartyErrorCodes.NotConnected)
                {
                    return; // everything stays pending; the next Connected tick resumes
                }
                catch (Exception ex)
                {
                    string message = ex.Message;
                    // Everything after the await runs off the main thread (ConfigureAwait(false)),
                    // and ScheduleBackoff reads the clock and UnityEngine.Random -- both main-thread-only.
                    _post(() =>
                    {
                        Debug.LogWarning($"[LocalMP][SpaceState] Batch failed: {message}");
                        ScheduleBackoff();
                    });
                    return;
                }

                _post(() => HandleBatchResult(result, sendable, slots, ops, scope, atomic));
            }
            finally
            {
                _post(() => _sendInFlight = false);
            }
        }

        /// <summary>Local refusals that must never spend a rate-limit token.</summary>
        private bool PreCheck(PendingWrite write)
        {
            bool protectedPath = IsPathProtected(write.Path);

            if (write.Scope == RoomStateScope.Public && protectedPath)
            {
                ReportFailure(write, StateErrors.ProtectedPath);
                return false;
            }
            if (write.Scope == RoomStateScope.Protected && !CanWriteProtected)
            {
                ReportFailure(write, StateErrors.NotAuthorized);
                return false;
            }
            // The server's delete guard only tests the deleted path itself, while the delete wipes
            // the whole subtree -- so a public delete of an ancestor would silently take protected
            // descendants with it. Refuse locally.
            if ((write.Kind == StateWriteKind.Delete || write.Kind == StateWriteKind.Replace)
                && write.Scope == RoomStateScope.Public
                && HasProtectedDescendant(write.Path))
            {
                ReportFailure(write, StateErrors.ProtectedPath);
                return false;
            }
            return true;
        }

        private void AppendOps(JArray ops, PendingWrite write)
        {
            // Re-asserted on every protected SET, not just the first: applyProtect is an idempotent
            // no-op when already covered, protect never broadcasts (so our view of other clients'
            // protections is permanently stale), and it costs no extra message.
            //
            // Deliberately NOT on a delete: protecting a path you are removing would permanently
            // lock a now-empty key for the room's 24h lifetime, which no author would expect from
            // "delete this". A protected delete of an already-protected path still passes the
            // guard, because scope:"protected" alone invokes it server-side.
            if (write.Scope == RoomStateScope.Protected && write.Kind != StateWriteKind.Delete)
                ops.Add(new JObject { ["op"] = "protect", ["path"] = write.Path });

            switch (write.Kind)
            {
                case StateWriteKind.Delete:
                    ops.Add(new JObject { ["op"] = "delete", ["path"] = write.Path });
                    break;

                case StateWriteKind.Replace:
                    // The server's object-set MERGES, so a true replace needs the subtree gone
                    // first -- but only when there is something to delete: an atomic batch aborts
                    // on the delete's not_found, which would break every first write of a new key.
                    if (MirrorHasSubtree(write.Path))
                        ops.Add(new JObject { ["op"] = "delete", ["path"] = write.Path });
                    ops.Add(new JObject { ["op"] = "set", ["path"] = write.Path, ["value"] = write.Value ?? JValue.CreateNull() });
                    break;

                default:
                    ops.Add(new JObject { ["op"] = "set", ["path"] = write.Path, ["value"] = write.Value ?? JValue.CreateNull() });
                    break;
            }
        }

        private void HandleBatchResult(RoomStateResult result, List<PendingWrite> sendable, List<int> slots,
                                      JArray ops, RoomStateScope scope, bool atomic)
        {
            if (result == null) { ScheduleBackoff(); return; }

            if (result.Ok)
            {
                _queue.Ack(sendable);
                _backoffSeconds = 0;
                _nextSendAt = Now + CurrentSendInterval();
                if (scope == RoomStateScope.Protected) NoteProtected(sendable);
                if (_publicBatchDegraded && ops.Count > 1) _publicBatchDegraded = false;
                return;
            }

            switch (result.Error)
            {
                case StateErrors.RateLimited:
                case StateErrors.SessionNotFound:
                case StateErrors.InternalError:
                    ScheduleBackoff(); // keep everything pending; never surfaced to the author
                    return;

                case StateErrors.ProtectedPath:
                    // A batch reply cannot say WHICH op offended, so drop the public lane to one op
                    // per message until a multi-op batch succeeds; each single failure then names
                    // its key exactly.
                    _publicBatchDegraded = true;
                    if (sendable.Count == 1) DiscoverProtected(sendable[0]);
                    ScheduleBackoff();
                    return;

                case StateErrors.BatchFailed:
                    ApplyOutcomes(result, sendable, slots, ops, atomic);
                    _nextSendAt = Now + CurrentSendInterval();
                    return;

                case StateErrors.PolicyDenied:
                case StateErrors.AppUnavailable:
                // The webhook's own denials. Latch them too: this client is not a moderator of this
                // space, so every further protected write is a guaranteed denial that would still
                // spend a token from the bucket shared with synced objects.
                case StateErrors.NotAuthorized:
                case StateErrors.NotSpaceAdmin:
                case StateErrors.JwtMissing:
                    _protectedWritesDisabled = true;
                    FailAll(sendable, result.Error);
                    return;

                case StateErrors.InvalidBatch:
                case StateErrors.InvalidScope:
                case StateErrors.UnknownMessageType:
                    // Our bug, not the author's.
                    _warned.Once("proto:" + result.Error,
                        () => Debug.LogError($"[LocalMP][SpaceState] Server rejected our batch as '{result.Error}'. This is a client bug."));
                    FailAll(sendable, result.Error);
                    return;

                default:
                    FailAll(sendable, result.Error ?? StateErrors.InternalError);
                    return;
            }
        }

        /// <summary>Map per-op outcomes back onto the writes that produced them.</summary>
        private void ApplyOutcomes(RoomStateResult result, List<PendingWrite> sendable, List<int> slotCounts,
                                   JArray ops, bool atomic)
        {
            if (result.Outcomes == null || result.Outcomes.Count != ops.Count || slotCounts.Count != sendable.Count)
            {
                FailAll(sendable, result.Error ?? StateErrors.BatchFailed);
                return;
            }

            int opIndex = 0;
            for (int index = 0; index < sendable.Count; index++)
            {
                var write = sendable[index];
                int slots = slotCounts[index];
                bool ok = true;
                string error = null;
                for (int i = 0; i < slots && opIndex + i < result.Outcomes.Count; i++)
                {
                    var outcome = result.Outcomes[opIndex + i];
                    if (outcome.Ok) continue;
                    // A Replace's delete half matching nothing is not a failure.
                    bool benign = outcome.Error == StateErrors.NotFound
                                  && (string)ops[opIndex + i]["op"] == "delete"
                                  && write.Kind == StateWriteKind.Replace;
                    if (benign) continue;
                    ok = false;
                    error = outcome.Error;
                }
                opIndex += slots;

                if (!ok)
                {
                    _queue.Drop(write);
                    ReportFailure(write, error ?? StateErrors.BatchFailed);
                }
                else if (!atomic)
                {
                    _queue.Ack(write);
                }
                // An ATOMIC batch that reports batch_failed persisted NOTHING -- the store's
                // early-return happens before it writes -- so an op whose own outcome says ok was
                // still rolled back. Leave it pending to be retried once the offending write above
                // has been dropped, or it is silently lost.
            }
        }

        private void FailAll(List<PendingWrite> sendable, string code)
        {
            foreach (var write in sendable)
            {
                _queue.Drop(write);
                ReportFailure(write, code);
            }
            _nextSendAt = Now + CurrentSendInterval();
        }

        private void NoteProtected(List<PendingWrite> sendable)
        {
            bool added = false;
            foreach (var write in sendable)
            {
                if (_knownProtected.Add(write.Path)) added = true;
            }
            if (added) ScopesChanged?.Invoke();
        }

        private void DiscoverProtected(PendingWrite write)
        {
            if (!_knownProtected.Add(write.Path)) return;
            ScopesChanged?.Invoke();
            // Retry once as protected when this client could plausibly be allowed.
            if (CanWriteProtected)
                _queue.Enqueue(write.Path, write.Value, write.Kind, RoomStateScope.Protected, DateTime.UtcNow);
            else
                ReportFailure(write, StateErrors.NotAuthorized);
        }

        private float CurrentSendInterval()
        {
            float baseInterval = Now - _connectedAt < _joinBackoffSeconds ? _joinSendInterval : _minSendInterval;
            return Mathf.Max(baseInterval, (float)_backoffSeconds);
        }

        private void ScheduleBackoff()
        {
            _backoffSeconds = _backoffSeconds <= 0 ? 0.25 : Math.Min(_backoffSeconds * 2, 4.0);
            // Jitter so a room full of clients does not resynchronise onto the same retry tick.
            _nextSendAt = Now + (float)_backoffSeconds + _jitter();
        }

        // ------------------------------------------------------------------ awaited API

        public Task<SpaceStateWriteResult> SetAsync(string key, JToken value, RoomStateScope scope, CancellationToken ct = default) =>
            SingleAsync(key, value, StateWriteKind.Set, scope, ct);

        public Task<SpaceStateWriteResult> ReplaceAsync(string key, JToken value, RoomStateScope scope, CancellationToken ct = default) =>
            SingleAsync(key, value, StateWriteKind.Replace, scope, ct);

        public Task<SpaceStateWriteResult> DeleteAsync(string key, RoomStateScope scope, CancellationToken ct = default) =>
            SingleAsync(key, null, StateWriteKind.Delete, scope, ct);

        private async Task<SpaceStateWriteResult> SingleAsync(
            string key, JToken value, StateWriteKind kind, RoomStateScope scope, CancellationToken ct)
        {
            if (SdkWireCodec.IsSentinel(key)) return SpaceStateWriteResult.Fail(StateErrors.InvalidPath);
            if (!StatePathCodec.TryEncodePath(key, out string path, out string reason))
                return new SpaceStateWriteResult(false, StateErrors.InvalidPath, reason, null);
            if (value != null && SdkWireCodec.ExceedsValueLimit(value, out _))
                return SpaceStateWriteResult.Fail(StateErrors.ValueTooLarge);
            if (scope == RoomStateScope.Protected && !CanWriteProtected)
                return SpaceStateWriteResult.Fail(StateErrors.NotAuthorized);
            if (scope == RoomStateScope.Public && IsPathProtected(path))
                return SpaceStateWriteResult.Fail(StateErrors.ProtectedPath);
            // The same shallow-delete guard the queued path applies. The server's delete check only
            // tests the deleted path itself while the delete wipes the whole subtree, so without
            // this a public SpaceStateDelete("a") silently destroys a protected "a.b".
            if ((kind == StateWriteKind.Delete || kind == StateWriteKind.Replace)
                && scope == RoomStateScope.Public
                && HasProtectedDescendant(path))
                return SpaceStateWriteResult.Fail(StateErrors.ProtectedPath);
            if (_session.State != SessionState.Joined)
                return SpaceStateWriteResult.Fail(StateErrors.NotConnected);
            if (!StatePathCodec.TryEncodeMemberNames(value, out JToken encodedValue, out string memberReason))
                return new SpaceStateWriteResult(false, StateErrors.InvalidPath, memberReason, null);

            var write = new PendingWrite { Path = path, Value = encodedValue, Kind = kind, Scope = scope };
            var ops = new JArray();
            AppendOps(ops, write);

            try
            {
                var result = await _client.BatchRoomStateAsync(ops, scope, scope == RoomStateScope.Protected, ct);
                if (result.Ok)
                {
                    // Post the whole mutation: _knownProtected is main-thread-only (production expects this
                    // continuation off it).
                    if (scope == RoomStateScope.Protected && write.Kind != StateWriteKind.Delete)
                    {
                        _post(() =>
                        {
                            if (_knownProtected.Add(path)) ScopesChanged?.Invoke();
                        });
                    }
                    return SpaceStateWriteResult.Success(result.Value);
                }
                string code = FirstFailure(result) ?? result.Error ?? StateErrors.InternalError;
                return SpaceStateWriteResult.Fail(code, result.Value);
            }
            catch (OperationCanceledException) { throw; }
            catch (LocalRelayException ex) { return SpaceStateWriteResult.Fail(PacketPartyErrorCodes.FromRelay(ex.Code)); }
            catch (Exception ex) { return new SpaceStateWriteResult(false, StateErrors.InternalError, ex.Message, null); }
        }

        private static string FirstFailure(RoomStateResult result)
        {
            if (result.Outcomes == null) return null;
            foreach (var outcome in result.Outcomes)
            {
                if (!outcome.Ok && !string.IsNullOrEmpty(outcome.Error)) return outcome.Error;
            }
            return null;
        }

        // ------------------------------------------------------------------ inbound

        private void OnSnapshot(RoomStateSnapshotEvent snapshot)
        {
            if (snapshot == null) return;

            _mirror.Clear();
            Absorb(snapshot.Public);
            Absorb(snapshot.Protected);

            // The snapshot split is the ONLY authoritative source of protection: `protect` never
            // broadcasts, and a path-less `get` returns public and protected merged with no prefix
            // list. Our own confirmed protects are kept -- protection is raise-only, so the server
            // can never have less than we have seen succeed.
            _knownProtected.UnionWith(snapshot.ProtectedPrefixes ?? new List<string>());
            foreach (var pair in snapshot.Protected ?? new Dictionary<string, JToken>())
                _knownProtected.Add(pair.Key);

            _revision = snapshot.Revision;
            _haveSnapshot = true;
            _publicBatchDegraded = false;

            Snapshotted?.Invoke();
            ScopesChanged?.Invoke();
        }

        private void Absorb(Dictionary<string, JToken> entries)
        {
            if (entries == null) return;
            foreach (var pair in entries) _mirror[pair.Key] = pair.Value;
        }

        private void OnChanged(RoomStateChangedEvent change)
        {
            if (change == null || change.Changes == null) return;

            // Room state, unlike user state, has no revision guard in the client, so a dropped or
            // reordered broadcast would diverge the mirror silently and permanently.
            if (_haveSnapshot && change.Revision > _revision + 1)
            {
                _warned.Once("gap", () => Debug.LogWarning(
                    $"[LocalMP][SpaceState] Revision gap ({_revision} -> {change.Revision}); the mirror may be stale until the next snapshot."));
            }
            _revision = change.Revision;

            foreach (var entry in change.Changes)
            {
                if (entry == null || string.IsNullOrEmpty(entry.Path)) continue;
                if (!StatePathCodec.TryDecodePath(entry.Path, out string key)) continue; // not ours

                _mirror.TryGetValue(entry.Path, out JToken previous);
                if (entry.Deleted) _mirror.Remove(entry.Path);
                else _mirror[entry.Path] = entry.Value;

                // `scope` on a broadcast merely echoes the writer's assertion, so it is a reliable
                // NEGATIVE only: a public write to a protected path is refused and never broadcast.
                bool isPublic = !IsPathProtected(entry.Path);
                if (isPublic && string.Equals(change.Scope, "public", StringComparison.Ordinal)
                    && _knownProtected.Remove(entry.Path))
                {
                    ScopesChanged?.Invoke();
                }

                Changed?.Invoke(new SpaceStateChange
                {
                    Key = key,
                    Value = entry.Deleted ? null : entry.Value,
                    PreviousValue = previous,
                    Deleted = entry.Deleted,
                    IsPublic = isPublic
                });
            }
        }

        // OnConnectionState(Connected): the client raises Connected and then OnRoomJoined back to back after
        // room.ready, on a fresh join and on a resume alike.
        private void OnRoomJoined()
        {
            _connectedAt = Now;
            _backoffSeconds = 0;
            _protectedWritesDisabled = false;
        }

        private void OnConnectionState(SessionState state)
        {
            if (state == SessionState.Idle || state == SessionState.Failed)
            {
                // Keep the mirror: the next snapshot rebuilds it, and dropping it would blank the
                // page mid-reconnect.
                _sendInFlight = false;
                _publicBatchDegraded = false;
            }
        }

        /// <summary>Space unload: nothing queued for the old room may leak into the new one.</summary>
        public void ClearAll()
        {
            _queue.Clear();
            _mirror.Clear();
            _knownProtected.Clear();
            _haveSnapshot = false;
            _revision = 0;
            _protectedWritesDisabled = false;
            _publicBatchDegraded = false;
        }

        // ------------------------------------------------------------------ failures

        private void ReportFailure(PendingWrite write, string code)
        {
            string key = StatePathCodec.TryDecodePath(write.Path, out string decoded) ? decoded : write.Path;
            ReportFailure(key, code, null);
        }

        private void ReportFailure(string key, string code, string detail)
        {
            string message = detail ?? StateErrors.Describe(code);
            if (StateErrors.IsOurBug(code))
                _warned.Once($"{code}:{key}", () => Debug.LogError($"[LocalMP][SpaceState] '{key}': {message}"));
            else
                _warned.Once($"{code}:{key}", () => Debug.LogWarning($"[LocalMP][SpaceState] '{key}': {message}"));
            WriteFailed?.Invoke(key, code, message);
        }
    }
}
