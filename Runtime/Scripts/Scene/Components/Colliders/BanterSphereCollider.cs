using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Banter.SDK
{
    /* 
    #### Sphere Collider
    Add a sphere shaped physics collider to the object.

    **Properties**
    - `isTrigger` - If the collider is a trigger.
    - `radius` - The radius of the sphere.

    **Code Example**
    ```js
        const isTrigger = false;
        const radius = 0.5;
        const gameObject = new BS.GameObject("MySphereCollider"); 
        const sphereCollider = await gameObject.AddComponent(new BS.BanterSphereCollider(isTrigger, radius));
    ```
    */
    [WatchComponent(typeof(SphereCollider))]
    [RequireComponent(typeof(SphereCollider))]
    [RequireComponent(typeof(BanterObjectId))]
    public class BanterSphereCollider : UnityComponentBase
    {
        [See(initial = "false")][SerializeField] internal bool isTrigger;
        [See(initial = "0.5")][SerializeField] internal float radius = 0.5f;
        // BANTER COMPILED CODE 
        public System.Boolean IsTrigger { get { return isTrigger; } set { isTrigger = value; } }
        public System.Single Radius { get { return radius; } set { radius = value; } }
        public SphereCollider _componentType;
        public SphereCollider componentType
        {
            get
            {
                if (_componentType == null)
                {
                    _componentType = GetComponent<SphereCollider>();
                }
                return _componentType;
            }
        }
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

        }

        internal override void Init(List<object> constructorProperties = null)
        {
            if (alreadyStarted) { return; }
            alreadyStarted = true;



            oid = gameObject.GetInstanceID();
            cid = GetInstanceID();

            if (constructorProperties != null)
            {
                Deserialise(constructorProperties);
            }

            SyncProperties(true);
            SetLoadedIfNot();
        }

        void Awake()
        {
            BanterScene.Instance().RegisterComponentOnMainThread(gameObject, this);
        }

        void OnDestroy()
        {
            scene.UnregisterComponentOnMainThread(gameObject, this);

            Destroy(componentType);
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
                if (values[i] is BanterBool)
                {
                    var valisTrigger = (BanterBool)values[i];
                    if (valisTrigger.n == PropertyName.isTrigger)
                    {
                        componentType.isTrigger = valisTrigger.x;
                        changedProperties.Add(PropertyName.isTrigger);
                    }
                }
                if (values[i] is BanterFloat)
                {
                    var valradius = (BanterFloat)values[i];
                    if (valradius.n == PropertyName.radius)
                    {
                        componentType.radius = valradius.x;
                        changedProperties.Add(PropertyName.radius);
                    }
                }
            }
        }

        internal override void SyncProperties(bool force = false, Action callback = null)
        {
            var updates = new List<BanterComponentPropertyUpdate>();
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.isTrigger,
                    type = PropertyType.Bool,
                    value = componentType.isTrigger,
                    componentType = ComponentType.SphereCollider,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.radius,
                    type = PropertyType.Float,
                    value = componentType.radius,
                    componentType = ComponentType.SphereCollider,
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