using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BS
{
    /*
    #### Banter Extrude
    A 2D outline given thickness, optionally along a curve.

    **Properties**
    - `shapePoints` - The 2D outline and holes, as JSON. Without it there is no shape and no mesh is built.
    - `curvePoints` - An optional 3D path to extrude along. Empty extrudes straight along Z.
    - `depth` - How far to extrude when there is no extrude path.
    - `depthSegments` - Subdivisions along the extrusion axis (three.js calls this steps).
    - `segments` - How finely curves in the outline are sampled.
    */
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BSObjectId))]
    [WatchComponent]
    public class BSExtrude : BSComponentBase
    {
        [Tooltip("The 2D outline and holes, as JSON. Without it there is no shape and no mesh is built.")]
        [See(initial = "")][SerializeField] internal string shapePoints = "";
        [Tooltip("An optional 3D path to extrude along. Empty extrudes straight along Z.")]
        [See(initial = "")][SerializeField] internal string curvePoints = "";
        [Tooltip("How far to extrude when there is no extrude path.")]
        [See(initial = "1")][SerializeField] internal float depth = 1f;
        [Tooltip("Subdivisions along the extrusion axis (three.js calls this steps).")]
        [See(initial = "1")][SerializeField] internal int depthSegments = 1;
        [Tooltip("How finely curves in the outline are sampled.")]
        [See(initial = "32")][SerializeField] internal int segments = 32;
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
            geometry.geometryType = GeometryType.ExtrudeGeometry;
            geometry.shapePoints = shapePoints;
            geometry.curvePoints = curvePoints;
            geometry.depth = depth;
            geometry.depthSegments = depthSegments;
            geometry.segments = segments;
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
        public System.String ShapePoints { get { return shapePoints; } set { shapePoints = value; UpdateCallback(new List<PropertyName> { PropertyName.shapePoints }); } }
        public System.String CurvePoints { get { return curvePoints; } set { curvePoints = value; UpdateCallback(new List<PropertyName> { PropertyName.curvePoints }); } }
        public System.Single Depth { get { return depth; } set { depth = value; UpdateCallback(new List<PropertyName> { PropertyName.depth }); } }
        public System.Int32 DepthSegments { get { return depthSegments; } set { depthSegments = value; UpdateCallback(new List<PropertyName> { PropertyName.depthSegments }); } }
        public System.Int32 Segments { get { return segments; } set { segments = value; UpdateCallback(new List<PropertyName> { PropertyName.segments }); } }

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
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.shapePoints, PropertyName.curvePoints, PropertyName.depth, PropertyName.depthSegments, PropertyName.segments, };
            UpdateCallback(changedProperties);
        }
        internal override string GetSignature()
        {
            return "Extrude" +  PropertyName.shapePoints + shapePoints + PropertyName.curvePoints + curvePoints + PropertyName.depth + depth + PropertyName.depthSegments + depthSegments + PropertyName.segments + segments;
        }

        internal override void Init(List<object> constructorProperties = null)
        {
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(gameObject.GetInstanceID(), GetInstanceID(), ComponentType.Extrude);


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
                    var valshapePoints = (BSString)values[i];
                    if (valshapePoints.n == PropertyName.shapePoints)
                    {
                        shapePoints = valshapePoints.x;
                        changedProperties.Add(PropertyName.shapePoints);
                    }
                }
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
                    var valdepth = (BSFloat)values[i];
                    if (valdepth.n == PropertyName.depth)
                    {
                        depth = valdepth.x;
                        changedProperties.Add(PropertyName.depth);
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
                if (values[i] is BSInt)
                {
                    var valsegments = (BSInt)values[i];
                    if (valsegments.n == PropertyName.segments)
                    {
                        segments = valsegments.x;
                        changedProperties.Add(PropertyName.segments);
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
                    name = PropertyName.shapePoints,
                    type = PropertyType.String,
                    value = shapePoints,
                    componentType = ComponentType.Extrude,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.curvePoints,
                    type = PropertyType.String,
                    value = curvePoints,
                    componentType = ComponentType.Extrude,
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
                    componentType = ComponentType.Extrude,
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
                    componentType = ComponentType.Extrude,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.segments,
                    type = PropertyType.Int,
                    value = segments,
                    componentType = ComponentType.Extrude,
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