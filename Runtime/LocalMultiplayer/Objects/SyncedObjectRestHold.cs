// <mirror source="Assets/Systems/Networking/SyncedObjectRestHold.cs" sha256="6ded3113c13ced9913fb77059f72d9b886f6b7a47b242557b899a6ec603c3f0b" mode="port" />
// SyncedObjectRestHold with LocalNetworkObject / LocalSyncedTransform for the PacketParty components. The grab bridge
// calls Wake directly (production subscribes to GrabNetworkBridge.LocalGrabbed).
using System;
using UnityEngine;

namespace BS.LocalMultiplayer.Objects
{
    /// <summary>
    /// BSSyncedObject "Kinematic If Not Owned" on a dynamic Rigidbody. The body rests kinematic on every player, its
    /// owner too, until this player claims it: a grab, a bump (with Take Ownership On Collision), TakeOwnership() or a
    /// seat. It wakes on the grant, not the request, except for a bump, which has to be dynamic before contact. A reset
    /// puts it back to rest, and so does losing ownership. A transfer because the owner left does not wake it. Each player
    /// decides for itself and nothing goes on the wire. Added by <see cref="SyncedObjectsModule"/>, only to flagged objects.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class SyncedObjectRestHold : MonoBehaviour
    {
        [NonSerialized] bool _live;
        private LocalNetworkObject _netObj;
        private LocalSyncedTransform _sync;
        private SyncedObjectPhysicsHandoff _handoff;
        private Rigidbody _body;
        private bool _awake;

        internal void Configure()
        {
            _live = true;
            // Added to an active object (the flag turned on at runtime): its OnEnable ran before this.
            if (isActiveAndEnabled) OnEnable();
        }

        private void Awake()
        {
            _netObj = GetComponent<LocalNetworkObject>();
            _sync = GetComponent<LocalSyncedTransform>();
            _handoff = GetComponent<SyncedObjectPhysicsHandoff>();
            _body = GetComponent<Rigidbody>();
        }

        private void OnEnable()
        {
            if (!_live) return;
            _netObj.OnOwnershipChanged += OnOwnershipChanged;
            if (_handoff != null) _handoff.RestHold = this;
            if (!_awake) Rest();
        }

        private void OnDisable()
        {
            if (!_live) return;
            Unhook();
            // Still active: the world turned the flag off, so the body goes back to its authored dynamic self.
            if (gameObject.activeInHierarchy)
            {
                Wake();
                _awake = false;
                // Once bound, the synced transform sets the flag from ownership. Before that nothing does.
                if (_body != null && !_netObj.EverBound) _body.isKinematic = false;
            }
        }

        private void Unhook()
        {
            if (_netObj != null) _netObj.OnOwnershipChanged -= OnOwnershipChanged;
            if (_handoff != null) _handoff.RestHold = null;
        }

        /// <summary>This player has the object: simulate it for as long as it owns it.</summary>
        public void Wake()
        {
            _awake = true;
            if (_sync != null) _sync.HoldKinematicOnOwner = false;
        }

        /// <summary>Hold it still on every player, its owner included.</summary>
        public void Rest()
        {
            _awake = false;
            if (_sync != null) _sync.HoldKinematicOnOwner = true;
            // Before the bind too: the synced transform leaves an unbound body alone, and this one would fall meanwhile.
            if (_body != null) _body.isKinematic = true;
        }

        /// <summary>A claim didn't land: back to rest if this player doesn't have the object.</summary>
        public void RestUnlessOwned()
        {
            if (!_netObj.IsOwned) Rest();
        }

        private void OnOwnershipChanged(bool owned)
        {
            if (!owned) Rest();
        }

        /// <summary>Local multiplayer is stopping: go inert. The synced transform gives the body its authored setting back.</summary>
        internal void Teardown()
        {
            Unhook();
            if (_sync != null) _sync.HoldKinematicOnOwner = false;
            _live = false;
        }
    }
}
