// <mirror source="Packages/com.sidequest.packetparty/Runtime/Components/PacketPartyHeldObject.cs" sha256="a0a25d0b3017d9664e5a1df826eec40b250c2a8dee2a20a4f629481d1ad422e7" mode="port" />
// PacketPartyHeldObject line for line. PacketPartyClient.SendObjectPayload / OnPeerJoined become the binder's payload
// lane and ILocalSession.PeerJoined; the remote hand anchor comes from the orb avatars (IRemoteBoneSource.ResolveHeldAnchor,
// Greenfield's RemoteAvatarService.ResolveAnchor). The codec lives in HeldObjectCodec.
using System;
using System.Threading.Tasks;
using UnityEngine;

namespace BS.LocalMultiplayer.Objects
{
    /// <summary>Replicates a held object as a stable pose relative to a named hand anchor.</summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class LocalHeldObject : MonoBehaviour
    {
        public const string ComponentKey = HeldObjectCodec.ComponentKey;

        public LocalNetworkObject NetworkObject { get; private set; }
        /// <summary>The transform whose hand-relative pose is synchronized: this GameObject (production's empty
        /// authored attachmentTarget; the module adds this component at runtime, so nothing ever sets one).</summary>
        public Transform AttachmentTarget => transform;
        public bool IsAttached { get; private set; }
        public string AnchorKey { get; private set; }
        public Vector3 LocalPosition { get; private set; }
        public Quaternion LocalRotation { get; private set; } = Quaternion.identity;
        /// <summary>True after the current remote attachment has resolved and applied at least one final anchor-relative pose.</summary>
        public bool HasAppliedRemotePose { get; private set; }
        public event Action<bool> OnAttachmentChanged;
        /// <summary>Raised after a remote attachment pose is applied during LateUpdate.</summary>
        public event Action OnRemotePoseApplied;
        /// <summary>Raised once when the current remote attachment first resolves to a final pose.</summary>
        public event Action OnRemotePoseReady;

        [NonSerialized] bool _live;
        private Transform _localAnchor;
        private Transform _remoteAnchor;
        private LocalSyncedTransform _syncTransform;
        private ILocalSession _subscribedSession;

        internal void Configure()
        {
            _live = true;
        }

        private void Awake()
        {
            NetworkObject = GetComponent<LocalNetworkObject>();
            _syncTransform = GetComponent<LocalSyncedTransform>();
        }

        private void OnEnable()
        {
            if (!_live || NetworkObject == null) return;
            NetworkObject.OnPayloadStream += HandlePayload;
            NetworkObject.OnOwnershipChanged += HandleOwnershipChanged;
            var session = NetworkObject.Binder != null ? NetworkObject.Binder.Session : null;
            if (session != null)
            {
                session.PeerJoined += HandlePeerJoined;
                _subscribedSession = session;
            }
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void Unsubscribe()
        {
            if (NetworkObject != null)
            {
                NetworkObject.OnPayloadStream -= HandlePayload;
                NetworkObject.OnOwnershipChanged -= HandleOwnershipChanged;
            }
            if (_subscribedSession != null)
            {
                _subscribedSession.PeerJoined -= HandlePeerJoined;
                _subscribedSession = null;
            }
        }

        public void Attach(Transform anchor, string anchorKey, bool reparentLocal = false)
        {
            if (anchor == null) throw new ArgumentNullException(nameof(anchor));
            if (string.IsNullOrWhiteSpace(anchorKey)) throw new ArgumentException("anchorKey required", nameof(anchorKey));
            if (!NetworkObject.IsOwned) throw new InvalidOperationException("attachment requires object ownership");
            _localAnchor = anchor;
            AnchorKey = anchorKey;
            var target = AttachmentTarget;
            LocalPosition = anchor.InverseTransformPoint(target.position);
            LocalRotation = Quaternion.Inverse(anchor.rotation) * target.rotation;
            IsAttached = true;
            HasAppliedRemotePose = false;
            _remoteAnchor = null;
            if (reparentLocal) target.SetParent(anchor, true);
            Publish();
            SafeInvoke(true);
        }

        public void Detach(bool preserveWorldPose = true)
        {
            if (!IsAttached) return;
            var target = AttachmentTarget;
            if (_localAnchor != null && target.parent == _localAnchor) target.SetParent(null, preserveWorldPose);
            IsAttached = false;
            HasAppliedRemotePose = false;
            _localAnchor = null;
            _remoteAnchor = null;
            if (NetworkObject.IsOwned) Publish();
            SafeInvoke(false);
        }

        private void LateUpdate()
        {
            if (!_live || NetworkObject == null) return;
            if (!IsAttached || NetworkObject.IsOwned) return;
            // Common transform parenting composes this pose in the hierarchy runtime — but only while the
            // sync-transform is actively driving the object. A caller that suspends it (disables the
            // component) while held is opting into HeldObject's anchor-relative pose instead, so defer
            // only to an ENABLED sync-transform.
            if (_syncTransform != null && _syncTransform.enabled) return;
            var record = NetworkObject.Record;
            if (record == null || string.IsNullOrEmpty(record.OwnerRoomSessionId)) return;
            if (_remoteAnchor == null)
            {
                var binder = NetworkObject.Binder;
                _remoteAnchor = binder != null ? binder.ResolveAttachmentAnchor(record.OwnerRoomSessionId, AnchorKey) : null;
            }
            if (_remoteAnchor == null) return;
            AttachmentTarget.SetPositionAndRotation(
                _remoteAnchor.TransformPoint(LocalPosition),
                _remoteAnchor.rotation * LocalRotation);
            if (!HasAppliedRemotePose)
            {
                HasAppliedRemotePose = true;
                try { OnRemotePoseReady?.Invoke(); }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }
            try { OnRemotePoseApplied?.Invoke(); }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        private void HandlePayload(string componentKey, byte kind, byte[] body)
        {
            if (componentKey != ComponentKey || kind != ObjectTransportRules.KindOpaqueBinary || NetworkObject.IsOwned) return;
            if (!HeldObjectCodec.TryDecode(body, out bool attached, out string anchorKey, out Vector3 position, out Quaternion rotation)) return;
            IsAttached = attached;
            AnchorKey = anchorKey;
            LocalPosition = position;
            LocalRotation = rotation;
            _remoteAnchor = null;
            HasAppliedRemotePose = false;
            SafeInvoke(attached);
        }

        private void HandleOwnershipChanged(bool owned)
        {
            _remoteAnchor = null;
            HasAppliedRemotePose = false;
            if (!owned) _localAnchor = null;
            else if (IsAttached)
            {
                // Automatic/explicit authority transfer arrives unlocked. The
                // new authority preserves the last rendered world pose and
                // clears the stale attachment before physics resumes.
                if (!NetworkObject.IsLocked) Detach(preserveWorldPose: true);
                else Publish();
            }
        }

        private void HandlePeerJoined(LocalPeer peer)
        {
            if (peer == null || !_live) return;
            if (IsAttached && NetworkObject.IsOwned) Publish();
        }

        private void Publish()
        {
            try
            {
                byte[] payload = HeldObjectCodec.Encode(IsAttached, AnchorKey, LocalPosition, LocalRotation);
                var binder = NetworkObject.Binder;
                if (binder == null) throw new LocalRelayException("not_connected", "no binder attached to the network object");
                binder.SendObjectPayload(NetworkObject.ObjectId, ComponentKey, payload, reliable: true);
                _ = CommitDurableStateAsync(payload);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LocalMP][SyncedObjects] LocalHeldObject '{name}': attachment publish failed ({ex.Message})");
            }
        }

        private async Task CommitDurableStateAsync(byte[] payload)
        {
            // Read the token while alive: the property throws on a destroyed component.
            var destroyed = destroyCancellationToken;
            try
            {
                await NetworkObject.CommitComponentStateAsync(ComponentKey, payload, destroyed);
            }
            catch (OperationCanceledException) when (destroyed.IsCancellationRequested) { }
            catch (Exception ex)
            {
                if (!_live || this == null) return;
                Debug.LogWarning($"[LocalMP][SyncedObjects] LocalHeldObject '{name}': durable attachment commit failed ({ex.Message})");
            }
        }

        /// <summary>Local multiplayer is stopping: go inert (the scene keeps the object where it is).</summary>
        internal void Teardown()
        {
            Unsubscribe();
            _live = false;
            OnAttachmentChanged = null;
            OnRemotePoseApplied = null;
            OnRemotePoseReady = null;
        }

        private void SafeInvoke(bool attached)
        {
            try { OnAttachmentChanged?.Invoke(attached); }
            catch (Exception ex) { Debug.LogException(ex); }
        }
    }
}
