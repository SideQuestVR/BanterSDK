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
    }
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BanterObjectId))]
    [WatchComponent]
    public class BanterSyncedObject : BanterComponentBase
    {
        [Tooltip("Determines if the object's position is synchronized across all clients.")]
        [See(initial = "true")][SerializeField] internal bool syncPosition = true;

        [Tooltip("Determines if the object's rotation is synchronized across all clients.")]
        [See(initial = "true")][SerializeField] internal bool syncRotation = true;

        [Tooltip("Determines if ownership is taken when the object collides with another object.")]
        [See(initial = "true")][SerializeField] internal bool takeOwnershipOnCollision = true;

        [Tooltip("Determines if ownership is taken when the object is grabbed.")]
        [See(initial = "true")][SerializeField] internal bool takeOwnershipOnGrab = true;

        [Tooltip("If enabled, the object becomes kinematic when it is not owned by the local player.")]
        [See(initial = "false")][SerializeField] internal bool kinematicIfNotOwned = false;


        [Method]
        public void _TakeOwnership()
        {
            scene.events.OnTakeOwnership.Invoke(synced, banterObjectId);
        }
        [Method]
        public bool _DoIOwn()
        {
            return scene.data.NSODoIOwn(synced, banterObjectId);
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
        internal void UpdateCallback(List<PropertyName> changedProperties)
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
        public System.Boolean SyncPosition { get { return syncPosition; } set { syncPosition = value; UpdateCallback(new List<PropertyName> { PropertyName.syncPosition }); } }
        public System.Boolean SyncRotation { get { return syncRotation; } set { syncRotation = value; UpdateCallback(new List<PropertyName> { PropertyName.syncRotation }); } }
        public System.Boolean TakeOwnershipOnCollision { get { return takeOwnershipOnCollision; } set { takeOwnershipOnCollision = value; UpdateCallback(new List<PropertyName> { PropertyName.takeOwnershipOnCollision }); } }
        public System.Boolean TakeOwnershipOnGrab { get { return takeOwnershipOnGrab; } set { takeOwnershipOnGrab = value; UpdateCallback(new List<PropertyName> { PropertyName.takeOwnershipOnGrab }); } }
        public System.Boolean KinematicIfNotOwned { get { return kinematicIfNotOwned; } set { kinematicIfNotOwned = value; UpdateCallback(new List<PropertyName> { PropertyName.kinematicIfNotOwned }); } }

        BanterScene _scene;
        public BanterScene scene
        {
            get
            {
                if (_scene == null)
                {
                    _scene = BanterScene.Instance();
                }
                return _scene;
            }
        }
        bool alreadyStarted = false;
        void Start()
        {
            Init();
            StartStuff();
        }

        internal override void ReSetup()
        {
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.syncPosition, PropertyName.syncRotation, PropertyName.takeOwnershipOnCollision, PropertyName.takeOwnershipOnGrab, PropertyName.kinematicIfNotOwned, };
            UpdateCallback(changedProperties);
        }

        internal override void Init(List<object> constructorProperties = null)
        {
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
        Boolean DoIOwn()
        {
            return _DoIOwn();
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
                return DoIOwn();
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
            scene.SetFromUnityProperties(updates, callback);
        }

        internal override void WatchProperties(PropertyName[] properties)
        {
        }
        // END BANTER COMPILED CODE 
    }
}