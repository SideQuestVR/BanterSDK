using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Banter.SDK
{
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BSObjectId))]
    [WatchComponent]
    public class BSSphere : BSComponentBase
    {
        [Tooltip("Radius of the sphere")]
        [See(initial = "1")][SerializeField] internal float radius;
        [Tooltip("Number of horizontal segments")]
        [See(initial = "16")][SerializeField] internal int widthSegments;
        [Tooltip("Number of vertical segments")]
        [See(initial = "16")][SerializeField] internal int heightSegments;
        [Tooltip("Start angle in radians for the horizontal segments")]
        [See(initial = "0")][SerializeField] internal float phiStart;
        [Tooltip("Length of the horizontal segments in radians")]
        [See(initial = "Math.PI * 2")][SerializeField] internal float phiLength = Mathf.PI * 2;
        [Tooltip("Start angle in radians for the vertical segments")]
        [See(initial = "0")][SerializeField] internal float thetaStart;
        [Tooltip("Length of the vertical segments in radians")]
        [See(initial = "Math.PI")][SerializeField] internal float thetaLength = Mathf.PI;

        internal override void StartStuff()
        {
            SetupGeometry();
            SetLoadedIfNot();
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
            geometry.geometryType = GeometryType.SphereGeometry;
            geometry.radius = radius;
            geometry.widthSegments = widthSegments;
            geometry.heightSegments = heightSegments;
            geometry.phiStart = phiStart;
            geometry.phiLength = phiLength;
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

        internal override void UpdateStuff()
        {
            
        }
        internal void UpdateCallback(List<PropertyName> changedProperties)
        {
            SetupGeometry();
        }
        // BANTER COMPILED CODE 
        public System.Single Radius { get { return radius; } set { radius = value; UpdateCallback(new List<PropertyName> { PropertyName.radius }); } }
        public System.Int32 WidthSegments { get { return widthSegments; } set { widthSegments = value; UpdateCallback(new List<PropertyName> { PropertyName.widthSegments }); } }
        public System.Int32 HeightSegments { get { return heightSegments; } set { heightSegments = value; UpdateCallback(new List<PropertyName> { PropertyName.heightSegments }); } }
        public System.Single PhiStart { get { return phiStart; } set { phiStart = value; UpdateCallback(new List<PropertyName> { PropertyName.phiStart }); } }
        public System.Single PhiLength { get { return phiLength; } set { phiLength = value; UpdateCallback(new List<PropertyName> { PropertyName.phiLength }); } }
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
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.radius, PropertyName.widthSegments, PropertyName.heightSegments, PropertyName.phiStart, PropertyName.phiLength, PropertyName.thetaStart, PropertyName.thetaLength, };
            UpdateCallback(changedProperties);
        }
        internal override string GetSignature()
        {
            return "Sphere" +  PropertyName.radius + radius + PropertyName.widthSegments + widthSegments + PropertyName.heightSegments + heightSegments + PropertyName.phiStart + phiStart + PropertyName.phiLength + phiLength + PropertyName.thetaStart + thetaStart + PropertyName.thetaLength + thetaLength;
        }

        internal override void Init(List<object> constructorProperties = null)
        {
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(gameObject.GetInstanceID(), GetInstanceID(), ComponentType.Sphere);


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
                    componentType = ComponentType.Sphere,
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
                    componentType = ComponentType.Sphere,
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
                    componentType = ComponentType.Sphere,
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
                    componentType = ComponentType.Sphere,
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
                    componentType = ComponentType.Sphere,
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
                    componentType = ComponentType.Sphere,
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
                    componentType = ComponentType.Sphere,
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