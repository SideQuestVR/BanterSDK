using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
namespace Banter.SDK
{
    /* 
    #### Banter Collider Events
    Fire collision and trigger events on the object. This component is required for the `trigger-enter` etc events to work.

    **Code Example**
    ```js
        const gameObject = new BS.GameObject("MyBanterColliderEvents"); 
        const events = await gameObject.AddComponent(new BS.BanterColliderEvents());
    ```
    */
    [RequireComponent(typeof(BanterObjectId))]
    [WatchComponent]
    public class BanterColliderEvents : BanterComponentBase
    {
        void OnCollisionEnter(Collision collision)
        {
            BanterScene.Instance().link?._OnCollisionEnter(gameObject, collision);
        }
        void OnCollisionExit(Collision collision)
        {
            BanterScene.Instance().link?._OnCollisionExit(gameObject, collision);
        }
        void OnTriggerEnter(Collider collider)
        {
            BanterScene.Instance().link?._OnTriggerEnter(gameObject, collider);
        }
        void OnTriggerExit(Collider collider)
        {
            BanterScene.Instance().link?._OnTriggerExit(gameObject, collider);
        }
        internal override void DestroyStuff() { }

        internal override void UpdateStuff()
        {
            
        }
        internal void UpdateCallback(List<PropertyName> changedProperties) { }
        internal override void StartStuff()
        {
            SetLoadedIfNot();
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
        internal override string GetSignature()
        {
            return "BanterColliderEvents";
        }

        internal override void Init(List<object> constructorProperties = null)
        {
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(gameObject.GetInstanceID(), GetInstanceID(), ComponentType.BanterColliderEvents);


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