// <mirror source="Assets/Systems/Networking/HeldSyncSuspender.cs" sha256="330d60746b3c50844b2de8f19ff4076811e15d2c2b9416302a054b96b417882a" mode="port" />
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
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class HeldSyncSuspender : MonoBehaviour
    {
        [NonSerialized] bool _live;
        private LocalHeldObject _held;
        private LocalSyncedTransform _sync;

        internal void Configure()
        {
            _live = true;
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
            if (!_live) return;
            if (_sync != null) _sync.enabled = !attached;
        }

        internal void Teardown()
        {
            if (_held != null) _held.OnAttachmentChanged -= OnAttachmentChanged;
            _live = false;
        }
    }
}
