using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Banter.SDK
{
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BSObjectId))]
    [WatchComponent]
    public class BSBox : BSComponentBase
    {
        [Tooltip("The width of the box.")]
        [See(initial = "1")][SerializeField] internal float width;
        [Tooltip("The height of the box.")]
        [See(initial = "1")][SerializeField] internal float height;
        [Tooltip("The depth of the box.")]
        [See(initial = "1")][SerializeField] internal float depth;
        [Tooltip("The number of width segments to divide the box into.")]
        [See(initial = "1")][SerializeField] internal int widthSegments = 1;
        [Tooltip("The number of height segments to divide the box into.")]
        [See(initial = "1")][SerializeField] internal int heightSegments = 1;
        [Tooltip("The number of depth segments to divide the box into.")]
        [See(initial = "1")][SerializeField] internal int depthSegments = 1;
        internal override void StartStuff()
        {
            SetupGeometry();
            SetLoadedIfNot();
        }

        internal override void UpdateStuff()
        {
            
        }

        void SetupGeometry()
        {
            var geometry = GetComponent<BSGeometry>();
            var shouldSetGeometry = false;
            if (geometry == null)
            {
                shouldSetGeometry = true;
                geometry = gameObject.AddComponent<BSGeometry>();
            }
            geometry.geometryType = GeometryType.BoxGeometry;
            geometry.width = width;
            geometry.height = height;
            geometry.depth = depth;
            geometry.widthSegments = widthSegments;
            geometry.heightSegments = heightSegments;
            geometry.depthSegments = depthSegments;
            if (shouldSetGeometry)
            {
                geometry.SetGeometry();
            }
           
        }

        internal override void DestroyStuff()
        {
            var geometry = GetComponent<BSGeometry>();
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
        public System.Single Width { get { return width; } set { width = value; UpdateCallback(new List<PropertyName> { PropertyName.width }); } }
        public System.Single Height { get { return height; } set { height = value; UpdateCallback(new List<PropertyName> { PropertyName.height }); } }
        public System.Single Depth { get { return depth; } set { depth = value; UpdateCallback(new List<PropertyName> { PropertyName.depth }); } }
        public System.Int32 WidthSegments { get { return widthSegments; } set { widthSegments = value; UpdateCallback(new List<PropertyName> { PropertyName.widthSegments }); } }
        public System.Int32 HeightSegments { get { return heightSegments; } set { heightSegments = value; UpdateCallback(new List<PropertyName> { PropertyName.heightSegments }); } }
        public System.Int32 DepthSegments { get { return depthSegments; } set { depthSegments = value; UpdateCallback(new List<PropertyName> { PropertyName.depthSegments }); } }

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
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.width, PropertyName.height, PropertyName.depth, PropertyName.widthSegments, PropertyName.heightSegments, PropertyName.depthSegments, };
            UpdateCallback(changedProperties);
        }
        internal override string GetSignature()
        {
            return "Box" +  PropertyName.width + width + PropertyName.height + height + PropertyName.depth + depth + PropertyName.widthSegments + widthSegments + PropertyName.heightSegments + heightSegments + PropertyName.depthSegments + depthSegments;
        }

        internal override void Init(List<object> constructorProperties = null)
        {
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(gameObject.GetInstanceID(), GetInstanceID(), ComponentType.Box);


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
            BSScene.Instance().RegisterComponentOnMainThread(gameObject, this);
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
                if (values[i] is BSFloat)
                {
                    var valwidth = (BSFloat)values[i];
                    if (valwidth.n == PropertyName.width)
                    {
                        width = valwidth.x;
                        changedProperties.Add(PropertyName.width);
                    }
                }
                if (values[i] is BSFloat)
                {
                    var valheight = (BSFloat)values[i];
                    if (valheight.n == PropertyName.height)
                    {
                        height = valheight.x;
                        changedProperties.Add(PropertyName.height);
                    }
                }
                if (values[i] is BSFloat)
                {
                    var valdepth = (BSFloat)values[i];
                    if (valdepth.n == PropertyName.depth)
                    {
                        depth = valdepth.x;
                        changedProperties.Add(PropertyName.depth);
                    }
                }
                if (values[i] is BSInt)
                {
                    var valwidthSegments = (BSInt)values[i];
                    if (valwidthSegments.n == PropertyName.widthSegments)
                    {
                        widthSegments = valwidthSegments.x;
                        changedProperties.Add(PropertyName.widthSegments);
                    }
                }
                if (values[i] is BSInt)
                {
                    var valheightSegments = (BSInt)values[i];
                    if (valheightSegments.n == PropertyName.heightSegments)
                    {
                        heightSegments = valheightSegments.x;
                        changedProperties.Add(PropertyName.heightSegments);
                    }
                }
                if (values[i] is BSInt)
                {
                    var valdepthSegments = (BSInt)values[i];
                    if (valdepthSegments.n == PropertyName.depthSegments)
                    {
                        depthSegments = valdepthSegments.x;
                        changedProperties.Add(PropertyName.depthSegments);
                    }
                }
            }
            if (values.Count > 0) { UpdateCallback(changedProperties); }
        }

        internal override void SyncProperties(bool force = false, Action callback = null)
        {
            var updates = new List<BSComponentPropertyUpdate>();
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.width,
                    type = PropertyType.Float,
                    value = width,
                    componentType = ComponentType.Box,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.height,
                    type = PropertyType.Float,
                    value = height,
                    componentType = ComponentType.Box,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.depth,
                    type = PropertyType.Float,
                    value = depth,
                    componentType = ComponentType.Box,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.widthSegments,
                    type = PropertyType.Int,
                    value = widthSegments,
                    componentType = ComponentType.Box,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.heightSegments,
                    type = PropertyType.Int,
                    value = heightSegments,
                    componentType = ComponentType.Box,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.depthSegments,
                    type = PropertyType.Int,
                    value = depthSegments,
                    componentType = ComponentType.Box,
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