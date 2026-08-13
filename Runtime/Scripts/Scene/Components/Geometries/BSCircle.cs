using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Banter.SDK
{
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BSObjectId))]
    [WatchComponent]
    public class BSCircle : BSComponentBase
    {



        [Tooltip("The radius of the circle")]
        [See(initial = "1")][SerializeField] internal float radius = 1;
        [Tooltip("The number of segments that make up the circle")]
        [See(initial = "16")][SerializeField] internal int segments = 16;
        [Tooltip("The starting angle of the circle in radians")]
        [See(initial = "0")][SerializeField] internal float thetaStart = 0;
        [Tooltip("The length of the angle of the circle in radians")]
        [See(initial = "Math.PI * 2")][SerializeField] internal float thetaLength = Mathf.PI * 2;
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
            geometry.geometryType = GeometryType.CircleGeometry;
            geometry.radius = radius;
            geometry.segments = segments;
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

         }
        internal void UpdateCallback(List<PropertyName> changedProperties)
        {
            SetupGeometry();
        }
        // BANTER COMPILED CODE 
        public System.Single Radius { get { return radius; } set { radius = value; UpdateCallback(new List<PropertyName> { PropertyName.radius }); } }
        public System.Int32 Segments { get { return segments; } set { segments = value; UpdateCallback(new List<PropertyName> { PropertyName.segments }); } }
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
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.radius, PropertyName.segments, PropertyName.thetaStart, PropertyName.thetaLength, };
            UpdateCallback(changedProperties);
        }
        internal override string GetSignature()
        {
            return "Circle" +  PropertyName.radius + radius + PropertyName.segments + segments + PropertyName.thetaStart + thetaStart + PropertyName.thetaLength + thetaLength;
        }

        internal override void Init(List<object> constructorProperties = null)
        {
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(gameObject.GetInstanceID(), GetInstanceID(), ComponentType.Circle);


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
                if (values[i] is BSInt)
                {
                    var valsegments = (BSInt)values[i];
                    if (valsegments.n == PropertyName.segments)
                    {
                        segments = valsegments.x;
                        changedProperties.Add(PropertyName.segments);
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
                    name = PropertyName.radius,
                    type = PropertyType.Float,
                    value = radius,
                    componentType = ComponentType.Circle,
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
                    componentType = ComponentType.Circle,
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
                    componentType = ComponentType.Circle,
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
                    componentType = ComponentType.Circle,
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