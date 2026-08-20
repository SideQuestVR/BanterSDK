using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BS
{
    /*
    #### Banter Tube
    A tube swept along a 3D curve.

    **Properties**
    - `curvePoints` - The 3D curve to sweep along, as JSON. Without it there is no path and no mesh is built.
    - `radius` - The radius of the tube cross-section.
    - `tubularSegments` - The number of segments along the path.
    - `radialSegments` - The number of segments around the cross-section.
    */
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BSObjectId))]
    [WatchComponent]
    public class BSTube : BSComponentBase
    {
        [Tooltip("The 3D curve to sweep along, as JSON. Without it there is no path and no mesh is built.")]
        [See(initial = "")][SerializeField] internal string curvePoints = "";
        [Tooltip("The radius of the tube cross-section.")]
        [See(initial = "0.5")][SerializeField] internal float radius = 0.5f;
        [Tooltip("The number of segments along the path.")]
        [See(initial = "32")][SerializeField] internal int tubularSegments = 32;
        [Tooltip("The number of segments around the cross-section.")]
        [See(initial = "32")][SerializeField] internal int radialSegments = 32;
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
            geometry.geometryType = GeometryType.TubeGeometry;
            geometry.curvePoints = curvePoints;
            geometry.radius = radius;
            geometry.tubularSegments = tubularSegments;
            geometry.radialSegments = radialSegments;
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
        public System.String CurvePoints { get { return curvePoints; } set { curvePoints = value; UpdateCallback(new List<PropertyName> { PropertyName.curvePoints }); } }
        public System.Single Radius { get { return radius; } set { radius = value; UpdateCallback(new List<PropertyName> { PropertyName.radius }); } }
        public System.Int32 TubularSegments { get { return tubularSegments; } set { tubularSegments = value; UpdateCallback(new List<PropertyName> { PropertyName.tubularSegments }); } }
        public System.Int32 RadialSegments { get { return radialSegments; } set { radialSegments = value; UpdateCallback(new List<PropertyName> { PropertyName.radialSegments }); } }

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
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.curvePoints, PropertyName.radius, PropertyName.tubularSegments, PropertyName.radialSegments, };
            UpdateCallback(changedProperties);
        }
        internal override string GetSignature()
        {
            return "Tube" +  PropertyName.curvePoints + curvePoints + PropertyName.radius + radius + PropertyName.tubularSegments + tubularSegments + PropertyName.radialSegments + radialSegments;
        }

        internal override void Init(List<object> constructorProperties = null)
        {
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(gameObject.GetInstanceID(), GetInstanceID(), ComponentType.Tube);


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
                if (values[i] is BSString)
                {
                    var valcurvePoints = (BSString)values[i];
                    if (valcurvePoints.n == PropertyName.curvePoints)
                    {
                        curvePoints = valcurvePoints.x;
                        changedProperties.Add(PropertyName.curvePoints);
                    }
                }
                if (values[i] is BSFloat)
                {
                    var valradius = (BSFloat)values[i];
                    if (valradius.n == PropertyName.radius)
                    {
                        radius = valradius.x;
                        changedProperties.Add(PropertyName.radius);
                    }
                }
                if (values[i] is BSInt)
                {
                    var valtubularSegments = (BSInt)values[i];
                    if (valtubularSegments.n == PropertyName.tubularSegments)
                    {
                        tubularSegments = valtubularSegments.x;
                        changedProperties.Add(PropertyName.tubularSegments);
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
                    name = PropertyName.curvePoints,
                    type = PropertyType.String,
                    value = curvePoints,
                    componentType = ComponentType.Tube,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.radius,
                    type = PropertyType.Float,
                    value = radius,
                    componentType = ComponentType.Tube,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.tubularSegments,
                    type = PropertyType.Int,
                    value = tubularSegments,
                    componentType = ComponentType.Tube,
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
                    componentType = ComponentType.Tube,
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