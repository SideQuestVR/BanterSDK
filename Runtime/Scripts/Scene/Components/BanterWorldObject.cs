using System;
using System.Collections;
using System.Collections.Generic;
#if BANTER_FLEX
using Banter.FlexaBody;
#endif
using UnityEngine;

namespace Banter.SDK
{
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BanterObjectId))]
    [WatchComponent]
    public class BanterWorldObject : BanterComponentBase
    {

        /// <summary>
        /// Array of colliders associated with this object.
        /// This array is automatically populated when colliders are collected.
        /// </summary>
        [Tooltip("Automatically populated array of colliders associated with this object.")]

#if BANTER_FLEX
        WorldObject worldObj;
        bool worldObjectAdded;
#endif

        internal override void DestroyStuff()
        {
#if BANTER_FLEX
            if (worldObjectAdded && worldObj)
            {
                Destroy(worldObj);
            }
#endif
        }

        internal override void StartStuff()
        {
#if BANTER_FLEX
            worldObj = GetComponent<WorldObject>();
            if (worldObj == null)
            {
                worldObjectAdded = true;
                worldObj = gameObject.AddComponent<WorldObject>();
            }
            worldObj.RB = GetComponent<Rigidbody>();
#else
            // Stub implementation when BANTER_FLEX is not available
#endif
            SetLoadedIfNot();
        }

        internal void UpdateCallback(List<PropertyName> changedProperties)
        {
            // SetupPhysicMaterial(changedProperties);
        }
        // BANTER COMPILED CODE 
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
            List<PropertyName> changedProperties = new List<PropertyName>() { };
            UpdateCallback(changedProperties);
        }

        internal override void Init(List<object> constructorProperties = null)
        {
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(gameObject.GetInstanceID(), GetInstanceID(), ComponentType.BanterWorldObject);


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
            var updates = new List<BanterComponentPropertyUpdate>();
            scene.SetFromUnityProperties(updates, callback);
        }

        internal override void WatchProperties(PropertyName[] properties)
        {
        }
        // END BANTER COMPILED CODE 
    }
}