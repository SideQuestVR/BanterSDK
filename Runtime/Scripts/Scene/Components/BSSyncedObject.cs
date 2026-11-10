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
        public bool takeOwnershipOnProximity;
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

        [Tooltip("Determines if ownership is taken when a player touches the object. Without Take Ownership On " +
                 "Proximity the object is still for a moment when first bumped (it moves once the claim lands).")]
        [See(initial = "true")][SerializeField] internal bool takeOwnershipOnCollision = true;


        [Tooltip("Determines if ownership is taken when the object is grabbed.")]
        [See(initial = "true")][SerializeField] internal bool takeOwnershipOnGrab = true;

        [Tooltip("Keeps a physics object still (kinematic) for everyone until a player grabs it, comes close or bumps " +
                 "it (with Take Ownership On Proximity / On Collision), sits on it, or a script calls TakeOwnership; a " +
                 "reset puts it back at rest. 'Owned' means the player whose game simulates it, not the one holding it. " +
                 "Untick both for 'only moves when grabbed'. A Rigidbody set to kinematic stays kinematic when claimed.")]
        [See(initial = "false")][SerializeField] internal bool kinematicIfNotOwned = false;

        [Tooltip("Seconds a moved object waits, once nobody is holding it or attached to it, before it returns to " +
                 "where it was placed. 0 = never. -1 = the app's default.")]
        [See(initial = "-1")][SerializeField] internal float resetAfterSeconds = -1f;

        // Declared last on purpose: the JS constructor takes these in declaration order, and existing worlds pass the
        // earlier ones positionally, so a new field can only go on the end (the bridge lock enforces it).
        [Tooltip("Determines if ownership is taken as a player comes close (just before touching it), so the object " +
                 "is already moving under their physics by the time they touch it. Untick so walking past or near it " +
                 "never takes it, e.g. a ball people stand around; with Take Ownership On Collision it's then taken " +
                 "on touch instead.")]
        [See(initial = "true")][SerializeField] internal bool takeOwnershipOnProximity = true;

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
            synced.takeOwnershipOnProximity = takeOwnershipOnProximity;
            synced.takeOwnershipOnGrab = takeOwnershipOnGrab;
            synced.kinematicIfNotOwned = kinematicIfNotOwned;
            synced.resetAfterSeconds = resetAfterSeconds;
            scene.events.OnSyncedObject.Invoke(synced, this);
        }
    }
}