using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Banter.SDK
{
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BanterObjectId))]
    [WatchComponent]
    public class BanterTorus : BanterComponentBase
    {
        [Tooltip("Radius of the inner circle")]
        [See(initial = "1")][SerializeField] internal float radius;
        [Tooltip("How tubular it is.")]
        [See(initial = "1")][SerializeField] internal float tube;

        [Tooltip("Number of radial segments")]
        [See(initial = "8")][SerializeField] internal int radialSegments;
        [Tooltip("Number of tubular segments")]
        [See(initial = "6")][SerializeField] internal int tubularSegments;
        [Tooltip("Arc length of the ring in radians")]
        [See(initial = "Math.PI * 2")][SerializeField] internal float arc;



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
            geometry.geometryType = GeometryType.TorusGeometry;
            geometry.tube = tube;
            geometry.radius = radius;
            geometry.radialSegments = radialSegments;
            geometry.tubularSegments = tubularSegments;
            geometry.arc = arc;

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

         }
        internal void UpdateCallback(List<PropertyName> changedProperties)
        {
            SetupGeometry();
        }
        // BANTER COMPILED CODE 
        public System.Single Radius { get { return radius; } set { radius = value; UpdateCallback(new List<PropertyName> { PropertyName.radius }); } }
        public System.Single Tube { get { return tube; } set { tube = value; UpdateCallback(new List<PropertyName> { PropertyName.tube }); } }
        public System.Int32 RadialSegments { get { return radialSegments; } set { radialSegments = value; UpdateCallback(new List<PropertyName> { PropertyName.radialSegments }); } }
        public System.Int32 TubularSegments { get { return tubularSegments; } set { tubularSegments = value; UpdateCallback(new List<PropertyName> { PropertyName.tubularSegments }); } }
        public System.Single Arc { get { return arc; } set { arc = value; UpdateCallback(new List<PropertyName> { PropertyName.arc }); } }

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
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.radius, PropertyName.tube, PropertyName.radialSegments, PropertyName.tubularSegments, PropertyName.arc, };
            UpdateCallback(changedProperties);
        }

        internal override void Init(List<object> constructorProperties = null)
        {
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(gameObject.GetInstanceID(), GetInstanceID(), ComponentType.BanterTorus);


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
                    var valradius = (BanterFloat)values[i];
                    if (valradius.n == PropertyName.radius)
                    {
                        radius = valradius.x;
                        changedProperties.Add(PropertyName.radius);
                    }
                }
                if (values[i] is BanterFloat)
                {
                    var valtube = (BanterFloat)values[i];
                    if (valtube.n == PropertyName.tube)
                    {
                        tube = valtube.x;
                        changedProperties.Add(PropertyName.tube);
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
                    var valtubularSegments = (BanterInt)values[i];
                    if (valtubularSegments.n == PropertyName.tubularSegments)
                    {
                        tubularSegments = valtubularSegments.x;
                        changedProperties.Add(PropertyName.tubularSegments);
                    }
                }
                if (values[i] is BanterFloat)
                {
                    var valarc = (BanterFloat)values[i];
                    if (valarc.n == PropertyName.arc)
                    {
                        arc = valarc.x;
                        changedProperties.Add(PropertyName.arc);
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
                    name = PropertyName.radius,
                    type = PropertyType.Float,
                    value = radius,
                    componentType = ComponentType.BanterTorus,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.tube,
                    type = PropertyType.Float,
                    value = tube,
                    componentType = ComponentType.BanterTorus,
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
                    componentType = ComponentType.BanterTorus,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.tubularSegments,
                    type = PropertyType.Int,
                    value = tubularSegments,
                    componentType = ComponentType.BanterTorus,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.arc,
                    type = PropertyType.Float,
                    value = arc,
                    componentType = ComponentType.BanterTorus,
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