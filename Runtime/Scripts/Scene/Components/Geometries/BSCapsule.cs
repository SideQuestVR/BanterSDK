using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BS
{
    /*
    #### Banter Capsule
    A capsule: a cylinder with hemispherical caps.

    **Properties**
    - `radius` - The overall bounding radius. The caps are half this, so the default 0.5/1 capsule is not a plain sphere.
    - `height` - The total height, caps included.
    - `radialSegments` - The number of radial segments to divide the shape into.
    - `heightSegments` - Doubles as the cap resolution; 1 is treated as 8 so the caps are not two-ring hemispheres.
    */
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BSObjectId))]
    [WatchComponent]
    public class BSCapsule : BSComponentBase
    {
        [Tooltip("The overall bounding radius. The caps are half this, so the default 0.5/1 capsule is not a plain sphere.")]
        [See(initial = "0.5")][SerializeField] internal float radius = 0.5f;
        [Tooltip("The total height, caps included.")]
        [See(initial = "1")][SerializeField] internal float height = 1f;
        [Tooltip("The number of radial segments to divide the shape into.")]
        [See(initial = "32")][SerializeField] internal int radialSegments = 32;
        [Tooltip("Doubles as the cap resolution; 1 is treated as 8 so the caps are not two-ring hemispheres.")]
        [See(initial = "1")][SerializeField] internal int heightSegments = 1;
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
            geometry.geometryType = GeometryType.CapsuleGeometry;
            geometry.radius = radius;
            geometry.height = height;
            geometry.radialSegments = radialSegments;
            geometry.heightSegments = heightSegments;
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
        public System.Single Radius { get { return radius; } set { radius = value; UpdateCallback(new List<PropertyName> { PropertyName.radius }); } }
        public System.Single Height { get { return height; } set { height = value; UpdateCallback(new List<PropertyName> { PropertyName.height }); } }
        public System.Int32 RadialSegments { get { return radialSegments; } set { radialSegments = value; UpdateCallback(new List<PropertyName> { PropertyName.radialSegments }); } }
        public System.Int32 HeightSegments { get { return heightSegments; } set { heightSegments = value; UpdateCallback(new List<PropertyName> { PropertyName.heightSegments }); } }

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
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.radius, PropertyName.height, PropertyName.radialSegments, PropertyName.heightSegments, };
            UpdateCallback(changedProperties);
        }
        internal override string GetSignature()
        {
            return "Capsule" + PropertyName.radius + radius + PropertyName.height + height + PropertyName.radialSegments + radialSegments + PropertyName.heightSegments + heightSegments;
        }

        internal override void Init(List<object> constructorProperties = null)
        {
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(gameObject.GetInstanceID(), GetInstanceID(), ComponentType.Capsule);


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
                    var valradius = (BSFloat)values[i];
                    if (valradius.n == PropertyName.radius)
                    {
                        radius = valradius.x;
                        changedProperties.Add(PropertyName.radius);
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
                if (values[i] is BSInt)
                {
                    var valradialSegments = (BSInt)values[i];
                    if (valradialSegments.n == PropertyName.radialSegments)
                    {
                        radialSegments = valradialSegments.x;
                        changedProperties.Add(PropertyName.radialSegments);
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
                    name = PropertyName.radius,
                    type = PropertyType.Float,
                    value = radius,
                    componentType = ComponentType.Capsule,
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
                    componentType = ComponentType.Capsule,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.radialSegments,
                    type = PropertyType.Int,
                    value = radialSegments,
                    componentType = ComponentType.Capsule,
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
                    componentType = ComponentType.Capsule,
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
