// No Greenfield counterpart: part of the plan's deliberate deviation for SyncedObjectPhysicsHandoff, whose proximity
// trigger sits on a layer-2 (Ignore Raycast) child here instead of on the object itself. Its own file, so Unity has a
// MonoScript for it (a MonoBehaviour's class must match its file name).
using System;
using UnityEngine;

namespace BS.LocalMultiplayer.Objects
{
    /// <summary>
    /// Passes the layer-2 proximity child's trigger enters to its <see cref="SyncedObjectPhysicsHandoff"/>. An object
    /// without a Rigidbody gets them only this way; with one, Unity may report them on the body as well.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class HandoffTriggerForwarder : MonoBehaviour
    {
        [NonSerialized] SyncedObjectPhysicsHandoff _target;

        internal void Bind(SyncedObjectPhysicsHandoff target)
        {
            _target = target;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_target != null) _target.OnProximityEnter(other);
        }
    }
}
