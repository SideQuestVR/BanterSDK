// <mirror source="Assets/Systems/Networking/HeldSyncSuspender.cs" sha256="ff3b099345d29e89f855370405c8e544b52e03b87f4ff6b6f026fdc6e04217b3" mode="port" />
// HeldSyncSuspender line for line, with PacketPartyHeldObject / PacketPartySynchronizedTransform replaced by
// LocalHeldObject / LocalSyncedTransform.
using System;
using UnityEngine;

namespace BS.LocalMultiplayer.Objects
{
    /// <summary>
    /// While a synced object is held (its <see cref="LocalHeldObject"/> is attached), suspends its
    /// <see cref="LocalSyncedTransform"/> so HeldObject drives the object bone-relative instead
    /// of the sync-transform driving its world pose. HeldObject only defers to an <i>enabled</i>
    /// sync-transform, so disabling it hands positioning to HeldObject (which anchors to the grabber's
    /// hand via the orb avatar — lag-free, tracks the visible hand exactly).
    ///
    /// Driven off the networked attachment state, so it fires the same on the owner (which then stops
    /// publishing the world pose) and on non-owners (which stop applying it). Re-enables on detach so the
    /// free object's transform sync resumes. Added alongside HeldObject by SyncedObjectsModule.
    ///
    /// The re-enable must not let the sync-transform learn the wrong "authored" kinematic flag: its OnEnable
    /// re-reads it from the body every time, and on a player watching someone else hold the object the body
    /// is network-kinematic right then. Without the fix below, that player's later grab took ownership of a
    /// body that stayed kinematic: the object wouldn't move, stuck where the previous holder let it go.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class HeldSyncSuspender : MonoBehaviour
    {
        [NonSerialized] bool _live;
        private LocalHeldObject _held;
        private LocalSyncedTransform _sync;
        private Rigidbody _dynamicBody;

        /// <summary>
        /// <paramref name="dynamicBody"/>: the authored-dynamic Rigidbody the sync-transform binds (Rigidbody
        /// binding), whose kinematic flag it toggles for ownership; null when it binds the Transform.
        /// </summary>
        internal void Configure(Rigidbody dynamicBody)
        {
            _live = true;
            _dynamicBody = dynamicBody;
        }

        private void Awake()
        {
            _held = GetComponent<LocalHeldObject>();
            _sync = GetComponent<LocalSyncedTransform>();
        }

        private void OnEnable()
        {
            if (!_live || _held == null) return;
            _held.OnAttachmentChanged += OnAttachmentChanged;
            OnAttachmentChanged(_held.IsAttached); // reconcile in case attachment preceded us
        }

        private void OnDisable()
        {
            if (_held != null) _held.OnAttachmentChanged -= OnAttachmentChanged;
        }

        private void OnAttachmentChanged(bool attached)
        {
            if (!_live || _sync == null) return;
            if (attached || _sync.enabled || _dynamicBody == null)
            {
                _sync.enabled = !attached;
                return;
            }
            // LocalSyncedTransform.OnEnable records the body's current isKinematic as authored. Show it the
            // authored (dynamic) value, then put back what the body is now; for a non-owner the
            // sync-transform keeps it kinematic anyway.
            bool current = _dynamicBody.isKinematic;
            _dynamicBody.isKinematic = false;
            _sync.enabled = true;
            _dynamicBody.isKinematic = current;
        }

        internal void Teardown()
        {
            if (_held != null) _held.OnAttachmentChanged -= OnAttachmentChanged;
            _live = false;
        }
    }
}
