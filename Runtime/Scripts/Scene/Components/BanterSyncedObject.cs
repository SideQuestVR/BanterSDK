using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Banter.SDK
{
    public class BanterSynced
    {
        public bool syncPosition;
        public bool syncRotation;
        public bool takeOwnershipOnCollision;
        public bool takeOwnershipOnGrab;
        public bool kinematicIfNotOwned;
        public bool doIOwn;
    }
    [RequireComponent(typeof(BanterObjectId))]
    [WatchComponent]
    public class BanterSyncedObject : BanterComponentBase
    {
        [See(initial = "true")] public bool syncPosition;

        [See(initial = "true")] public bool syncRotation;

        [See(initial = "true")] public bool takeOwnershipOnCollision;

        [See(initial = "true")] public bool takeOwnershipOnGrab;

        [See(initial = "false")] public bool kinematicIfNotOwned;

        [See(initial = "false")] public bool doIOwn;

        [Method]
        public void _TakeOwnership()
        {
            scene.events.OnTakeOwnership.Invoke(synced, banterObjectId);
        }
        [Method]
        public void _DoIOwn()
        {
            scene.events.OnDoIOwn.Invoke(synced, banterObjectId);
        }
        BanterSynced synced;
        BanterObjectId banterObjectId;
        internal override void StartStuff()
        {
            banterObjectId = GetComponent<BanterObjectId>();
            UpdateCallback(null);
            SetLoadedIfNot();
        }

        internal override void DestroyStuff() { }
        public void UpdateCallback(List<PropertyName> changedProperties)
        {
            if (synced == null)
            {
                synced = new BanterSynced();
                synced.syncPosition = syncPosition;
                synced.syncRotation = syncRotation;
                synced.takeOwnershipOnCollision = takeOwnershipOnCollision;
                synced.takeOwnershipOnGrab = takeOwnershipOnGrab;
                synced.kinematicIfNotOwned = kinematicIfNotOwned;
                scene.events.OnSyncedObject.Invoke(synced, banterObjectId);
            }
        }
        // BANTER COMPILED CODE 
        BanterScene scene;
        bool alreadyStarted = false;
        void Start()
        {
            Init();
            StartStuff();
        }

        internal override void ReSetup()
        {
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.syncPosition, PropertyName.syncRotation, PropertyName.takeOwnershipOnCollision, PropertyName.takeOwnershipOnGrab, PropertyName.kinematicIfNotOwned, PropertyName.doIOwn, };
            UpdateCallback(changedProperties);
        }

        internal override void Init(List<object> constructorProperties = null)
        {
            scene = BanterScene.Instance();
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(gameObject.GetInstanceID(), GetInstanceID(), ComponentType.BanterSyncedObject);


            oid = gameObject.GetInstanceID();
            cid = GetInstanceID();

            if (constructorProperties != null)
            {
                Deserialise(constructorProperties);
            }

            SyncProperties(true);

        }

        void Awake()
        {
            BanterScene.Instance().RegisterComponentOnMainThread(gameObject, this);
        }

        void OnDestroy()
        {
            scene.UnregisterComponentOnMainThread(gameObject, this);

            DestroyStuff();
        }

        void TakeOwnership()
        {
            _TakeOwnership();
        }
        void DoIOwn()
        {
            _DoIOwn();
        }
        internal override object CallMethod(string methodName, List<object> parameters)
        {

            if (methodName == "TakeOwnership" && parameters.Count == 0)
            {
                TakeOwnership();
                return null;
            }
            else if (methodName == "DoIOwn" && parameters.Count == 0)
            {
                DoIOwn();
                return null;
            }
            else
            {
                return null;
            }
        }

        internal override void Deserialise(List<object> values)
        {
            List<PropertyName> changedProperties = new List<PropertyName>();
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] is BanterBool)
                {
                    var valsyncPosition = (BanterBool)values[i];
                    if (valsyncPosition.n == PropertyName.syncPosition)
                    {
                        syncPosition = valsyncPosition.x;
                        changedProperties.Add(PropertyName.syncPosition);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valsyncRotation = (BanterBool)values[i];
                    if (valsyncRotation.n == PropertyName.syncRotation)
                    {
                        syncRotation = valsyncRotation.x;
                        changedProperties.Add(PropertyName.syncRotation);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valtakeOwnershipOnCollision = (BanterBool)values[i];
                    if (valtakeOwnershipOnCollision.n == PropertyName.takeOwnershipOnCollision)
                    {
                        takeOwnershipOnCollision = valtakeOwnershipOnCollision.x;
                        changedProperties.Add(PropertyName.takeOwnershipOnCollision);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valtakeOwnershipOnGrab = (BanterBool)values[i];
                    if (valtakeOwnershipOnGrab.n == PropertyName.takeOwnershipOnGrab)
                    {
                        takeOwnershipOnGrab = valtakeOwnershipOnGrab.x;
                        changedProperties.Add(PropertyName.takeOwnershipOnGrab);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valkinematicIfNotOwned = (BanterBool)values[i];
                    if (valkinematicIfNotOwned.n == PropertyName.kinematicIfNotOwned)
                    {
                        kinematicIfNotOwned = valkinematicIfNotOwned.x;
                        changedProperties.Add(PropertyName.kinematicIfNotOwned);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valdoIOwn = (BanterBool)values[i];
                    if (valdoIOwn.n == PropertyName.doIOwn)
                    {
                        doIOwn = valdoIOwn.x;
                        changedProperties.Add(PropertyName.doIOwn);
                    }
                }
            }
            if (values.Count > 0) { UpdateCallback(changedProperties); }
        }

        internal override void SyncProperties(bool force = false, Action callback = null)
        {
            var updates = new List<BanterComponentPropertyUpdate>();
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.syncPosition,
                    type = PropertyType.Bool,
                    value = syncPosition,
                    componentType = ComponentType.BanterSyncedObject,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.syncRotation,
                    type = PropertyType.Bool,
                    value = syncRotation,
                    componentType = ComponentType.BanterSyncedObject,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.takeOwnershipOnCollision,
                    type = PropertyType.Bool,
                    value = takeOwnershipOnCollision,
                    componentType = ComponentType.BanterSyncedObject,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.takeOwnershipOnGrab,
                    type = PropertyType.Bool,
                    value = takeOwnershipOnGrab,
                    componentType = ComponentType.BanterSyncedObject,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.kinematicIfNotOwned,
                    type = PropertyType.Bool,
                    value = kinematicIfNotOwned,
                    componentType = ComponentType.BanterSyncedObject,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.doIOwn,
                    type = PropertyType.Bool,
                    value = doIOwn,
                    componentType = ComponentType.BanterSyncedObject,
                    oid = oid,
                    cid = cid
                });
            }
            scene.SetFromUnityProperties(updates, callback);
        }

        internal override void WatchProperties(PropertyName[] properties)
        {
        }
        // END BANTER COMPILED CODE 
    }
}