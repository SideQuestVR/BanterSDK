using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Banter.SDK
{
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BanterObjectId))]
    [WatchComponent]
    public class BanterCylinder : BanterComponentBase
    {
        [Tooltip("Radius of the top of the cylinder")]
        [See(initial = "1")][SerializeField] internal float topRadius;
        [Tooltip("Radius of the bottom of the cylinder")]
        [See(initial = "1")][SerializeField] internal float bottomRadius;
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

        void SetupGeometry()
        {
            var geometry = GetComponent<BanterGeometry>();
            var shouldSetGeometry = false;
            if (geometry == null)
            {
                shouldSetGeometry = true;
                geometry = gameObject.AddComponent<BanterGeometry>();
            }
            geometry.geometryType = GeometryType.CylinderGeometry;
            geometry.radiusTop = topRadius;
            geometry.radiusBottom = bottomRadius;
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
            var geometry = GetComponent<BanterGeometry>();
            if (geometry)
            {
                Destroy(geometry);
            }
            var material = GetComponent<BanterMaterial>();
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
        public System.Single TopRadius { get { return topRadius; } set { topRadius = value; UpdateCallback(new List<PropertyName> { PropertyName.topRadius }); } }
        public System.Single BottomRadius { get { return bottomRadius; } set { bottomRadius = value; UpdateCallback(new List<PropertyName> { PropertyName.bottomRadius }); } }
        public System.Single Height { get { return height; } set { height = value; UpdateCallback(new List<PropertyName> { PropertyName.height }); } }
        public System.Int32 RadialSegments { get { return radialSegments; } set { radialSegments = value; UpdateCallback(new List<PropertyName> { PropertyName.radialSegments }); } }
        public System.Int32 HeightSegments { get { return heightSegments; } set { heightSegments = value; UpdateCallback(new List<PropertyName> { PropertyName.heightSegments }); } }
        public System.Boolean OpenEnded { get { return openEnded; } set { openEnded = value; UpdateCallback(new List<PropertyName> { PropertyName.openEnded }); } }
        public System.Single ThetaStart { get { return thetaStart; } set { thetaStart = value; UpdateCallback(new List<PropertyName> { PropertyName.thetaStart }); } }
        public System.Single ThetaLength { get { return thetaLength; } set { thetaLength = value; UpdateCallback(new List<PropertyName> { PropertyName.thetaLength }); } }

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
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.topRadius, PropertyName.bottomRadius, PropertyName.height, PropertyName.radialSegments, PropertyName.heightSegments, PropertyName.openEnded, PropertyName.thetaStart, PropertyName.thetaLength, };
            UpdateCallback(changedProperties);
        }

        internal override void Init(List<object> constructorProperties = null)
        {
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(gameObject.GetInstanceID(), GetInstanceID(), ComponentType.BanterCylinder);


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
                    var valtopRadius = (BanterFloat)values[i];
                    if (valtopRadius.n == PropertyName.topRadius)
                    {
                        topRadius = valtopRadius.x;
                        changedProperties.Add(PropertyName.topRadius);
                    }
                }
                if (values[i] is BanterFloat)
                {
                    var valbottomRadius = (BanterFloat)values[i];
                    if (valbottomRadius.n == PropertyName.bottomRadius)
                    {
                        bottomRadius = valbottomRadius.x;
                        changedProperties.Add(PropertyName.bottomRadius);
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
                    var valradialSegments = (BanterInt)values[i];
                    if (valradialSegments.n == PropertyName.radialSegments)
                    {
                        radialSegments = valradialSegments.x;
                        changedProperties.Add(PropertyName.radialSegments);
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
                if (values[i] is BanterBool)
                {
                    var valopenEnded = (BanterBool)values[i];
                    if (valopenEnded.n == PropertyName.openEnded)
                    {
                        openEnded = valopenEnded.x;
                        changedProperties.Add(PropertyName.openEnded);
                    }
                }
                if (values[i] is BanterFloat)
                {
                    var valthetaStart = (BanterFloat)values[i];
                    if (valthetaStart.n == PropertyName.thetaStart)
                    {
                        thetaStart = valthetaStart.x;
                        changedProperties.Add(PropertyName.thetaStart);
                    }
                }
                if (values[i] is BanterFloat)
                {
                    var valthetaLength = (BanterFloat)values[i];
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
            var updates = new List<BanterComponentPropertyUpdate>();
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.topRadius,
                    type = PropertyType.Float,
                    value = topRadius,
                    componentType = ComponentType.BanterCylinder,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.bottomRadius,
                    type = PropertyType.Float,
                    value = bottomRadius,
                    componentType = ComponentType.BanterCylinder,
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
                    componentType = ComponentType.BanterCylinder,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.radialSegments,
                    type = PropertyType.Int,
                    value = radialSegments,
                    componentType = ComponentType.BanterCylinder,
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
                    componentType = ComponentType.BanterCylinder,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.openEnded,
                    type = PropertyType.Bool,
                    value = openEnded,
                    componentType = ComponentType.BanterCylinder,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.thetaStart,
                    type = PropertyType.Float,
                    value = thetaStart,
                    componentType = ComponentType.BanterCylinder,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.thetaLength,
                    type = PropertyType.Float,
                    value = thetaLength,
                    componentType = ComponentType.BanterCylinder,
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