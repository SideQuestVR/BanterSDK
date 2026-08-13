using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BS
{
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BSObjectId))]
    [WatchComponent]
    public class BSCylinder : BSComponentBase
    {
        [Tooltip("Radius of the top of the cylinder")]
        [See(initial = "1")][SerializeField] internal float radiusTop;
        [Tooltip("Radius of the bottom of the cylinder")]
        [See(initial = "1")][SerializeField] internal float radiusBottom;
        [Tooltip("Height of the cylinder")]
        [See(initial = "1")][SerializeField] internal float height;
        [Tooltip("Number of segments around the cylinder")]
        [See(initial = "32")][SerializeField] internal int radialSegments;
        [Tooltip("Number of segments along the height of the cylinder")]
        [See(initial = "1")][SerializeField] internal int heightSegments;
        [Tooltip("Whether the cylinder is open-ended")]
        [See(initial = "false")][SerializeField] internal bool openEnded;
        [Tooltip("Start angle of the cylinder in radians")]
        [See(initial = "0")][SerializeField] internal float thetaStart;
        [Tooltip("Angle length of the cylinder in radians")]
        [See(initial = "Math.PI * 2")][SerializeField] internal float thetaLength;
        [Tooltip("Radius of the cylinder")]


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
            geometry.geometryType = GeometryType.CylinderGeometry;
            geometry.radiusTop = radiusTop;
            geometry.radiusBottom = radiusBottom;
            geometry.height = height;
            geometry.radialSegments = radialSegments;
            geometry.heightSegments = heightSegments;
            geometry.openEnded = openEnded;
            geometry.thetaStart = thetaStart;
            geometry.thetaLength = thetaLength;

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
            var material = GetComponent<BSMaterial>();
            if (material)
            {
                Destroy(material);
            }

         }
        internal void UpdateCallback(List<PropertyName> changedProperties)
        {
            SetupGeometry();
        }
        // BANTER COMPILED CODE 
        public System.Single RadiusTop { get { return radiusTop; } set { radiusTop = value; UpdateCallback(new List<PropertyName> { PropertyName.radiusTop }); } }
        public System.Single RadiusBottom { get { return radiusBottom; } set { radiusBottom = value; UpdateCallback(new List<PropertyName> { PropertyName.radiusBottom }); } }
        public System.Single Height { get { return height; } set { height = value; UpdateCallback(new List<PropertyName> { PropertyName.height }); } }
        public System.Int32 RadialSegments { get { return radialSegments; } set { radialSegments = value; UpdateCallback(new List<PropertyName> { PropertyName.radialSegments }); } }
        public System.Int32 HeightSegments { get { return heightSegments; } set { heightSegments = value; UpdateCallback(new List<PropertyName> { PropertyName.heightSegments }); } }
        public System.Boolean OpenEnded { get { return openEnded; } set { openEnded = value; UpdateCallback(new List<PropertyName> { PropertyName.openEnded }); } }
        public System.Single ThetaStart { get { return thetaStart; } set { thetaStart = value; UpdateCallback(new List<PropertyName> { PropertyName.thetaStart }); } }
        public System.Single ThetaLength { get { return thetaLength; } set { thetaLength = value; UpdateCallback(new List<PropertyName> { PropertyName.thetaLength }); } }

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
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.radiusTop, PropertyName.radiusBottom, PropertyName.height, PropertyName.radialSegments, PropertyName.heightSegments, PropertyName.openEnded, PropertyName.thetaStart, PropertyName.thetaLength, };
            UpdateCallback(changedProperties);
        }
        internal override string GetSignature()
        {
            return "Cylinder" +  PropertyName.radiusTop + radiusTop + PropertyName.radiusBottom + radiusBottom + PropertyName.height + height + PropertyName.radialSegments + radialSegments + PropertyName.heightSegments + heightSegments + PropertyName.openEnded + openEnded + PropertyName.thetaStart + thetaStart + PropertyName.thetaLength + thetaLength;
        }

        internal override void Init(List<object> constructorProperties = null)
        {
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(gameObject.GetInstanceID(), GetInstanceID(), ComponentType.Cylinder);


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
                    var valradiusTop = (BSFloat)values[i];
                    if (valradiusTop.n == PropertyName.radiusTop)
                    {
                        radiusTop = valradiusTop.x;
                        changedProperties.Add(PropertyName.radiusTop);
                    }
                }
                if (values[i] is BSFloat)
                {
                    var valradiusBottom = (BSFloat)values[i];
                    if (valradiusBottom.n == PropertyName.radiusBottom)
                    {
                        radiusBottom = valradiusBottom.x;
                        changedProperties.Add(PropertyName.radiusBottom);
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
                if (values[i] is BSBool)
                {
                    var valopenEnded = (BSBool)values[i];
                    if (valopenEnded.n == PropertyName.openEnded)
                    {
                        openEnded = valopenEnded.x;
                        changedProperties.Add(PropertyName.openEnded);
                    }
                }
                if (values[i] is BSFloat)
                {
                    var valthetaStart = (BSFloat)values[i];
                    if (valthetaStart.n == PropertyName.thetaStart)
                    {
                        thetaStart = valthetaStart.x;
                        changedProperties.Add(PropertyName.thetaStart);
                    }
                }
                if (values[i] is BSFloat)
                {
                    var valthetaLength = (BSFloat)values[i];
                    if (valthetaLength.n == PropertyName.thetaLength)
                    {
                        thetaLength = valthetaLength.x;
                        changedProperties.Add(PropertyName.thetaLength);
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
                    name = PropertyName.radiusTop,
                    type = PropertyType.Float,
                    value = radiusTop,
                    componentType = ComponentType.Cylinder,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.radiusBottom,
                    type = PropertyType.Float,
                    value = radiusBottom,
                    componentType = ComponentType.Cylinder,
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
                    componentType = ComponentType.Cylinder,
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
                    componentType = ComponentType.Cylinder,
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
                    componentType = ComponentType.Cylinder,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.openEnded,
                    type = PropertyType.Bool,
                    value = openEnded,
                    componentType = ComponentType.Cylinder,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.thetaStart,
                    type = PropertyType.Float,
                    value = thetaStart,
                    componentType = ComponentType.Cylinder,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.thetaLength,
                    type = PropertyType.Float,
                    value = thetaLength,
                    componentType = ComponentType.Cylinder,
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