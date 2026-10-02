// <mirror source="Packages/com.sidequest.packetparty/Runtime/Components/PacketPartyNetworkObject.cs" sha256="c517a5d95049d4be81af13b146bef39a40ad331138eed3af254f67602cd27877" mode="port" />
// PacketPartyNetworkObject in its scene-declare mode, line for line where it applies: identity and ownership read
// from the binder's mirror (:56-100), the binder callbacks (:268-385), the ownership requests (:217-257) and the
// durable pose (:389-437). PacketPartyClient becomes LocalObjectBinder. Not ported: BindTo / spawned binding, unlocked
// cleanup, RPCs and the authored durable-pose source (synced objects always have exactly one root transform).
// Deliberate differences (plan parity ledger): a kept ObjectId is declared again on the first join after each space
// load, since locally the scene outlives the page, and on the first join of each new room session (Leave → Join, a
// refused resume), which production only starts with a reloaded space; a declare that dies with its session is retried
// on the next join.
using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace BS.LocalMultiplayer.Objects
{
    /// <summary>
    /// Binds a GameObject to one synced object in the room registry: on joining, it idempotently declares a scene
    /// object under its <see cref="BSObjectId"/> (the first declarer becomes the owner), then follows the record.
    /// Sibling components (<see cref="LocalSyncedTransform"/>, <see cref="LocalGrabbable"/>, <see cref="LocalHeldObject"/>)
    /// subscribe to this component's events instead of the session's. Added by <see cref="SyncedObjectsModule"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class LocalNetworkObject : MonoBehaviour
    {
        string typeKey = "object";
        bool declareSceneObject = true;
        string sceneObjectId = "";
        SyncedObjectDisconnectPolicy ownerDisconnectPolicy = SyncedObjectDisconnectPolicy.Transfer;
        bool lockOnDeclare;

        // Set when the module configures this component; false after a script reload or once torn down.
        [NonSerialized] bool _live;
        LocalObjectBinder _binder;
        bool _declareInFlight;
        bool _lastOwned;
        bool _everBound;
        // The space load or new room session (binder.SceneLoadSerial) the current ObjectId was declared under; -1 = never
        // declared.
        int _declaredForSceneLoad = -1;
        LocalSyncedTransform _syncTransform;

        /// <summary>Bound objectId; null until declared.</summary>
        public string ObjectId { get; private set; }

        /// <summary>The stable id this component declares: its BSObjectId.Id.</summary>
        public string SceneObjectId => string.IsNullOrEmpty(sceneObjectId) ? name : sceneObjectId;

        /// <summary>Live registry record from the mirror; null when unbound/deleted or not in a room.</summary>
        public SyncedObjectRecord Record
        {
            get
            {
                var binder = _binder;
                if (binder == null || ObjectId == null) return null;
                return binder.GetRecord(ObjectId);
            }
        }

        public LocalObjectBinder Binder => _binder;
        public bool IsBound => ObjectId != null;
        /// <summary>
        /// This object has been bound at least once. Production keeps a bound object's id across disconnects, so a
        /// previously bound object that isn't owned stays network-driven (kinematic); one never bound is left alone.
        /// </summary>
        public bool EverBound => _everBound;
        public bool IsOwned => _binder != null && _binder.IsOwnedBySelf(Record);
        public string OwnerRoomSessionId => Record?.OwnerRoomSessionId;
        public bool IsLocked => Record?.Locked == true;
        /// <summary>True when this object is currently owned by another room member.</summary>
        public bool IsRemoteOwned => IsBound && !IsOwned && !string.IsNullOrEmpty(OwnerRoomSessionId);
        /// <summary>True while a held-object component is driving this object from a hand anchor.</summary>
        public bool IsAttachmentActive => TryGetComponent(out LocalHeldObject held) && held.IsAttached;
        public LocalSyncedTransform SyncTransform => _syncTransform;
        internal bool IsLive => _live;
        /// <summary>A declare request is on its way to the relay.</summary>
        public bool IsDeclaring => _declareInFlight;

        /// <summary>Fired when ownership flips relative to this player (true = we own it now).</summary>
        public event Action<bool> OnOwnershipChanged;
        /// <summary>Fired on every registry record update for this object.</summary>
        public event Action<SyncedObjectRecord> OnRecordChangedEvent;
        /// <summary>Fired when the record is deleted (reason string from the registry).</summary>
        public event Action<string> OnDeleted;
        /// <summary>Opaque component payload from the remote owner (key, kind, body).</summary>
        public event Action<string, byte, byte[]> OnPayloadStream;
        public event Action TransformIdentityChanged;

        /// <summary>The synchronized transform may start: the object has a network identity.</summary>
        public bool IsTransformIdentityReady => _binder != null && (IsBound || _everBound);
        public bool HasTransformAuthority => IsOwned;

        void OnEnable()
        {
            if (!_live || _binder == null) return;
            _binder.Register(this);
        }

        void OnDisable()
        {
            if (!_live || _binder == null) return;
            _binder.Unregister(this);
        }

        void OnDestroy()
        {
            if (_binder != null) _binder.Forget(this);
        }

        /// <summary>
        /// Set the scene-declare parameters BEFORE the component activates (add while the GameObject is inactive,
        /// configure, activate).
        /// </summary>
        internal void ConfigureSceneDeclare(
            LocalObjectBinder binder,
            string declareTypeKey,
            string stableSceneObjectId,
            SyncedObjectDisconnectPolicy disconnectPolicy = SyncedObjectDisconnectPolicy.Transfer,
            bool locked = false)
        {
            if (binder == null) throw new ArgumentNullException(nameof(binder));
            if (string.IsNullOrEmpty(declareTypeKey)) throw new ArgumentException("typeKey required", nameof(declareTypeKey));
            if (string.IsNullOrEmpty(stableSceneObjectId)) throw new ArgumentException("sceneObjectId required", nameof(stableSceneObjectId));
            if (IsBound || _declareInFlight)
            {
                throw new InvalidOperationException($"'{name}' is already bound/declaring; configure before activation");
            }
            _binder = binder;
            typeKey = declareTypeKey;
            declareSceneObject = true;
            sceneObjectId = stableSceneObjectId;
            ownerDisconnectPolicy = disconnectPolicy;
            lockOnDeclare = locked;
            _live = true;
        }

        internal void AttachSyncTransform(LocalSyncedTransform syncTransform)
        {
            _syncTransform = syncTransform;
        }

        /// <summary>Local multiplayer is stopping: unbind and go inert.</summary>
        internal void Teardown()
        {
            if (_binder != null) _binder.Forget(this);
            _live = false;
            OnOwnershipChanged = null;
            OnRecordChangedEvent = null;
            OnDeleted = null;
            OnPayloadStream = null;
            TransformIdentityChanged = null;
        }

        public async Task<SyncedObjectRecord> AcquireAsync(bool locked = true, CancellationToken cancellation = default)
        {
            return await RequireBinder().AcquireObjectAsync(RequireObjectId(), locked, cancellation);
        }

        /// <summary>Commit the final world pose, then unlock while retaining simulation authority.</summary>
        public async Task<SyncedObjectRecord> DropAsync(CancellationToken cancellation = default)
        {
            var state = new JObject { ["transform"] = BuildDurableTransformState() };
            var objectId = RequireObjectId();
            await RequireBinder().CommitObjectStateAsync(objectId, state, RequireRecord().OwnershipGeneration, cancellation);
            return await RequireBinder().UnlockObjectAsync(objectId, cancellation);
        }

        public async Task<SyncedObjectRecord> CommitComponentStateAsync(string componentKey, byte[] payload,
            CancellationToken cancellation = default)
        {
            if (string.IsNullOrEmpty(componentKey)) throw new ArgumentException("componentKey required", nameof(componentKey));
            var position = transform.position;
            var rotation = transform.rotation;
            var state = new JObject
            {
                ["transform"] = new JObject
                {
                    ["position"] = new JArray(position.x, position.y, position.z),
                    ["rotation"] = new JArray(rotation.x, rotation.y, rotation.z, rotation.w)
                },
                ["components"] = new JObject { [componentKey] = Convert.ToBase64String(payload ?? Array.Empty<byte>()) }
            };
            return await RequireBinder().CommitObjectStateAsync(RequireObjectId(), state, RequireRecord().OwnershipGeneration, cancellation);
        }

        public async Task<SyncedObjectRecord> TransferAsync(string toRoomSessionId, CancellationToken cancellation = default)
        {
            return await RequireBinder().TransferObjectAsync(RequireObjectId(), toRoomSessionId, cancellation);
        }

        public async Task DeleteAsync(CancellationToken cancellation = default)
        {
            await RequireBinder().DeleteObjectAsync(RequireObjectId(), cancellation);
        }

        // ─── Binder callbacks ───────────────────────────────────────────────────

        internal void OnBinderReady(LocalObjectBinder binder)
        {
            if (!_live) return;
            _binder = binder;
            if (ObjectId != null && _declaredForSceneLoad == binder.SceneLoadSerial)
            {
                // Re-bind within the same room session; rebroadcast current state. A resumed session lands here.
                binder.BindObjectId(this, ObjectId);
                var record = Record;
                if (record != null) OnRecordChanged(record);
                return;
            }
            // First join, or the first join after a space load or of a new room session: production instantiates the
            // space again and its fresh components declare, so declare again (scene creates are idempotent).
            if (declareSceneObject && !_declareInFlight)
            {
                _declareInFlight = true;
                _ = DeclareAsync(binder);
            }
        }

        internal void OnClientDisconnected()
        {
            // Keep ObjectId (scene ids are stable; re-declare confirms on reconnect)
            // but drop ownership state so the next record dispatch re-fires events.
            if (_lastOwned)
            {
                _lastOwned = false;
                TransformIdentityChanged?.Invoke();
                SafeInvokeOwnership(false);
            }
        }

        internal void OnRecordChanged(SyncedObjectRecord record)
        {
            if (!_live) return;
            ApplyDurableState(record);
            try { OnRecordChangedEvent?.Invoke(record); }
            catch (Exception ex) { Debug.LogException(ex); }
            TransformIdentityChanged?.Invoke();
            bool owned = _binder != null && _binder.IsOwnedBySelf(record);
            if (owned != _lastOwned)
            {
                _lastOwned = owned;
                SafeInvokeOwnership(owned);
            }
        }

        void ApplyDurableState(SyncedObjectRecord record)
        {
            var state = record?.State;
            if (state == null) return;
            // Hydrate the durable attachment before considering the saved world
            // transform. A held object's world pose is stale by definition; moving
            // only its networked body before the hand-relative state is known can
            // tear compound physics objects apart for a frame.
            bool attachmentApplied = ApplyDurableComponent(state, HeldObjectCodec.ComponentKey);
            if (!(_binder != null && _binder.IsOwnedBySelf(record))
                && !IsAttachmentActive
                && !TryGetComponent(out LocalSyncedTransform _)
                && state.Transform?.Position?.Length == 3)
            {
                var p = state.Transform.Position;
                transform.position = new Vector3(p[0], p[1], p[2]);
                if (state.Transform.Rotation?.Length == 4)
                {
                    var r = state.Transform.Rotation;
                    transform.rotation = new Quaternion(r[0], r[1], r[2], r[3]);
                }
                if (TryGetComponent(out Rigidbody body) && state.Transform.Velocity?.Length == 3)
                {
                    var v = state.Transform.Velocity;
                    body.linearVelocity = new Vector3(v[0], v[1], v[2]);
                }
            }
            if (state.Components == null) return;
            foreach (var pair in state.Components)
            {
                if (attachmentApplied && pair.Key == HeldObjectCodec.ComponentKey) continue;
                ApplyDurableComponent(state, pair.Key);
            }
        }

        bool ApplyDurableComponent(SyncedObjectState state, string componentKey)
        {
            if (state.Components == null || !state.Components.TryGetValue(componentKey, out var encoded))
                return false;
            try
            {
                OnPayloadReceived(componentKey, ObjectTransportRules.KindOpaqueBinary, Convert.FromBase64String(encoded ?? ""));
            }
            catch (FormatException)
            {
                Debug.LogWarning($"[LocalMP][SyncedObjects] '{name}': invalid durable payload for {componentKey}");
            }
            return true;
        }

        internal void OnObjectDeleted(string reason)
        {
            if (_lastOwned)
            {
                _lastOwned = false;
                SafeInvokeOwnership(false);
            }
            try { OnDeleted?.Invoke(reason ?? "deleted"); }
            catch (Exception ex) { Debug.LogException(ex); }
            TransformIdentityChanged?.Invoke();
        }

        internal void OnPayloadReceived(string componentKey, byte kind, byte[] body)
        {
            try { OnPayloadStream?.Invoke(componentKey, kind, body); }
            catch (Exception ex) { Debug.LogException(ex); }
        }

        // ─── Internals ──────────────────────────────────────────────────────────

        internal JObject BuildDurableTransformState()
        {
            LocalSyncedTransform synchronized = _syncTransform;
            if (synchronized == null) TryGetComponent(out synchronized);
            Vector3 position;
            Quaternion rotation;
            Vector3? velocity = null;
            if (synchronized != null)
            {
                TransformBaseFields fields = TransformBaseFields.Position | TransformBaseFields.Rotation;
                if (synchronized.PoseBinding == TransformPoseBinding.Rigidbody)
                    fields |= TransformBaseFields.Velocity;
                if (!synchronized.TrySampleAuthoredWorldPose(fields, out SynchronizedTransformSample sample))
                    throw new InvalidOperationException("network_object_pose_sample_unavailable");
                position = sample.Position;
                rotation = sample.Rotation;
                if (sample.HasVelocity) velocity = sample.Velocity;
            }
            else
            {
                position = transform.position;
                rotation = transform.rotation;
            }

            // Preserve the pre-component behavior for Transform-driven objects:
            // a colocated Rigidbody still contributes the durable release velocity.
            Rigidbody body = synchronized != null ? synchronized.SynchronizedRigidbody : null;
            if (body == null && synchronized != null) synchronized.TryGetComponent(out body);
            if (body == null) TryGetComponent(out body);
            if (!velocity.HasValue && body != null)
            {
                velocity = body.linearVelocity;
            }

            var transformState = new JObject
            {
                ["position"] = new JArray(position.x, position.y, position.z),
                ["rotation"] = new JArray(rotation.x, rotation.y, rotation.z, rotation.w)
            };
            if (velocity.HasValue)
            {
                Vector3 value = velocity.Value;
                transformState["velocity"] = new JArray(value.x, value.y, value.z);
            }
            return transformState;
        }

        async Task DeclareAsync(LocalObjectBinder binder)
        {
            int sceneLoad = binder.SceneLoadSerial;
            bool declared = false;
            try
            {
                string id = string.IsNullOrEmpty(sceneObjectId) ? name : sceneObjectId;
                var record = await binder.CreateObjectAsync(
                    typeKey, SyncedObjectOrigin.Scene, id,
                    ownerDisconnectPolicy: ownerDisconnectPolicy,
                    locked: lockOnDeclare,
                    cancellation: destroyCancellationToken);
                if (!_live || this == null) return;
                ObjectId = record.ObjectId;
                _everBound = true;
                _declaredForSceneLoad = sceneLoad;
                declared = true;
                binder.BindObjectId(this, ObjectId);
                // The mirror holds the newest record (a broadcast may have overtaken the reply).
                OnRecordChanged(Record ?? record);
            }
            catch (OperationCanceledException)
            {
                // Component destroyed mid-declare, or local multiplayer stopped.
                declared = true;
            }
            catch (LocalRelayException ex)
            {
                Debug.LogWarning($"[LocalMP][SyncedObjects] '{name}': declare failed ({ex.Message})");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
            finally
            {
                _declareInFlight = false;
                if (!declared && _live && this != null) binder.RequeueAfterFailedDeclare(this);
            }
        }

        void SafeInvokeOwnership(bool owned)
        {
            try { OnOwnershipChanged?.Invoke(owned); }
            catch (Exception ex) { Debug.LogException(ex); }
            TransformIdentityChanged?.Invoke();
        }

        LocalObjectBinder RequireBinder()
        {
            var binder = _binder;
            if (binder == null || !_live) throw new LocalRelayException("not_connected", "no binder attached to the network object");
            return binder;
        }

        string RequireObjectId()
        {
            if (ObjectId == null) throw new LocalRelayException("not_bound", $"'{name}' has no bound objectId yet");
            return ObjectId;
        }

        SyncedObjectRecord RequireRecord()
        {
            var record = Record;
            if (record == null) throw new LocalRelayException("not_bound", $"'{name}' has no room record");
            return record;
        }
    }
}
