using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Banter.SDK
{
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BanterObjectId))]
    [WatchComponent]
    public class BanterRing : BanterComponentBase
    {
        [Tooltip("Radius of the inner circle")]
        [See(initial = "1")][SerializeField] internal float innerRadius;
        [Tooltip("Radius of the outer circle")]
        [See(initial = "2")][SerializeField] internal float outerRadius;
        [Tooltip("Number of segments in the theta direction")]
        [See(initial = "32")][SerializeField] internal int thetaSegments;
        [Tooltip("Number of segments in the phi direction")]
        [See(initial = "1")][SerializeField] internal int phiSegments;
        [Tooltip("Start angle in radians")]
        [See(initial = "0")][SerializeField] internal float thetaStart;
        [Tooltip("Angle in radians")]
        [See(initial = "Math.PI * 2")][SerializeField] internal float thetaLength = Mathf.PI * 2;
        

        
        
        internal override void StartStuff()
        {
            SetupGeometry();
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
            geometry.geometryType = GeometryType.RingGeometry;
            geometry.innerRadius = innerRadius;
            geometry.outerRadius = outerRadius;
            geometry.thetaSegments = thetaSegments;
            geometry.phiSegments = phiSegments;
            geometry.thetaStart = thetaStart;
            geometry.thetaLength = thetaLength;
            if (shouldSetGeometry)
            {
                geometry.SetGeometry();
            }
            var material = GetComponent<BanterMaterial>();
            if (material == null)
            {
                gameObject.AddComponent<BanterMaterial>();
            }
        }

        internal override void DestroyStuff() { }
        internal void UpdateCallback(List<PropertyName> changedProperties)
        {
            SetupGeometry();
        }
        // BANTER COMPILED CODE 
        public System.Single InnerRadius { get { return innerRadius; } set { innerRadius = value; UpdateCallback(new List<PropertyName> { PropertyName.innerRadius }); } }
        public System.Single OuterRadius { get { return outerRadius; } set { outerRadius = value; UpdateCallback(new List<PropertyName> { PropertyName.outerRadius }); } }
        public System.Int32 ThetaSegments { get { return thetaSegments; } set { thetaSegments = value; UpdateCallback(new List<PropertyName> { PropertyName.thetaSegments }); } }
        public System.Int32 PhiSegments { get { return phiSegments; } set { phiSegments = value; UpdateCallback(new List<PropertyName> { PropertyName.phiSegments }); } }
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
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.innerRadius, PropertyName.outerRadius, PropertyName.thetaSegments, PropertyName.phiSegments, PropertyName.thetaStart, PropertyName.thetaLength, };
            UpdateCallback(changedProperties);
        }

        internal override void Init(List<object> constructorProperties = null)
        {
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(gameObject.GetInstanceID(), GetInstanceID(), ComponentType.BanterRing);


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
                    var valinnerRadius = (BanterFloat)values[i];
                    if (valinnerRadius.n == PropertyName.innerRadius)
                    {
                        innerRadius = valinnerRadius.x;
                        changedProperties.Add(PropertyName.innerRadius);
                    }
                }
                if (values[i] is BanterFloat)
                {
                    var valouterRadius = (BanterFloat)values[i];
                    if (valouterRadius.n == PropertyName.outerRadius)
                    {
                        outerRadius = valouterRadius.x;
                        changedProperties.Add(PropertyName.outerRadius);
                    }
                }
                if (values[i] is BanterInt)
                {
                    var valthetaSegments = (BanterInt)values[i];
                    if (valthetaSegments.n == PropertyName.thetaSegments)
                    {
                        thetaSegments = valthetaSegments.x;
                        changedProperties.Add(PropertyName.thetaSegments);
                    }
                }
                if (values[i] is BanterInt)
                {
                    var valphiSegments = (BanterInt)values[i];
                    if (valphiSegments.n == PropertyName.phiSegments)
                    {
                        phiSegments = valphiSegments.x;
                        changedProperties.Add(PropertyName.phiSegments);
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
                    name = PropertyName.innerRadius,
                    type = PropertyType.Float,
                    value = innerRadius,
                    componentType = ComponentType.BanterRing,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.outerRadius,
                    type = PropertyType.Float,
                    value = outerRadius,
                    componentType = ComponentType.BanterRing,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.thetaSegments,
                    type = PropertyType.Int,
                    value = thetaSegments,
                    componentType = ComponentType.BanterRing,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.phiSegments,
                    type = PropertyType.Int,
                    value = phiSegments,
                    componentType = ComponentType.BanterRing,
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
                    componentType = ComponentType.BanterRing,
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
                    componentType = ComponentType.BanterRing,
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