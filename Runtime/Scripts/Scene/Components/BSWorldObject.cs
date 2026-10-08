using System;
using System.Collections;
using System.Collections.Generic;
using SideQuest.FlexaBody;
using UnityEngine;

namespace BS
{
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BSObjectId))]
    [WatchComponent]
    public class BSWorldObject : BSComponentBase
    {

        WorldObject worldObj;

        bool worldObjectAdded;

        internal override void DestroyStuff()
        {
            if (worldObjectAdded && worldObj)
            {
                Destroy(worldObj);
            }
        }

        internal override void StartStuff()
        {
            // FlexaBody's WorldObject in Banter, the SDK's port of it otherwise (Utils/FlexaBody).
            worldObj = GetComponent<WorldObject>();
            if (worldObj == null)
            {
                worldObjectAdded = true;
                worldObj = gameObject.AddComponent<WorldObject>();
            }
            worldObj.RB = GetComponent<Rigidbody>();
            SetLoadedIfNot();
        }

        internal void UpdateCallback(List<PropertyName> changedProperties)
        {
            // SetupPhysicMaterial(changedProperties);
        }

        internal override void UpdateStuff()
        {

        }
        // BANTER COMPILED CODE 
        BSScene _scene;
        public BSScene scene
        {
            get
            {
                if (_scene == null)
                {
                    _scene = BSScene.Instance();
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
            List<PropertyName> changedProperties = new List<PropertyName>() { };
            UpdateCallback(changedProperties);
        }
        internal override string GetSignature()
        {
            return "WorldObject";
        }

        internal override void Init(List<object> constructorProperties = null)
        {
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(gameObject.GetInstanceID(), GetInstanceID(), ComponentType.WorldObject);


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
            BSScene.Instance().RegisterComponentOnMainThread(gameObject, this);
        }

        void OnDestroy()
        {
            scene.UnregisterComponentOnMainThread(gameObject, this);

            DestroyStuff();
        }

        internal override object CallMethod(string methodName, List<object> parameters)
        {
            return null;
        }

        internal override void Deserialise(List<object> values)
        {
            List<PropertyName> changedProperties = new List<PropertyName>();
            for (int i = 0; i < values.Count; i++)
            {
            }
            if (values.Count > 0) { UpdateCallback(changedProperties); }
        }

        internal override void SyncProperties(bool force = false, Action callback = null)
        {
            var updates = new List<BSComponentPropertyUpdate>();
            scene.SetFromUnityProperties(updates, callback);
        }

        internal override void WatchProperties(PropertyName[] properties)
        {
        }
        // END BANTER COMPILED CODE 
    }
}