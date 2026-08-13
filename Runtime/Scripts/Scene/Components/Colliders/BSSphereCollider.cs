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
        const sphereCollider = await gameObject.AddComponent(new BS.BSSphereCollider(isTrigger, radius));
    ```
    */
    [WatchComponent(typeof(SphereCollider))]
    [RequireComponent(typeof(SphereCollider))]
    [RequireComponent(typeof(BSObjectId))]
    public class BSSphereCollider : UnityComponentBase
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

        }
        internal override string GetSignature()
        {
            return "SphereCollider" +  PropertyName.isTrigger + isTrigger + PropertyName.radius + radius;
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
            BSScene.Instance().RegisterComponentOnMainThread(gameObject, this);
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
                if (values[i] is BSBool)
                {
                    var valisTrigger = (BSBool)values[i];
                    if (valisTrigger.n == PropertyName.isTrigger)
                    {
                        componentType.isTrigger = valisTrigger.x;
                        changedProperties.Add(PropertyName.isTrigger);
                    }
                }
                if (values[i] is BSFloat)
                {
                    var valradius = (BSFloat)values[i];
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
            var updates = new List<BSComponentPropertyUpdate>();
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
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
                updates.Add(new BSComponentPropertyUpdate()
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

        public override UnityEngine.Object GetReferenceObject()
        {
            return componentType;
        }
        // END BANTER COMPILED CODE 
    }
}