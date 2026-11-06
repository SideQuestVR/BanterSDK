using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.VisualScripting;

namespace BS
{
    [RenamedFrom("Banter.SDK.BanterSynced")]
    public class BSSynced
    {
        public bool syncPosition;
        public bool syncRotation;
        public bool takeOwnershipOnCollision;
        public bool takeOwnershipOnGrab;
        public bool kinematicIfNotOwned;
        public float resetAfterSeconds;
    }
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BSObjectId))]
    [WatchComponent]
    public partial class BSSyncedObject : BSComponentBase
    {
        [Tooltip("Determines if the object's position is synchronized across all clients.")]
        [See(initial = "true")][SerializeField] internal bool syncPosition = true;

        [Tooltip("Determines if the object's rotation is synchronized across all clients.")]
        [See(initial = "true")][SerializeField] internal bool syncRotation = true;

        [Tooltip("Determines if ownership is taken when the object collides with another object.")]
        [See(initial = "true")][SerializeField] internal bool takeOwnershipOnCollision = true;

        [Tooltip("Determines if ownership is taken when the object is grabbed.")]
        [See(initial = "true")][SerializeField] internal bool takeOwnershipOnGrab = true;

        [Tooltip("Keeps a physics object still (kinematic) for everyone until a player grabs it, bumps it (with Take " +
                 "Ownership On Collision), sits on it, or a script calls TakeOwnership; a reset puts it back at rest. " +
                 "'Owned' means the player whose game simulates it, not the one holding it. Untick Take Ownership On " +
                 "Collision for 'only moves when grabbed'. A Rigidbody set to kinematic stays kinematic when claimed.")]
        [See(initial = "false")][SerializeField] internal bool kinematicIfNotOwned = false;

        [Tooltip("Seconds a moved object waits, once nobody is holding it or attached to it, before it returns to " +
                 "where it was placed. 0 = never. -1 = the app's default.")]
        [See(initial = "-1")][SerializeField] internal float resetAfterSeconds = -1f;

        [Method]
        public void _TakeOwnership()
        {
            scene.events.OnTakeOwnership.Invoke(synced, this);
        }
        [Method]
        public bool _DoIOwn()
        {
            return scene.data.NSODoIOwn(synced, banterObjectId);
        }
        BSSynced synced;
        BSObjectId banterObjectId;
        internal override void StartStuff()
        {
            banterObjectId = GetComponent<BSObjectId>();
            UpdateCallback(null);
            SetLoadedIfNot();
        }

        internal override void UpdateStuff()
        {

        }

        internal override void DestroyStuff() { }
        internal void UpdateCallback(List<PropertyName> changedProperties)
        {
            if (synced == null)
            {
                synced = new BSSynced();
            }
            synced.syncPosition = syncPosition;
            synced.syncRotation = syncRotation;
            synced.takeOwnershipOnCollision = takeOwnershipOnCollision;
            synced.takeOwnershipOnGrab = takeOwnershipOnGrab;
            synced.kinematicIfNotOwned = kinematicIfNotOwned;
            synced.resetAfterSeconds = resetAfterSeconds;
            scene.events.OnSyncedObject.Invoke(synced, this);
        }
    }
}