
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Banter.SDK
{
    public enum BanterMonoBehaviourLifeCycle
    {
        Start,
        Update,
        OnDestroy
    }

    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BanterObjectId))]
    [WatchComponent]
    public class BanterMonoBehaviour : BanterComponentBase
    {
        [See(initial = "20")][SerializeField] internal int fps = 20;

        [See(initial = "")][SerializeField] internal string startFunction = "";
        [See(initial = "")][SerializeField] internal string updateFunction = "";
        [See(initial = "")][SerializeField] internal string destroyFunction = "";

        internal override void StartStuff()
        {
            
        }

        internal override void DestroyStuff()
        {

        }
        internal void UpdateCallback(List<PropertyName> changedProperties)
        {
           
        }
        // BANTER COMPILED CODE 
        public System.Int32 Fps { get { return fps; } set { fps = value; UpdateCallback(new List<PropertyName> { PropertyName.fps }); } }
        public System.String StartFunction { get { return startFunction; } set { startFunction = value; UpdateCallback(new List<PropertyName> { PropertyName.startFunction }); } }
        public System.String UpdateFunction { get { return updateFunction; } set { updateFunction = value; UpdateCallback(new List<PropertyName> { PropertyName.updateFunction }); } }
        public System.String DestroyFunction { get { return destroyFunction; } set { destroyFunction = value; UpdateCallback(new List<PropertyName> { PropertyName.destroyFunction }); } }

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
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.fps, PropertyName.startFunction, PropertyName.updateFunction, PropertyName.destroyFunction, };
            UpdateCallback(changedProperties);
        }

        internal override void Init(List<object> constructorProperties = null)
        {
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(gameObject.GetInstanceID(), GetInstanceID(), ComponentType.BanterMonoBehaviour);


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
                if (values[i] is BanterInt)
                {
                    var valfps = (BanterInt)values[i];
                    if (valfps.n == PropertyName.fps)
                    {
                        fps = valfps.x;
                        changedProperties.Add(PropertyName.fps);
                    }
                }
                if (values[i] is BanterString)
                {
                    var valstartFunction = (BanterString)values[i];
                    if (valstartFunction.n == PropertyName.startFunction)
                    {
                        startFunction = valstartFunction.x;
                        changedProperties.Add(PropertyName.startFunction);
                    }
                }
                if (values[i] is BanterString)
                {
                    var valupdateFunction = (BanterString)values[i];
                    if (valupdateFunction.n == PropertyName.updateFunction)
                    {
                        updateFunction = valupdateFunction.x;
                        changedProperties.Add(PropertyName.updateFunction);
                    }
                }
                if (values[i] is BanterString)
                {
                    var valdestroyFunction = (BanterString)values[i];
                    if (valdestroyFunction.n == PropertyName.destroyFunction)
                    {
                        destroyFunction = valdestroyFunction.x;
                        changedProperties.Add(PropertyName.destroyFunction);
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
                    name = PropertyName.fps,
                    type = PropertyType.Int,
                    value = fps,
                    componentType = ComponentType.BanterMonoBehaviour,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.startFunction,
                    type = PropertyType.String,
                    value = startFunction,
                    componentType = ComponentType.BanterMonoBehaviour,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.updateFunction,
                    type = PropertyType.String,
                    value = updateFunction,
                    componentType = ComponentType.BanterMonoBehaviour,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.destroyFunction,
                    type = PropertyType.String,
                    value = destroyFunction,
                    componentType = ComponentType.BanterMonoBehaviour,
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