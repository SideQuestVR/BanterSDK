using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Banter
{
    /* 
    #### Box Collider
    Add a box shaped physics collider to the object.

    **Properties**
    - `isTrigger` - If the collider is a trigger.
    - `center` - The center of the box.
    - `size` - The size of the box.

    **Code Example**
    ```js
        const isTrigger = false;
        const center = new BS.Vector3(0,0,0);
        const size = new BS.Vector3(1,1,1);
        const gameObject = new GameObject("MyBoxCollider"); 
        const boxCollider = await gameObject.AddComponent(new BS.BanterBoxCollider(isTrigger, center, size));
    ```
    */
    [WatchComponent(typeof(BoxCollider))]
    [RequireComponent(typeof(BoxCollider))]
    [RequireComponent(typeof(BanterObjectId))]
    public class BanterBoxCollider : UnityComponentBase
    {
        [See(initial = "false")] public bool isTrigger;
        [See(initial = "0,0,0")] public Vector3 center;
        [See(initial = "1,1,1")] public Vector3 size;
        // BANTER COMPILED CODE 
        public BoxCollider _componentType;
        public BoxCollider componentType
        {
            get
            {
                if (_componentType == null)
                {
                    _componentType = GetComponent<BoxCollider>(); ;
                }
                return _componentType;
            }
        }
        BanterScene scene;

        bool alreadyStarted = false;

        void Start()
        {
            Init();
            StartStuff();
        }
        public override void ReSetup()
        {

        }


        public override void Init()
        {
            scene = BanterScene.Instance();
            if (alreadyStarted) { return; }
            alreadyStarted = true;



            oid = gameObject.GetInstanceID();
            cid = GetInstanceID();
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
        public override object CallMethod(string methodName, List<object> parameters)
        {
            return null;
        }

        public override void Deserialise(List<object> values)
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
                if (values[i] is BanterVector3)
                {
                    var valcenter = (BanterVector3)values[i];
                    if (valcenter.n == PropertyName.center)
                    {
                        componentType.center = new Vector3(valcenter.x, valcenter.y, valcenter.z);
                        changedProperties.Add(PropertyName.center);
                    }
                }
                if (values[i] is BanterVector3)
                {
                    var valsize = (BanterVector3)values[i];
                    if (valsize.n == PropertyName.size)
                    {
                        componentType.size = new Vector3(valsize.x, valsize.y, valsize.z);
                        changedProperties.Add(PropertyName.size);
                    }
                }
            }
        }
        public override void SyncProperties(bool force = false, Action callback = null)
        {
            var updates = new List<BanterComponentPropertyUpdate>();
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.isTrigger,
                    type = PropertyType.Bool,
                    value = componentType.isTrigger,
                    componentType = ComponentType.BoxCollider,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.center,
                    type = PropertyType.Vector3,
                    value = componentType.center,
                    componentType = ComponentType.BoxCollider,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.size,
                    type = PropertyType.Vector3,
                    value = componentType.size,
                    componentType = ComponentType.BoxCollider,
                    oid = oid,
                    cid = cid
                });
            }
            scene.SetFromUnityProperties(updates, callback);
        }
        public override void WatchProperties(PropertyName[] properties)
        {
        }
        // END BANTER COMPILED CODE 
    }
}