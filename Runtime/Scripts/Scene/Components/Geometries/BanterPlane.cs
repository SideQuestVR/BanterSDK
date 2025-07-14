using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Banter.SDK
{
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BanterObjectId))]
    [WatchComponent]
    public class BanterPlane : BanterComponentBase
    {
        [Tooltip("The width of the box.")]
        [See(initial = "1")][SerializeField] internal float width;
        [Tooltip("The height of the box.")]
        [See(initial = "1")][SerializeField] internal float height;
        [Tooltip("The number of width segments to divide the box into.")]
        [See(initial = "1")][SerializeField] internal int widthSegments = 1;
        [Tooltip("The number of height segments to divide the box into.")]
        [See(initial = "1")][SerializeField] internal int heightSegments = 1;
        internal override void StartStuff()
        {
            SetupGeometry();
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
            geometry.geometryType = GeometryType.BoxGeometry;
            geometry.width = width;
            geometry.height = height;
            geometry.widthSegments = widthSegments;
            geometry.heightSegments = heightSegments;
            if (shouldSetGeometry)
            {
                geometry.SetGeometry();
            }
            var material = GetComponent<BanterMaterial>();
            if (material == null)
            {
                gameObject.AddComponent<BanterMaterial>();
            }
        }

        internal override void DestroyStuff() { }
        internal void UpdateCallback(List<PropertyName> changedProperties)
        {
            SetupGeometry();
        }
        // BANTER COMPILED CODE 
        public System.Single Width { get { return width; } set { width = value; UpdateCallback(new List<PropertyName> { PropertyName.width }); } }
        public System.Single Height { get { return height; } set { height = value; UpdateCallback(new List<PropertyName> { PropertyName.height }); } }
        public System.Int32 WidthSegments { get { return widthSegments; } set { widthSegments = value; UpdateCallback(new List<PropertyName> { PropertyName.widthSegments }); } }
        public System.Int32 HeightSegments { get { return heightSegments; } set { heightSegments = value; UpdateCallback(new List<PropertyName> { PropertyName.heightSegments }); } }

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
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.width, PropertyName.height, PropertyName.widthSegments, PropertyName.heightSegments, };
            UpdateCallback(changedProperties);
        }

        internal override void Init(List<object> constructorProperties = null)
        {
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(gameObject.GetInstanceID(), GetInstanceID(), ComponentType.BanterPlane);


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
                if (values[i] is BanterFloat)
                {
                    var valwidth = (BanterFloat)values[i];
                    if (valwidth.n == PropertyName.width)
                    {
                        width = valwidth.x;
                        changedProperties.Add(PropertyName.width);
                    }
                }
                if (values[i] is BanterFloat)
                {
                    var valheight = (BanterFloat)values[i];
                    if (valheight.n == PropertyName.height)
                    {
                        height = valheight.x;
                        changedProperties.Add(PropertyName.height);
                    }
                }
                if (values[i] is BanterInt)
                {
                    var valwidthSegments = (BanterInt)values[i];
                    if (valwidthSegments.n == PropertyName.widthSegments)
                    {
                        widthSegments = valwidthSegments.x;
                        changedProperties.Add(PropertyName.widthSegments);
                    }
                }
                if (values[i] is BanterInt)
                {
                    var valheightSegments = (BanterInt)values[i];
                    if (valheightSegments.n == PropertyName.heightSegments)
                    {
                        heightSegments = valheightSegments.x;
                        changedProperties.Add(PropertyName.heightSegments);
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
                    name = PropertyName.width,
                    type = PropertyType.Float,
                    value = width,
                    componentType = ComponentType.BanterPlane,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.height,
                    type = PropertyType.Float,
                    value = height,
                    componentType = ComponentType.BanterPlane,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.widthSegments,
                    type = PropertyType.Int,
                    value = widthSegments,
                    componentType = ComponentType.BanterPlane,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.heightSegments,
                    type = PropertyType.Int,
                    value = heightSegments,
                    componentType = ComponentType.BanterPlane,
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