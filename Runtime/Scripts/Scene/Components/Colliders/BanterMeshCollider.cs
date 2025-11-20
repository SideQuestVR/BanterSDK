using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Banter.SDK
{
    /* 
    #### Mesh Collider
    Add a mesh shaped physics collider to the object. This requires a mesh to be supplied, or used from a MeshFilter component.

    **Properties**
    - `isTrigger` - If the collider is a trigger.
    - `convex` - If the collider is convex i.e. has no holes or concave parts.

    **Code Example**
    ```js
        const isTrigger = false;
        const convex = true;
        const gameObject = new BS.GameObject("MyMeshCollider"); 
        const meshCollider = await gameObject.AddComponent(new BS.BanterMeshCollider(isTrigger, convex));
    ```
    */
    [WatchComponent(typeof(MeshCollider))]
    [RequireComponent(typeof(MeshCollider))]
    [RequireComponent(typeof(BanterObjectId))]
    public class BanterMeshCollider : UnityComponentBase
    {
        [See(initial = "false")][SerializeField] internal bool convex;
        [See(initial = "false")][SerializeField] internal bool isTrigger;
        // BANTER COMPILED CODE 
        public System.Boolean Convex { get { return convex; } set { convex = value; } }
        public System.Boolean IsTrigger { get { return isTrigger; } set { isTrigger = value; } }
        public MeshCollider _componentType;
        public MeshCollider componentType
        {
            get
            {
                if (_componentType == null)
                {
                    _componentType = GetComponent<MeshCollider>();
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

            var mf = GetComponent<MeshFilter>();
            if (mf != null)
            {
                componentType.sharedMesh = GetComponent<MeshFilter>().sharedMesh;
            }

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
                    var valconvex = (BanterBool)values[i];
                    if (valconvex.n == PropertyName.convex)
                    {
                        componentType.convex = valconvex.x;
                        changedProperties.Add(PropertyName.convex);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valisTrigger = (BanterBool)values[i];
                    if (valisTrigger.n == PropertyName.isTrigger)
                    {
                        if (valisTrigger.x && !componentType.convex)
                        {
                            LogLine.Do("Setting isTrigger to true but convex is false, setting convex to true");
                            componentType.convex = true;
                        }
                        componentType.isTrigger = valisTrigger.x;
                        changedProperties.Add(PropertyName.isTrigger);
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
                    name = PropertyName.convex,
                    type = PropertyType.Bool,
                    value = componentType.convex,
                    componentType = ComponentType.MeshCollider,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.isTrigger,
                    type = PropertyType.Bool,
                    value = componentType.isTrigger,
                    componentType = ComponentType.MeshCollider,
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