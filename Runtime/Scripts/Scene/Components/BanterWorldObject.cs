using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Banter.SDK {

    [RequireComponent(typeof(BanterObjectId))]
    [WatchComponent]
    public class BanterWorldObject : BanterComponentBase
    {
        [Method]
        public void _CollectColliders() {
            scene.events.OnWorldObjectCollectColliders.Invoke(this);
        }

        public override void DestroyStuff()
        {
            // throw new NotImplementedException();
        }

        public override void StartStuff()
        {
            scene.events.OnWorldObject.Invoke(this);
        }

        public void UpdateCallback(List<PropertyName> changedProperties)
        {
            // SetupPhysicMaterial(changedProperties);
        }
        // BANTER COMPILED CODE 
        BanterScene scene;
        bool alreadyStarted = false;
        void Start()
        {
            Init();
            StartStuff();
        }

        public override void ReSetup()
        {
            List<PropertyName> changedProperties = new List<PropertyName>() { };
            UpdateCallback(changedProperties);
        }

        public override void Init(List<object> constructorProperties = null)
        {
            scene = BanterScene.Instance();
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

        void CollectColliders()
        {
            _CollectColliders();
        }
        public override object CallMethod(string methodName, List<object> parameters)
        {

            if (methodName == "CollectColliders" && parameters.Count == 0)
            {
                CollectColliders();
                return null;
            }
            else
            {
                return null;
            }
        }

        public override void Deserialise(List<object> values)
        {
            List<PropertyName> changedProperties = new List<PropertyName>();
            for (int i = 0; i < values.Count; i++)
            {
            }
            if (values.Count > 0) { UpdateCallback(changedProperties); }
        }

        public override void SyncProperties(bool force = false, Action callback = null)
        {
            var updates = new List<BanterComponentPropertyUpdate>();
            scene.SetFromUnityProperties(updates, callback);
        }

        public override void WatchProperties(PropertyName[] properties)
        {
        }
        // END BANTER COMPILED CODE 
    }
}