using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Banter.SDK
{
    [WatchComponent(typeof(BoxCollider))]
    [RequireComponent(typeof(BoxCollider))]
    [RequireComponent(typeof(BanterObjectId))]
    public class BanterBoxCollider : UnityComponentBase
    {
        [See(initial = "false")][SerializeField] internal bool isTrigger = false;
        [See(initial = "0,0,0")][SerializeField] internal Vector3 center = Vector3.zero;
        [See(initial = "1,1,1")][SerializeField] internal Vector3 size = Vector3.one;
        // BANTER COMPILED CODE 
        public System.Boolean IsTrigger { get { return isTrigger; } set { isTrigger = value; } }
        public UnityEngine.Vector3 Center { get { return center; } set { center = value; } }
        public UnityEngine.Vector3 Size { get { return size; } set { size = value; } }
        public BoxCollider _componentType;
        public BoxCollider componentType
        {
            get
            {
                if (_componentType == null)
                {
                    _componentType = GetComponent<BoxCollider>();
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