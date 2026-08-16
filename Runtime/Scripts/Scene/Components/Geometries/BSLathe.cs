using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BS
{
    /*
    #### Banter Lathe
    A surface of revolution: a 2D profile swept around the Y axis.

    **Properties**
    - `shapePoints` - The 2D half-profile to revolve, as JSON. Without it there is no shape and no mesh is built.
    - `segments` - How finely the profile itself is sampled.
    - `radialSegments` - The number of segments around the axis of revolution.
    - `phiStart` - The starting angle of the revolution.
    - `phiLength` - The swept angle. Less than a full turn leaves the solid open.
    */
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BSObjectId))]
    [WatchComponent]
    public class BSLathe : BSComponentBase
    {
        [Tooltip("The 2D half-profile to revolve, as JSON. Without it there is no shape and no mesh is built.")]
        [See(initial = "")][SerializeField] internal string shapePoints = "";
        [Tooltip("How finely the profile itself is sampled.")]
        [See(initial = "32")][SerializeField] internal int segments = 32;
        [Tooltip("The number of segments around the axis of revolution.")]
        [See(initial = "32")][SerializeField] internal int radialSegments = 32;
        [Tooltip("The starting angle of the revolution.")]
        [See(initial = "0")][SerializeField] internal float phiStart = 0f;
        [Tooltip("The swept angle. Less than a full turn leaves the solid open.")]
        [See(initial = "6.283185")][SerializeField] internal float phiLength = Mathf.PI * 2f;
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
            geometry.geometryType = GeometryType.LatheGeometry;
            geometry.shapePoints = shapePoints;
            geometry.segments = segments;
            geometry.radialSegments = radialSegments;
            geometry.phiStart = phiStart;
            geometry.phiLength = phiLength;
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
        public System.Int32 Segments { get { return segments; } set { segments = value; UpdateCallback(new List<PropertyName> { PropertyName.segments }); } }
        public System.Int32 RadialSegments { get { return radialSegments; } set { radialSegments = value; UpdateCallback(new List<PropertyName> { PropertyName.radialSegments }); } }
        public System.Single PhiStart { get { return phiStart; } set { phiStart = value; UpdateCallback(new List<PropertyName> { PropertyName.phiStart }); } }
        public System.Single PhiLength { get { return phiLength; } set { phiLength = value; UpdateCallback(new List<PropertyName> { PropertyName.phiLength }); } }

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
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.shapePoints, PropertyName.segments, PropertyName.radialSegments, PropertyName.phiStart, PropertyName.phiLength, };
            UpdateCallback(changedProperties);
        }
        internal override string GetSignature()
        {
            return "Lathe" + PropertyName.shapePoints + shapePoints + PropertyName.segments + segments + PropertyName.radialSegments + radialSegments + PropertyName.phiStart + phiStart + PropertyName.phiLength + phiLength;
        }

        internal override void Init(List<object> constructorProperties = null)
        {
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(gameObject.GetInstanceID(), GetInstanceID(), ComponentType.Lathe);


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
                if (values[i] is BSInt)
                {
                    var valsegments = (BSInt)values[i];
                    if (valsegments.n == PropertyName.segments)
                    {
                        segments = valsegments.x;
                        changedProperties.Add(PropertyName.segments);
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
                if (values[i] is BSFloat)
                {
                    var valphiStart = (BSFloat)values[i];
                    if (valphiStart.n == PropertyName.phiStart)
                    {
                        phiStart = valphiStart.x;
                        changedProperties.Add(PropertyName.phiStart);
                    }
                }
                if (values[i] is BSFloat)
                {
                    var valphiLength = (BSFloat)values[i];
                    if (valphiLength.n == PropertyName.phiLength)
                    {
                        phiLength = valphiLength.x;
                        changedProperties.Add(PropertyName.phiLength);
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
                    componentType = ComponentType.Lathe,
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
                    componentType = ComponentType.Lathe,
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
                    componentType = ComponentType.Lathe,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.phiStart,
                    type = PropertyType.Float,
                    value = phiStart,
                    componentType = ComponentType.Lathe,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.phiLength,
                    type = PropertyType.Float,
                    value = phiLength,
                    componentType = ComponentType.Lathe,
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
