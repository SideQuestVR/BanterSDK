using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BS
{
    /*
    #### Banter Shape
    A filled flat polygon built from a 2D outline, with optional holes.

    **Properties**
    - `shapePoints` - The 2D outline and holes, as JSON. Without it there is no shape and no mesh is built.
    - `segments` - How finely curves in the outline are sampled.
    */
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BSObjectId))]
    [WatchComponent]
    public class BSShape : BSComponentBase
    {
        [Tooltip("The 2D outline and holes, as JSON. Without it there is no shape and no mesh is built.")]
        [See(initial = "")][SerializeField] internal string shapePoints = "";
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
            geometry.geometryType = GeometryType.ShapeGeometry;
            geometry.shapePoints = shapePoints;
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
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.shapePoints, PropertyName.segments, };
            UpdateCallback(changedProperties);
        }
        internal override string GetSignature()
        {
            return "Shape" +  PropertyName.shapePoints + shapePoints + PropertyName.segments + segments;
        }

        internal override void Init(List<object> constructorProperties = null)
        {
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(gameObject.GetInstanceID(), GetInstanceID(), ComponentType.Shape);


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
                    componentType = ComponentType.Shape,
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
                    componentType = ComponentType.Shape,
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