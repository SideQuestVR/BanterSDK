// <mirror source="Packages/com.sidequest.packetparty/Runtime/Components/PacketPartyObjectBinder.cs" sha256="bac0c961188024bf6450bfd881d15e9b50e2b6cf9b151c560c16735401c076ca" mode="port" />
// <mirror source="Packages/com.sidequest.packetparty/Runtime/PacketPartyClient.cs" sha256="6ab88c53c894dcdd8273685d80972419478499cfec5f09de9499a52b97367d" mode="port" />
// The scene binder (PacketPartyObjectBinder.cs:181-352) together with the object half of the client it sits on
// (PacketPartyClient.cs:2001-2390): the record mirror and IsOwnedBySelf, the app.object.* requests with their
// result-record application, the lifecycle broadcasts, and the payload lane with its sequence gates. PacketPartyClient
// is replaced by ILocalSession: requests go through RequestAsync, broadcasts arrive through On(type), the reliable
// object data channel is the relay's obj.payload message (RELAY.md 6.4), and Connected/Disconnected are RoomJoined /
// RoomLeft. The binder is created by SyncedObjectsModule, so there is no static Instance or pending set.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace BS.LocalMultiplayer.Objects
{
    /// <summary>
    /// The hub between the relay session and the <see cref="LocalNetworkObject"/> components: it mirrors the room's
    /// object registry (records arrive in the join snapshot, in broadcasts and in request replies), dispatches each
    /// record to the component bound to its id, and carries component payloads. Components register on enable and are
    /// held pending until the room is joined. Main thread only.
    /// </summary>
    public sealed class LocalObjectBinder : IDisposable
    {
        // The per-object receive gate of the payload lane. Production resets it on ownerChanged; a gate recorded for
        // another owner or generation is ignored, which also covers ownership that changed while this player was away.
        struct InboundGate
        {
            public int Seq;
            public string Owner;
            public uint Generation;

            public bool Matches(SyncedObjectRecord record)
                => record != null && Generation == record.OwnershipGeneration
                    && string.Equals(Owner, record.OwnerRoomSessionId, StringComparison.Ordinal);
        }

        readonly ILocalHost _host;
        readonly ILocalSession _session;

        // PacketPartyClient._objects / _objectsRevision: the locally mirrored registry.
        readonly Dictionary<string, SyncedObjectRecord> _objects = new Dictionary<string, SyncedObjectRecord>(StringComparer.Ordinal);
        long _objectsRevision;
        // PacketPartyClient._objectOutSeq / _objectInSeq, keyed by objectId: the relay has one (reliable) object lane.
        readonly Dictionary<string, int> _objectOutSeq = new Dictionary<string, int>(StringComparer.Ordinal);
        readonly Dictionary<string, InboundGate> _objectInSeq = new Dictionary<string, InboundGate>(StringComparer.Ordinal);

        // PacketPartyObjectBinder._bound / _pending.
        readonly Dictionary<string, LocalNetworkObject> _bound = new Dictionary<string, LocalNetworkObject>(StringComparer.Ordinal);
        readonly List<LocalNetworkObject> _pending = new List<LocalNetworkObject>();
        // Every component this binder configured, bound or not: diagnostics ask whether an id exists here.
        readonly HashSet<LocalNetworkObject> _known = new HashSet<LocalNetworkObject>();
        readonly List<LocalNetworkObject> _scratch = new List<LocalNetworkObject>();

        readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        readonly Action<JObject> _onSnapshot;
        readonly Action<JObject> _onCreated;
        readonly Action<JObject> _onDeleted;
        readonly Action<JObject> _onOwnerChanged;
        readonly Action<JObject> _onUpdated;
        readonly Action<JObject> _onPayload;

        bool _joined;
        bool _activatePending;
        bool _disposed;
        int _sceneLoadSerial;
        // Bumped whenever the mirror starts over (a join, or the session ending): a request reply from an earlier
        // mirror generation must not be applied to this one.
        int _mirrorSerial;
        string _lastOwnRoomSessionId;

        /// <summary>A record arrived in a snapshot or a created broadcast (for Id diagnostics).</summary>
        public event Action<SyncedObjectRecord> RecordDeclared;
        /// <summary>A record was deleted (for Id diagnostics).</summary>
        public event Action<string> RecordRemoved;
        /// <summary>The mirror was replaced (snapshot) or cleared (the session ended).</summary>
        public event Action MirrorReset;
        /// <summary>
        /// The connection dropped or the session ended: publishers and remote views start over (production's
        /// Transforms.ResetForReconnect / Reset).
        /// </summary>
        public event Action Disconnected;

        public LocalObjectBinder(ILocalHost host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _session = host.Session ?? throw new ArgumentException("the host has no session", nameof(host));
            _onSnapshot = OnObjectSnapshotSignal;
            _onCreated = OnObjectCreatedSignal;
            _onDeleted = OnObjectDeletedSignal;
            _onOwnerChanged = OnObjectOwnerChangedSignal;
            _onUpdated = OnObjectUpdatedSignal;
            _onPayload = HandleObjectLaneMessage;
            _session.On(ObjectMessages.Snapshot, _onSnapshot);
            _session.On(ObjectMessages.Created, _onCreated);
            _session.On(ObjectMessages.Deleted, _onDeleted);
            _session.On(ObjectMessages.OwnerChanged, _onOwnerChanged);
            _session.On(ObjectMessages.Updated, _onUpdated);
            _session.On(ObjectLaneMessages.Payload, _onPayload);
            _session.RoomJoined += HandleRoomJoined;
            _session.RoomLeft += HandleRoomLeft;
            _session.StateChanged += HandleStateChanged;
            if (_session.State == SessionState.Joined && !string.IsNullOrEmpty(_session.OwnRoomSessionId))
            {
                // Installed into a live session (never the normal order: the host installs before the join).
                _joined = true;
                _lastOwnRoomSessionId = _session.OwnRoomSessionId;
            }
        }

        public ILocalSession Session => _session;
        public ILocalHost Host => _host;

        /// <summary>Locally mirrored object registry (hydrated by the join snapshot + broadcasts).</summary>
        public IReadOnlyDictionary<string, SyncedObjectRecord> Objects => _objects;
        public long ObjectsRevision => _objectsRevision;

        /// <summary>This player's canonical ownership identity for the current session.</summary>
        public string OwnRoomSessionId => _session.OwnRoomSessionId;

        /// <summary>Joined: the room's snapshots are applied and requests may be sent.</summary>
        public bool IsJoined => _joined && !_disposed && _session.State == SessionState.Joined;

        /// <summary>
        /// Bumped on every space load (BSSceneEvents.OnLoad) and every new room session (not a resumed one); network
        /// objects declare again on the next join.
        /// </summary>
        public int SceneLoadSerial => _sceneLoadSerial;

        /// <summary>Cancelled when the binder is disposed (local multiplayer stopped).</summary>
        public CancellationToken Lifetime => _lifetime.Token;

        /// <summary>True when this player's room session owns the record.</summary>
        public bool IsOwnedBySelf(SyncedObjectRecord record)
        {
            if (record == null) return false;
            string own = _session.OwnRoomSessionId;
            return !string.IsNullOrEmpty(own) && string.Equals(record.OwnerRoomSessionId, own, StringComparison.Ordinal);
        }

        public SyncedObjectRecord GetRecord(string objectId)
            => objectId != null && _objects.TryGetValue(objectId, out var record) ? record : null;

        public bool TryGetBound(string objectId, out LocalNetworkObject networkObject)
        {
            if (objectId != null && _bound.TryGetValue(objectId, out networkObject) && networkObject != null) return true;
            networkObject = null;
            return false;
        }

        /// <summary>Whether a network object with this scene id was set up here (bound, pending or disabled).</summary>
        public bool HasLocalObject(string sceneObjectId)
        {
            foreach (var networkObject in _known)
            {
                if (networkObject != null && string.Equals(networkObject.SceneObjectId, sceneObjectId, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>The network objects this binder configured.</summary>
        public IReadOnlyCollection<LocalNetworkObject> KnownObjects => _known;

        /// <summary>
        /// Where a remote player holds an object (PacketPartyObjectBinder.AttachmentAnchorResolver, which Greenfield
        /// points at RemoteAvatarService.ResolveAnchor): the orb avatar's "left", "right" or "head".
        /// </summary>
        public Transform ResolveAttachmentAnchor(string ownerRoomSessionId, string anchorKey)
        {
            var bones = _host.Get<IRemoteBoneSource>();
            return bones != null ? bones.ResolveHeldAnchor(ownerRoomSessionId, anchorKey) : null;
        }

        /// <summary>The space is (re)loading: the next join declares every object again (see LocalNetworkObject).</summary>
        public void NotifySceneLoad()
        {
            _sceneLoadSerial++;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _session.Off(ObjectMessages.Snapshot, _onSnapshot);
            _session.Off(ObjectMessages.Created, _onCreated);
            _session.Off(ObjectMessages.Deleted, _onDeleted);
            _session.Off(ObjectMessages.OwnerChanged, _onOwnerChanged);
            _session.Off(ObjectMessages.Updated, _onUpdated);
            _session.Off(ObjectLaneMessages.Payload, _onPayload);
            _session.RoomJoined -= HandleRoomJoined;
            _session.RoomLeft -= HandleRoomLeft;
            _session.StateChanged -= HandleStateChanged;
            try { _lifetime.Cancel(); }
            catch (ObjectDisposedException) { }
            _lifetime.Dispose();
            _bound.Clear();
            _pending.Clear();
            _known.Clear();
            _objects.Clear();
            _objectOutSeq.Clear();
            _objectInSeq.Clear();
            RecordDeclared = null;
            RecordRemoved = null;
            MirrorReset = null;
            Disconnected = null;
        }

        // ─── Component registration ─────────────────────────────────────────────

        /// <summary>Called by <see cref="LocalNetworkObject"/> when it enables.</summary>
        internal void Register(LocalNetworkObject networkObject)
        {
            if (networkObject == null || _disposed) return;
            _known.Add(networkObject);
            if (!IsJoined || _activatePending || string.IsNullOrEmpty(_session.OwnRoomSessionId))
            {
                if (!_pending.Contains(networkObject)) _pending.Add(networkObject);
                return;
            }
            Activate(networkObject);
        }

        internal void Unregister(LocalNetworkObject networkObject)
        {
            if (networkObject == null) return;
            _pending.Remove(networkObject);
            if (networkObject.ObjectId != null &&
                _bound.TryGetValue(networkObject.ObjectId, out var current) &&
                ReferenceEquals(current, networkObject))
            {
                _bound.Remove(networkObject.ObjectId);
            }
        }

        /// <summary>The component is going away for good (destroyed, or local multiplayer stopping).</summary>
        internal void Forget(LocalNetworkObject networkObject)
        {
            Unregister(networkObject);
            _known.Remove(networkObject);
        }

        /// <summary>A declare didn't complete; try again with the next join instead of leaving the object unbound.</summary>
        internal void RequeueAfterFailedDeclare(LocalNetworkObject networkObject)
        {
            if (networkObject == null || _disposed || !networkObject.isActiveAndEnabled) return;
            if (!_pending.Contains(networkObject)) _pending.Add(networkObject);
        }

        /// <summary>Bind a component to a live objectId.</summary>
        internal void BindObjectId(LocalNetworkObject networkObject, string objectId)
        {
            if (networkObject == null || string.IsNullOrEmpty(objectId)) return;
            if (_bound.TryGetValue(objectId, out var existing) && existing != null && !ReferenceEquals(existing, networkObject))
            {
                Debug.LogWarning($"[LocalMP][SyncedObjects] {objectId} already bound to '{existing.name}'; rebinding to '{networkObject.name}' " +
                                 "(two objects share one BSObjectId.Id)");
            }
            _bound[objectId] = networkObject;
        }

        void FlushPendingRegistrations()
        {
            if (_pending.Count == 0) return;
            var toActivate = _pending.ToArray();
            _pending.Clear();
            foreach (var networkObject in toActivate)
            {
                if (networkObject != null) Register(networkObject);
            }
        }

        void Activate(LocalNetworkObject networkObject)
        {
            networkObject.OnBinderReady(this);
        }

        // ─── Session lifecycle (production: HandleConnectionStateChanged) ──────────

        void HandleRoomJoined()
        {
            if (_disposed) return;
            string own = _session.OwnRoomSessionId;
            if (!string.Equals(own, _lastOwnRoomSessionId, StringComparison.Ordinal))
            {
                // A new room session (production: a full join after teardown) starts every payload stream over. A
                // resumed session keeps them, so the peers' sequence gates still accept this player's payloads.
                _objectOutSeq.Clear();
                _objectInSeq.Clear();
                _lastOwnRoomSessionId = own;
                // Production only starts a new session with a freshly loaded space, whose objects all declare. Here
                // Leave → Join and a refused resume start one in the same scene, after the relay may have deleted
                // this player's records (nobody left to take them over after the authority grace): count it as a
                // space load, so every object declares again. Scene creates are idempotent, so an object whose record
                // survived just binds to it. A resumed session keeps its records and only re-binds, as in production.
                _sceneLoadSerial++;
            }
            _mirrorSerial++;
            _joined = true;
            if (_session.State == SessionState.Joined)
            {
                ActivateAfterJoin();
            }
            else
            {
                // RoomJoined before the session reports Joined: activate as soon as it does, so the declares aren't
                // refused with not_connected.
                _activatePending = true;
            }
        }

        void HandleStateChanged(SessionState state)
        {
            if (_disposed) return;
            if (state == SessionState.Joined)
            {
                if (_activatePending && _joined) ActivateAfterJoin();
                return;
            }
            if (state == SessionState.Reconnecting)
            {
                // The socket dropped and the session tries to resume (PacketPartyClient.ReconnectLoopAsync). Its
                // partial teardown keeps the record mirror, the bindings and the payload streams, so owned objects
                // stay owned; only the transform streams start over (Transforms.ResetForReconnect). RoomJoined
                // follows on resume, RoomLeft if the relay no longer has the session.
                if (_joined) HandleConnectionLost();
                return;
            }
            // Idle, Failed: the session is over (RoomLeft normally said so already).
            if (_joined || _bound.Count > 0 || _objects.Count > 0) HandleRoomLeft();
        }

        void HandleConnectionLost()
        {
            _joined = false;
            _activatePending = false;
            try { Disconnected?.Invoke(); }
            catch (Exception ex) { Debug.LogException(ex); }
        }

        void ActivateAfterJoin()
        {
            _activatePending = false;
            // Re-run declares/bindings under the new session identity.
            foreach (var networkObject in _bound.Values)
            {
                if (networkObject != null && !_pending.Contains(networkObject)) _pending.Add(networkObject);
            }
            _bound.Clear();
            FlushPendingRegistrations();
        }

        void HandleRoomLeft()
        {
            if (_disposed) return;
            if (!_joined && _bound.Count == 0 && _objects.Count == 0) return;
            _joined = false;
            _activatePending = false;
            _mirrorSerial++;
            // Reconnects rehydrate from the post-welcome snapshots; stale mirrors must not survive a disconnect
            // (PacketPartyClient.TeardownAsync).
            _objects.Clear();
            _objectsRevision = 0;
            try { Disconnected?.Invoke(); }
            catch (Exception ex) { Debug.LogException(ex); }
            _scratch.Clear();
            _scratch.AddRange(_bound.Values);
            _bound.Clear();
            foreach (var networkObject in _scratch)
            {
                if (networkObject == null) continue;
                networkObject.OnClientDisconnected();
                if (!_pending.Contains(networkObject)) _pending.Add(networkObject);
            }
            _scratch.Clear();
            RaiseMirrorReset();
        }

        // ─── Broadcasts (PacketPartyClient.OnObject*Signal + PacketPartyObjectBinder.HandleObject*) ──

        void OnObjectSnapshotSignal(JObject body)
        {
            var snapshot = Parse<ObjectSnapshotEvent>(body, ObjectMessages.Snapshot);
            if (snapshot == null) return;
            _objects.Clear();
            foreach (var record in snapshot.Objects ?? new List<SyncedObjectRecord>())
            {
                if (record?.ObjectId != null) _objects[record.ObjectId] = record;
            }
            _objectsRevision = snapshot.Revision;
            RaiseMirrorReset();
            if (snapshot.Objects == null) return;
            foreach (var record in snapshot.Objects)
            {
                if (record?.ObjectId == null) continue;
                RaiseRecordDeclared(record);
                DispatchRecord(record);
            }
        }

        void OnObjectCreatedSignal(JObject body)
        {
            var created = Parse<ObjectCreatedEvent>(body, ObjectMessages.Created);
            if (created?.Object?.ObjectId == null) return;
            _objects[created.Object.ObjectId] = created.Object;
            _objectsRevision = created.Revision;
            RaiseRecordDeclared(created.Object);
            DispatchRecord(created.Object);
        }

        void OnObjectDeletedSignal(JObject body)
        {
            var deleted = Parse<ObjectDeletedEvent>(body, ObjectMessages.Deleted);
            if (deleted?.ObjectId == null) return;
            _objects.Remove(deleted.ObjectId);
            _objectsRevision = deleted.Revision;
            if (_bound.TryGetValue(deleted.ObjectId, out var networkObject))
            {
                _bound.Remove(deleted.ObjectId);
                if (networkObject != null)
                {
                    networkObject.OnObjectDeleted(deleted.Reason);
                    // Keep it registered, so the next join binds (or, after a space load, declares) it again.
                    if (!_pending.Contains(networkObject)) _pending.Add(networkObject);
                }
            }
            try { RecordRemoved?.Invoke(deleted.ObjectId); }
            catch (Exception ex) { Debug.LogException(ex); }
        }

        void OnObjectOwnerChangedSignal(JObject body)
        {
            var change = Parse<ObjectOwnerChangedEvent>(body, ObjectMessages.OwnerChanged);
            if (change?.Object?.ObjectId == null) return;
            _objects[change.Object.ObjectId] = change.Object;
            _objectsRevision = change.Revision;
            // A new owner starts its own payload seq stream; reset gates.
            ResetObjectTransportState(change.Object.ObjectId);
            DispatchRecord(change.Object);
        }

        void OnObjectUpdatedSignal(JObject body)
        {
            var updated = Parse<ObjectUpdatedEvent>(body, ObjectMessages.Updated);
            if (updated?.Object?.ObjectId == null) return;
            _objects[updated.Object.ObjectId] = updated.Object;
            _objectsRevision = Math.Max(_objectsRevision, updated.Revision);
            DispatchRecord(updated.Object);
        }

        void DispatchRecord(SyncedObjectRecord record)
        {
            if (_bound.TryGetValue(record.ObjectId, out var networkObject) && networkObject != null)
            {
                networkObject.OnRecordChanged(record);
            }
            // No SpawnHandler: a record nobody here binds is ignored, as in Greenfield (ObjectIdDiagnostics reports it).
        }

        void RaiseRecordDeclared(SyncedObjectRecord record)
        {
            try { RecordDeclared?.Invoke(record); }
            catch (Exception ex) { Debug.LogException(ex); }
        }

        void RaiseMirrorReset()
        {
            try { MirrorReset?.Invoke(); }
            catch (Exception ex) { Debug.LogException(ex); }
        }

        static T Parse<T>(JObject body, string type) where T : class
        {
            if (body == null) return null;
            try
            {
                return body.ToObject<T>(ObjectJson.Serializer);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LocalMP][SyncedObjects] ignored a malformed {type}: {ex.Message}");
                return null;
            }
        }

        // ─── Payload lane (PacketPartyClient.cs:2031-2168) ───────────────────────

        /// <summary>Send one opaque component payload immediately (reliable). Owner only.</summary>
        public void SendObjectPayload(string objectId, string componentKey, byte[] body, bool reliable = true)
        {
            var record = AssertOwnedForSend(objectId);
            var message = new JObject
            {
                ["objectId"] = objectId,
                ["gen"] = record.OwnershipGeneration,
                ["seq"] = NextObjectSeq(objectId),
                ["key"] = componentKey ?? "",
                ["body"] = Convert.ToBase64String(body ?? Array.Empty<byte>()),
                ["reliable"] = reliable
            };
            if (!_session.Send(ObjectLaneMessages.Payload, message))
            {
                throw new LocalRelayException("not_connected", "the object payload lane is not open");
            }
        }

        void HandleObjectLaneMessage(JObject message)
        {
            AcceptObjectLaneMessage(message);
        }

        /// <summary>
        /// Authenticate object sender/generation before sequence gating and dispatch. True when the payload passed the
        /// gate (it then reaches the component bound to the object, if any).
        /// </summary>
        internal bool AcceptObjectLaneMessage(JObject message)
        {
            if (message == null) return false;
            string objectId;
            uint generation;
            int seq;
            string sender;
            string componentKey;
            byte[] body;
            try
            {
                objectId = (string)message["objectId"];
                if (string.IsNullOrEmpty(objectId)) return false;
                long rawGeneration = (long)message["gen"];
                long rawSeq = (long)message["seq"];
                if (rawGeneration < 0 || rawGeneration > uint.MaxValue || rawSeq < 0 || rawSeq > 0xFFFF) return false;
                generation = (uint)rawGeneration;
                seq = (int)rawSeq;
                sender = (string)message["from"];
                componentKey = (string)message["key"] ?? "";
                body = Convert.FromBase64String((string)message["body"] ?? "");
            }
            catch (Exception)
            {
                // Malformed input is expected on shared lanes.
                return false;
            }
            if (!_objects.TryGetValue(objectId, out var record)) return false;
            int? last = _objectInSeq.TryGetValue(objectId, out var gate) && gate.Matches(record) ? gate.Seq : (int?)null;
            if (!ObjectTransportRules.ShouldAcceptEnvelope(
                record.OwnerRoomSessionId,
                record.OwnershipGeneration,
                generation,
                seq,
                sender,
                last)) return false;
            _objectInSeq[objectId] = new InboundGate { Seq = seq, Owner = record.OwnerRoomSessionId, Generation = record.OwnershipGeneration };
            if (_bound.TryGetValue(objectId, out var networkObject) && networkObject != null)
            {
                networkObject.OnPayloadReceived(componentKey, ObjectTransportRules.KindOpaqueBinary, body);
            }
            return true;
        }

        int NextObjectSeq(string objectId)
        {
            int next = (_objectOutSeq.TryGetValue(objectId, out int current) ? current + 1 : 1) & 0xFFFF;
            _objectOutSeq[objectId] = next;
            return next;
        }

        SyncedObjectRecord AssertOwnedForSend(string objectId)
        {
            if (string.IsNullOrEmpty(objectId)) throw new ArgumentException("objectId required", nameof(objectId));
            if (!_objects.TryGetValue(objectId, out var record))
            {
                throw new LocalRelayException("unknown_object", $"object {objectId} is not in the local mirror");
            }
            if (!IsOwnedBySelf(record))
            {
                throw new LocalRelayException("not_owner", $"object {objectId} is not owned by this session");
            }
            return record;
        }

        /// <summary>Clear per-object transport state when ownership changes or the object dies.</summary>
        void ResetObjectTransportState(string objectId)
        {
            _objectOutSeq.Remove(objectId);
            _objectInSeq.Remove(objectId);
        }

        // ─── Requests (PacketPartyClient.cs:2170-2333) ──────────────────────────

        /// <summary>
        /// Create a synced object. Scene objects use <paramref name="sceneObjectId"/> as a stable id and creates are
        /// idempotent (declare the scene on every join): the first declarer owns it at generation 0.
        /// </summary>
        public async Task<SyncedObjectRecord> CreateObjectAsync(
            string typeKey,
            SyncedObjectOrigin origin = SyncedObjectOrigin.Spawned,
            string sceneObjectId = null,
            SyncedObjectDisconnectPolicy? ownerDisconnectPolicy = null,
            bool locked = false,
            CancellationToken cancellation = default)
        {
            var payload = new JObject
            {
                ["typeKey"] = typeKey,
                ["origin"] = origin == SyncedObjectOrigin.Scene ? "scene" : "spawned"
            };
            if (origin == SyncedObjectOrigin.Scene) payload["objectId"] = sceneObjectId;
            var disconnectPolicy = ownerDisconnectPolicy ??
                (origin == SyncedObjectOrigin.Scene ? SyncedObjectDisconnectPolicy.Transfer : SyncedObjectDisconnectPolicy.Destroy);
            payload["ownerDisconnectPolicy"] = disconnectPolicy == SyncedObjectDisconnectPolicy.Transfer ? "transfer" : "destroy";
            if (locked) payload["locked"] = true;
            var result = await RequestObjectAsync(ObjectMessages.Create, payload, cancellation);
            return result.Object;
        }

        public async Task DeleteObjectAsync(string objectId, CancellationToken cancellation = default)
        {
            await RequestObjectAsync(ObjectMessages.Delete, new JObject { ["objectId"] = objectId }, cancellation);
        }

        /// <summary>Atomically become authority and optionally lock the object as held.</summary>
        public async Task<SyncedObjectRecord> AcquireObjectAsync(string objectId, bool locked = true, CancellationToken cancellation = default)
        {
            var result = await RequestObjectAsync(ObjectMessages.Acquire,
                new JObject { ["objectId"] = objectId, ["lock"] = locked }, cancellation);
            return result.Object;
        }

        public async Task<SyncedObjectRecord> CommitObjectStateAsync(string objectId, JObject state,
            uint expectedOwnershipGeneration, CancellationToken cancellation = default)
        {
            var result = await RequestObjectAsync(ObjectMessages.CommitState,
                new JObject
                {
                    ["objectId"] = objectId,
                    ["state"] = state,
                    ["expectedOwnershipGeneration"] = expectedOwnershipGeneration
                }, cancellation);
            return result.Object;
        }

        public async Task<SyncedObjectRecord> TransferObjectAsync(string objectId, string toRoomSessionId, CancellationToken cancellation = default)
        {
            var result = await RequestObjectAsync(ObjectMessages.Transfer,
                new JObject { ["objectId"] = objectId, ["toRoomSessionId"] = toRoomSessionId }, cancellation);
            return result.Object;
        }

        /// <summary>Owner only: block take-steal while held.</summary>
        public async Task<SyncedObjectRecord> LockObjectAsync(string objectId, CancellationToken cancellation = default)
        {
            var result = await RequestObjectAsync(ObjectMessages.Lock, new JObject { ["objectId"] = objectId }, cancellation);
            return result.Object;
        }

        public async Task<SyncedObjectRecord> UnlockObjectAsync(string objectId, CancellationToken cancellation = default)
        {
            var result = await RequestObjectAsync(ObjectMessages.Unlock, new JObject { ["objectId"] = objectId }, cancellation);
            return result.Object;
        }

        async Task<ObjectResult> RequestObjectAsync(string type, JObject payload, CancellationToken cancellation)
        {
            if (_disposed) throw new OperationCanceledException("local multiplayer stopped");
            // Stamp the request with the session it belongs to: the binder's own mirror generation, which moves exactly
            // when the mirror is rebuilt (the host's SessionEpoch moves with the socket, which is a step earlier).
            int mirrorSerial = _mirrorSerial;
            // The reply resumes on Unity's main thread (no ConfigureAwait(false)): broadcasts update the same mirror.
            JObject body = await _session.RequestAsync(type, payload, cancellation.CanBeCanceled ? cancellation : _lifetime.Token);
            if (_disposed) throw new OperationCanceledException("local multiplayer stopped");
            if (mirrorSerial != _mirrorSerial)
            {
                // The reply belongs to a session that has ended; its record would corrupt the new session's mirror.
                throw new LocalRelayException("closed", $"object {type}: the room session changed before the reply arrived");
            }
            ObjectResult result = null;
            try { result = body?.ToObject<ObjectResult>(ObjectJson.Serializer); }
            catch (Exception) { result = null; }
            result ??= new ObjectResult { Ok = false, Error = "invalid_result" };
            // Object-operation responses carry the authoritative post-operation record. Apply it immediately so
            // callers do not race the separate room broadcast when checking ownership/lock state after await.
            if (result.Object?.ObjectId != null)
            {
                ApplyResultRecord(result.Object, result.Revision);
            }
            if (!result.Ok)
            {
                throw new LocalRelayException(result.Error ?? "object_error", $"object {type} failed: {result.Error}");
            }
            return result;
        }

        void ApplyResultRecord(SyncedObjectRecord record, long revision)
        {
            // The relay sends a reply before the broadcast it causes, but the reply's continuation is posted, so a
            // later broadcast for the same object may already be in the mirror: never let the reply roll it back.
            if (_objects.TryGetValue(record.ObjectId, out var current) && current != null && current.Revision > record.Revision)
            {
                return;
            }
            _objects[record.ObjectId] = record;
            _objectsRevision = Math.Max(_objectsRevision, revision);
        }
    }
}
