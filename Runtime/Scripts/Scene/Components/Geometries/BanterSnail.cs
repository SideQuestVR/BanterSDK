using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Banter.SDK
{
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BanterObjectId))]
    [WatchComponent]
    public class BanterSnail : BanterComponentBase
    {
        [Tooltip("The number of stacks to divide the shape into.")]
        [See(initial = "5")][SerializeField] internal int stacks = 5;
        [Tooltip("The number of slices to divide the shape into.")]
        [See(initial = "5")][SerializeField] internal int slices = 5;

        internal override void StartStuff()
        {
            SetupGeometry();
            SetLoadedIfNot();
        }

        void SetupGeometry()
        {
            var geometry = GetComponent<BanterGeometry>();
            var shouldSetGeometry = false;
            if (geometry == null)
            {
                shouldSetGeometry = true;
                geometry = gameObject.AddComponent<BanterGeometry>();
            }
            geometry.geometryType = GeometryType.ParametricGeometry;
            geometry.parametricType = ParametricGeometryType.Snail;
            geometry.stacks = stacks;
            geometry.slices = slices;
            if (shouldSetGeometry)
            {
                geometry.SetGeometry();
            }
        }

        internal override void DestroyStuff()
        {
            var geometry = GetComponent<BanterGeometry>();
            if (geometry)
            {
                Destroy(geometry);
            }
        }

        internal void UpdateCallback(List<PropertyName> changedProperties)
        {
            SetupGeometry();
        }
        // BANTER COMPILED CODE 
        public System.Int32 Stacks { get { return stacks; } set { stacks = value; UpdateCallback(new List<PropertyName> { PropertyName.stacks }); } }
        public System.Int32 Slices { get { return slices; } set { slices = value; UpdateCallback(new List<PropertyName> { PropertyName.slices }); } }

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
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.stacks, PropertyName.slices, };
            UpdateCallback(changedProperties);
        }

        internal override void Init(List<object> constructorProperties = null)
        {
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(gameObject.GetInstanceID(), GetInstanceID(), ComponentType.BanterSnail);


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
                    var valstacks = (BanterInt)values[i];
                    if (valstacks.n == PropertyName.stacks)
                    {
                        stacks = valstacks.x;
                        changedProperties.Add(PropertyName.stacks);
                    }
                }
                if (values[i] is BanterInt)
                {
                    var valslices = (BanterInt)values[i];
                    if (valslices.n == PropertyName.slices)
                    {
                        slices = valslices.x;
                        changedProperties.Add(PropertyName.slices);
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
                    name = PropertyName.stacks,
                    type = PropertyType.Int,
                    value = stacks,
                    componentType = ComponentType.BanterSnail,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.slices,
                    type = PropertyType.Int,
                    value = slices,
                    componentType = ComponentType.BanterSnail,
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