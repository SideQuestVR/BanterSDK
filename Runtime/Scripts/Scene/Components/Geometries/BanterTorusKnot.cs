using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Banter.SDK
{
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BanterObjectId))]
    [WatchComponent]
    public class BanterTorusKnot : BanterComponentBase
    {
        [Tooltip("Radius of the inner circle")]
        [See(initial = "0.5")][SerializeField] internal float radius = 0.5f;
        [Tooltip("How tubular it is.")]
        [See(initial = "0.4")][SerializeField] internal float tube = 0.4f;

        [Tooltip("Number of radial segments")]
        [See(initial = "8")][SerializeField] internal int radialSegments = 8;
        [Tooltip("Number of tubular segments")]
        [See(initial = "64")][SerializeField] internal int tubularSegments = 64;
        [Tooltip("The number of p segments to divide the shape into.")]
        [See(initial = "2")][SerializeField] internal int p = 2;
        [Tooltip("The number of q segments to divide the shape into.")]
        [See(initial = "3")][SerializeField] internal int q = 3;


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
            geometry.geometryType = GeometryType.TorusKnotGeometry;
            geometry.tube = tube;
            geometry.radius = radius;
            geometry.radialSegments = radialSegments;
            geometry.tubularSegments = tubularSegments;
            geometry.p = p;
            geometry.q = q;

            Debug.Log(tube + " - " + radius + " - " + radialSegments + " - " + tubularSegments + " - " + p + " - " + q);

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
        public System.Int32 P { get { return p; } set { p = value; UpdateCallback(new List<PropertyName> { PropertyName.p }); } }
        public System.Int32 Q { get { return q; } set { q = value; UpdateCallback(new List<PropertyName> { PropertyName.q }); } }

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
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.radius, PropertyName.tube, PropertyName.radialSegments, PropertyName.tubularSegments, PropertyName.p, PropertyName.q, };
            UpdateCallback(changedProperties);
        }

        internal override void Init(List<object> constructorProperties = null)
        {
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(gameObject.GetInstanceID(), GetInstanceID(), ComponentType.BanterTorusKnot);


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
                if (values[i] is BanterInt)
                {
                    var valp = (BanterInt)values[i];
                    if (valp.n == PropertyName.p)
                    {
                        p = valp.x;
                        changedProperties.Add(PropertyName.p);
                    }
                }
                if (values[i] is BanterInt)
                {
                    var valq = (BanterInt)values[i];
                    if (valq.n == PropertyName.q)
                    {
                        q = valq.x;
                        changedProperties.Add(PropertyName.q);
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
                    componentType = ComponentType.BanterTorusKnot,
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
                    componentType = ComponentType.BanterTorusKnot,
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
                    componentType = ComponentType.BanterTorusKnot,
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
                    componentType = ComponentType.BanterTorusKnot,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.p,
                    type = PropertyType.Int,
                    value = p,
                    componentType = ComponentType.BanterTorusKnot,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.q,
                    type = PropertyType.Int,
                    value = q,
                    componentType = ComponentType.BanterTorusKnot,
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