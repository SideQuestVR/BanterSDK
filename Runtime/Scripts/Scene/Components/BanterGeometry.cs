using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Banter.SDK
{

    [System.Serializable]
    public class ParametricPoints
    {
        public Vector3[] points;
    }

    /* 
    #### Banter Geometry
    Add a geometry shape to the object. This can be a box, circle, cone, cylinder, plane, ring, sphere, torus, torus knot, or a custom parametric shape.

    **Properties**
    - `geometryType` - The type of primitive shape to generate.
    - `parametricType` - The type of parametric shape to generate if using ParametricGeometry.
    - `width` - The width of the shape.
    - `height` - The height of the shape.
    - `depth` - The depth of the shape.
    - `widthSegments` - The number of width segments to divide the shape into.
    - `heightSegments` - The number of height segments to divide the shape into.
    - `depthSegments` - The number of depth segments to divide the shape into.
    - `radius` - The radius of the shape.
    - `segments` - The number of segments to divide the shape into.
    - `thetaStart` - The starting x angle of the shape.
    - `thetaLength` - The ending x angle of the shape.
    - `phiStart` - The starting y angle of the shape.
    - `phiLength` - The ending y angle of the shape.
    - `radialSegments` - The number of radial segments to divide the shape into.
    - `openEnded` - Whether the object is open on the end or not.
    - `radiusTop` - The radius of the top of the shape.
    - `radiusBottom` - The radius of the bottom of the shape.
    - `innerRadius` - The inner radius of the shape.
    - `outerRadius` - The outer radius of the shape.
    - `thetaSegments` - The number of angular x segments to divide the shape into.
    - `phiSegments` - The number of angular y segments to divide the shape into.
    - `tube` - The tube size.
    - `tubularSegments` - The number of tubular segments to divide the tube into.
    - `arc` - The arc of the shape.
    - `p` - The number of p segments to divide the shape into.
    - `q` - The number of q segments to divide the shape into.
    - `stacks` - The number of stacks to divide the shape into.
    - `slices` - The number of slices to divide the shape into.
    - `detail` - The detail of the shape.
    - `parametricPoints` - The points of the parametric shape.

    **Code Example**
    ```js
        const geometryType = BS.GeometryType.BOxGeometry;
        const parametricType = null;
        const width = 1;
        const height = 1;
        const depth = 1;
        const widthSegments = 1;
        const heightSegments = 1;
        const depthSegments = 1;
        const radius = 1;
        const segments = 24;
        const thetaStart = 0;
        const thetaLength = 6.283185;
        const phiStart = 0;
        const phiLength = 6.283185;
        const radialSegments = 8;
        const openEnded = false;
        const radiusTop = 1;
        const radiusBottom = 1;
        const innerRadius = 0.3;
        const outerRadius = 1;
        const thetaSegments = 24;
        const phiSegments = 8;
        const tube = 0.4;
        const tubularSegments = 16;
        const arc = 6.283185;
        const p = 2;
        const q = 3;
        const stacks = 5;
        const slices = 5;
        const detail = 0;
        const parametricPoints = "";
        const gameObject = new BS.GameObject("MyGeometry");
        const geometry = await gameObject.AddComponent(new BS.BanterGeometry(geometryType, parametricType, width, height, depth, widthSegments, heightSegments, depthSegments, radius, segments, thetaStart, thetaLength, phiStart, phiLength, radialSegments, openEnded, radiusTop, radiusBottom, innerRadius, outerRadius, thetaSegments, phiSegments, tube, tubularSegments, arc, p, q, stacks, slices, detail, parametricPoints));
    ```
    */
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BanterObjectId))]
    [WatchComponent]
    public class BanterGeometry : BanterComponentBase
    {
        [Tooltip("The type of primitive shape to generate.")]
        [See(initial = "0")][SerializeField] internal GeometryType geometryType;
        [Tooltip("The type of parametric shape to generate if using ParametricGeometry.")]
        [See(initial = "0")][SerializeField] internal ParametricGeometryType parametricType;
        [Tooltip("The width of the shape.")]
        [See(initial = "1")][SerializeField] internal float width = 1;
        [Tooltip("The height of the shape.")]
        [See(initial = "1")][SerializeField] internal float height = 1;
        [Tooltip("The depth of the shape.")]
        [See(initial = "1")][SerializeField] internal float depth = 1;
        [Tooltip("The number of width segments to divide the shape into.")]
        [See(initial = "1")][SerializeField] internal int widthSegments = 1;
        [Tooltip("The number of height segments to divide the shape into.")]
        [See(initial = "1")][SerializeField] internal int heightSegments = 1;
        [Tooltip("The number of depth segments to divide the shape into.")]
        [See(initial = "1")][SerializeField] internal int depthSegments = 1;

        [Tooltip("The radius of the shape.")]
        [See(initial = "1")][SerializeField] internal float radius = 1;
        [Tooltip("The number of segments to divide the shape into.")]
        [See(initial = "24")][SerializeField] internal int segments = 24;
        [Tooltip("The starting x angle of the shape.")]
        [See(initial = "0")][SerializeField] internal float thetaStart = 0;
        [Tooltip("The ending x angle of the shape.")]
        [See(initial = "6.283185")][SerializeField] internal float thetaLength = Mathf.PI * 2f;

        [Tooltip("The starting y angle of the shape.")]
        [See(initial = "0")][SerializeField] internal float phiStart = 0;
        [Tooltip("The ending y angle of the shape.")]
        [See(initial = "6.283185")][SerializeField] internal float phiLength = Mathf.PI * 2f;

        [Tooltip("The number of radial segments to divide the shape into.")]
        [See(initial = "8")][SerializeField] internal int radialSegments = 8;
        [Tooltip("Whether the object is open on the end or not.")]
        [See(initial = "false")][SerializeField] internal bool openEnded = false;

        [Tooltip("The radius of the top of the shape.")]
        [See(initial = "1")][SerializeField] internal float radiusTop = 1;
        [Tooltip("The radius of the bottom of the shape.")]
        [See(initial = "1")][SerializeField] internal float radiusBottom = 1;

        [Tooltip("The inner radius of the shape.")]
        [See(initial = "0.3")][SerializeField] internal float innerRadius = 0.3f;
        [Tooltip("The outer radius of the shape.")]
        [See(initial = "1")][SerializeField] internal float outerRadius = 1f;
        [Tooltip("The number of angular x segments to divide the shape into.")]
        [See(initial = "24")][SerializeField] internal int thetaSegments = 24;
        [Tooltip("The number of angular y segments to divide the shape into.")]
        [See(initial = "8")][SerializeField] internal int phiSegments = 8;
        [Tooltip("The tube size.")]
        [See(initial = "0.4")][SerializeField] internal float tube = 0.4f;
        [Tooltip("The number of tubular segments to divide the tube into.")]
        [See(initial = "16")][SerializeField] internal int tubularSegments = 16;
        [Tooltip("The arc of the shape.")]
        [See(initial = "6.283185")][SerializeField] internal float arc = Mathf.PI * 2;
        [Tooltip("The number of p segments to divide the shape into.")]
        [See(initial = "2")][SerializeField] internal int p = 2;
        [Tooltip("The number of q segments to divide the shape into.")]
        [See(initial = "3")][SerializeField] internal int q = 3;
        [Tooltip("The number of stacks to divide the shape into.")]
        [See(initial = "5")][SerializeField] internal int stacks = 5;
        [Tooltip("The number of slices to divide the shape into.")]
        [See(initial = "5")][SerializeField] internal int slices = 5;
        [See(initial = "0")][SerializeField] internal float detail = 0;
        [See(initial = "")][SerializeField] internal string parametricPoints = "";
        MeshFilter _filter;
        private static Dictionary<string, Mesh> geometryCache = new Dictionary<string, Mesh>();
        public static void ClearCache()
        {
            foreach (var geo in geometryCache)
            {
                Destroy(geo.Value);
            }
            geometryCache.Clear();
        }
        
        private Mesh GetCachedMesh()
        {
            var signature = GetSignature();
            if(geometryCache.ContainsKey(signature))
            {
                return geometryCache[signature];
            }
            else
            {
                Mesh mesh = null;
                switch (geometryType)
                {
                    case GeometryType.BoxGeometry:
                        mesh = new Box(width, height, depth, widthSegments, heightSegments, depthSegments).Generate();
                        break;
                    case GeometryType.CircleGeometry:
                        mesh = new Circle(radius, segments, thetaStart, thetaLength).Generate();
                        break;
                    case GeometryType.ConeGeometry:
                        mesh = new Cylinder(radiusTop, radiusBottom, height, radialSegments, heightSegments, openEnded, thetaStart, thetaLength).Generate();
                        break;
                    case GeometryType.CylinderGeometry:
                        mesh = new Cylinder(radiusTop, radiusBottom, height, radialSegments, heightSegments, openEnded, thetaStart, thetaLength).Generate();
                        break;
                    case GeometryType.PlaneGeometry:
                        mesh = new Plane(width, height, widthSegments, heightSegments).Generate();
                        break;
                    case GeometryType.RingGeometry:
                        mesh = new Ring(innerRadius, outerRadius, thetaSegments, phiSegments, thetaStart, thetaLength).Generate();
                        break;
                    case GeometryType.SphereGeometry:
                        mesh = new Sphere(radius, widthSegments, heightSegments, phiStart, phiLength, thetaStart, thetaLength).Generate();
                        break;
                    case GeometryType.TorusGeometry:
                        mesh = new Torus(radius, tube, radialSegments, tubularSegments, arc).Generate();
                        break;
                    case GeometryType.TorusKnotGeometry:
                        mesh = new TorusKnot(radius, tube, radialSegments, tubularSegments, p, q).Generate();
                        break;
                    case GeometryType.ParametricGeometry:
                        switch (parametricType)
                        {
                            case ParametricGeometryType.Klein:
                                mesh = new ParametricGeometry(stacks, slices, ParametricGeometry.Klein).Generate();
                                break;
                            case ParametricGeometryType.Apple:
                                mesh = new ParametricGeometry(stacks, slices, ParametricGeometry.Apple).Generate();
                                break;
                            case ParametricGeometryType.Fermet:
                                mesh = new ParametricGeometry(stacks, slices, ParametricGeometry.Fermet).Generate();
                                break;
                            case ParametricGeometryType.Catenoid:
                                mesh = new ParametricGeometry(stacks, slices, ParametricGeometry.Catenoid).Generate();
                                break;
                            case ParametricGeometryType.Helicoid:
                                mesh = new ParametricGeometry(stacks, slices, ParametricGeometry.Helicoid).Generate();
                                break;
                            case ParametricGeometryType.Horn:
                                mesh = new ParametricGeometry(stacks, slices, ParametricGeometry.Horn).Generate();
                                break;
                            case ParametricGeometryType.Mobius:
                                mesh = new ParametricGeometry(stacks, slices, ParametricGeometry.Mobius).Generate();
                                break;
                            case ParametricGeometryType.Mobius3d:
                                mesh = new ParametricGeometry(stacks, slices, ParametricGeometry.Mobius3d).Generate();
                                break;
                            case ParametricGeometryType.Natica:
                                mesh = new ParametricGeometry(stacks, slices, ParametricGeometry.Natica).Generate();
                                break;
                            case ParametricGeometryType.Pillow:
                                mesh = new ParametricGeometry(stacks, slices, ParametricGeometry.Pillow).Generate();
                                break;
                            case ParametricGeometryType.Scherk:
                                mesh = new ParametricGeometry(stacks, slices, ParametricGeometry.Scherk).Generate();
                                break;
                            case ParametricGeometryType.Snail:
                                mesh = new ParametricGeometry(stacks, slices, ParametricGeometry.Snail).Generate();
                                break;
                            case ParametricGeometryType.Spiral:
                                mesh = new ParametricGeometry(stacks, slices, ParametricGeometry.Spiral).Generate();
                                break;
                            case ParametricGeometryType.Spring:
                                mesh = new ParametricGeometry(stacks, slices, ParametricGeometry.Spring).Generate();
                                break;
                        }
                        break;
                }
                if(mesh)
                {
                    geometryCache.Add(signature, mesh);
                }
                return mesh;
            }
        }
        internal override void StartStuff()
        {
            SetGeometry();
        }

        internal override void UpdateStuff()
        {
            
        }
        internal void UpdateCallback(List<PropertyName> changedProperties)
        {
            SetGeometry(changedProperties);
        }
        public void SetGeometry(List<PropertyName> changedProperties = null)
        {
            SetLoadedIfNot();
            _filter = GetComponent<MeshFilter>();
            if (_filter == null)
            {
                _filter = gameObject.AddComponent<MeshFilter>();
            }
            _filter.sharedMesh = GetCachedMesh();
            if (changedProperties != null && changedProperties.Contains(PropertyName.parametricPoints)
            && geometryType == GeometryType.ParametricGeometry && parametricType == ParametricGeometryType.Custom)
            {
                var pointsData = JsonUtility.FromJson<ParametricPoints>(parametricPoints);
                _filter.sharedMesh = new ParametricGeometry(stacks, slices, null, pointsData.points).Generate();
            }
        }

        internal override void DestroyStuff()
        {
            if (_filter != null)
            {
                Destroy(_filter.sharedMesh);
                geometryCache.Remove(GetSignature());
                Destroy(_filter);
            }
        }
        // BANTER COMPILED CODE 
        public Banter.SDK.GeometryType GeometryType { get { return geometryType; } set { geometryType = value; UpdateCallback(new List<PropertyName> { PropertyName.geometryType }); } }
        public Banter.SDK.ParametricGeometryType ParametricType { get { return parametricType; } set { parametricType = value; UpdateCallback(new List<PropertyName> { PropertyName.parametricType }); } }
        public System.Single Width { get { return width; } set { width = value; UpdateCallback(new List<PropertyName> { PropertyName.width }); } }
        public System.Single Height { get { return height; } set { height = value; UpdateCallback(new List<PropertyName> { PropertyName.height }); } }
        public System.Single Depth { get { return depth; } set { depth = value; UpdateCallback(new List<PropertyName> { PropertyName.depth }); } }
        public System.Int32 WidthSegments { get { return widthSegments; } set { widthSegments = value; UpdateCallback(new List<PropertyName> { PropertyName.widthSegments }); } }
        public System.Int32 HeightSegments { get { return heightSegments; } set { heightSegments = value; UpdateCallback(new List<PropertyName> { PropertyName.heightSegments }); } }
        public System.Int32 DepthSegments { get { return depthSegments; } set { depthSegments = value; UpdateCallback(new List<PropertyName> { PropertyName.depthSegments }); } }
        public System.Single Radius { get { return radius; } set { radius = value; UpdateCallback(new List<PropertyName> { PropertyName.radius }); } }
        public System.Int32 Segments { get { return segments; } set { segments = value; UpdateCallback(new List<PropertyName> { PropertyName.segments }); } }
        public System.Single ThetaStart { get { return thetaStart; } set { thetaStart = value; UpdateCallback(new List<PropertyName> { PropertyName.thetaStart }); } }
        public System.Single ThetaLength { get { return thetaLength; } set { thetaLength = value; UpdateCallback(new List<PropertyName> { PropertyName.thetaLength }); } }
        public System.Single PhiStart { get { return phiStart; } set { phiStart = value; UpdateCallback(new List<PropertyName> { PropertyName.phiStart }); } }
        public System.Single PhiLength { get { return phiLength; } set { phiLength = value; UpdateCallback(new List<PropertyName> { PropertyName.phiLength }); } }
        public System.Int32 RadialSegments { get { return radialSegments; } set { radialSegments = value; UpdateCallback(new List<PropertyName> { PropertyName.radialSegments }); } }
        public System.Boolean OpenEnded { get { return openEnded; } set { openEnded = value; UpdateCallback(new List<PropertyName> { PropertyName.openEnded }); } }
        public System.Single RadiusTop { get { return radiusTop; } set { radiusTop = value; UpdateCallback(new List<PropertyName> { PropertyName.radiusTop }); } }
        public System.Single RadiusBottom { get { return radiusBottom; } set { radiusBottom = value; UpdateCallback(new List<PropertyName> { PropertyName.radiusBottom }); } }
        public System.Single InnerRadius { get { return innerRadius; } set { innerRadius = value; UpdateCallback(new List<PropertyName> { PropertyName.innerRadius }); } }
        public System.Single OuterRadius { get { return outerRadius; } set { outerRadius = value; UpdateCallback(new List<PropertyName> { PropertyName.outerRadius }); } }
        public System.Int32 ThetaSegments { get { return thetaSegments; } set { thetaSegments = value; UpdateCallback(new List<PropertyName> { PropertyName.thetaSegments }); } }
        public System.Int32 PhiSegments { get { return phiSegments; } set { phiSegments = value; UpdateCallback(new List<PropertyName> { PropertyName.phiSegments }); } }
        public System.Single Tube { get { return tube; } set { tube = value; UpdateCallback(new List<PropertyName> { PropertyName.tube }); } }
        public System.Int32 TubularSegments { get { return tubularSegments; } set { tubularSegments = value; UpdateCallback(new List<PropertyName> { PropertyName.tubularSegments }); } }
        public System.Single Arc { get { return arc; } set { arc = value; UpdateCallback(new List<PropertyName> { PropertyName.arc }); } }
        public System.Int32 P { get { return p; } set { p = value; UpdateCallback(new List<PropertyName> { PropertyName.p }); } }
        public System.Int32 Q { get { return q; } set { q = value; UpdateCallback(new List<PropertyName> { PropertyName.q }); } }
        public System.Int32 Stacks { get { return stacks; } set { stacks = value; UpdateCallback(new List<PropertyName> { PropertyName.stacks }); } }
        public System.Int32 Slices { get { return slices; } set { slices = value; UpdateCallback(new List<PropertyName> { PropertyName.slices }); } }
        public System.Single Detail { get { return detail; } set { detail = value; UpdateCallback(new List<PropertyName> { PropertyName.detail }); } }
        public System.String ParametricPoints { get { return parametricPoints; } set { parametricPoints = value; UpdateCallback(new List<PropertyName> { PropertyName.parametricPoints }); } }

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
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.geometryType, PropertyName.parametricType, PropertyName.width, PropertyName.height, PropertyName.depth, PropertyName.widthSegments, PropertyName.heightSegments, PropertyName.depthSegments, PropertyName.radius, PropertyName.segments, PropertyName.thetaStart, PropertyName.thetaLength, PropertyName.phiStart, PropertyName.phiLength, PropertyName.radialSegments, PropertyName.openEnded, PropertyName.radiusTop, PropertyName.radiusBottom, PropertyName.innerRadius, PropertyName.outerRadius, PropertyName.thetaSegments, PropertyName.phiSegments, PropertyName.tube, PropertyName.tubularSegments, PropertyName.arc, PropertyName.p, PropertyName.q, PropertyName.stacks, PropertyName.slices, PropertyName.detail, PropertyName.parametricPoints, };
            UpdateCallback(changedProperties);
        }
        internal override string GetSignature()
        {
            return "BanterGeometry" +  PropertyName.geometryType + geometryType + PropertyName.parametricType + parametricType + PropertyName.width + width + PropertyName.height + height + PropertyName.depth + depth + PropertyName.widthSegments + widthSegments + PropertyName.heightSegments + heightSegments + PropertyName.depthSegments + depthSegments + PropertyName.radius + radius + PropertyName.segments + segments + PropertyName.thetaStart + thetaStart + PropertyName.thetaLength + thetaLength + PropertyName.phiStart + phiStart + PropertyName.phiLength + phiLength + PropertyName.radialSegments + radialSegments + PropertyName.openEnded + openEnded + PropertyName.radiusTop + radiusTop + PropertyName.radiusBottom + radiusBottom + PropertyName.innerRadius + innerRadius + PropertyName.outerRadius + outerRadius + PropertyName.thetaSegments + thetaSegments + PropertyName.phiSegments + phiSegments + PropertyName.tube + tube + PropertyName.tubularSegments + tubularSegments + PropertyName.arc + arc + PropertyName.p + p + PropertyName.q + q + PropertyName.stacks + stacks + PropertyName.slices + slices + PropertyName.detail + detail + PropertyName.parametricPoints + parametricPoints;
        }

        internal override void Init(List<object> constructorProperties = null)
        {
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(gameObject.GetInstanceID(), GetInstanceID(), ComponentType.BanterGeometry);


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
                if (values[i] is BanterInt)
                {
                    var valgeometryType = (BanterInt)values[i];
                    if (valgeometryType.n == PropertyName.geometryType)
                    {
                        geometryType = (GeometryType)valgeometryType.x;
                        changedProperties.Add(PropertyName.geometryType);
                    }
                }
                if (values[i] is BanterInt)
                {
                    var valparametricType = (BanterInt)values[i];
                    if (valparametricType.n == PropertyName.parametricType)
                    {
                        parametricType = (ParametricGeometryType)valparametricType.x;
                        changedProperties.Add(PropertyName.parametricType);
                    }
                }
                if (values[i] is BanterFloat)
                {
                    var valwidth = (BanterFloat)values[i];
                    if (valwidth.n == PropertyName.width)
                    {
                        width = valwidth.x;
                        changedProperties.Add(PropertyName.width);
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
                if (values[i] is BanterFloat)
                {
                    var valdepth = (BanterFloat)values[i];
                    if (valdepth.n == PropertyName.depth)
                    {
                        depth = valdepth.x;
                        changedProperties.Add(PropertyName.depth);
                    }
                }
                if (values[i] is BanterInt)
                {
                    var valwidthSegments = (BanterInt)values[i];
                    if (valwidthSegments.n == PropertyName.widthSegments)
                    {
                        widthSegments = valwidthSegments.x;
                        changedProperties.Add(PropertyName.widthSegments);
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
                if (values[i] is BanterInt)
                {
                    var valdepthSegments = (BanterInt)values[i];
                    if (valdepthSegments.n == PropertyName.depthSegments)
                    {
                        depthSegments = valdepthSegments.x;
                        changedProperties.Add(PropertyName.depthSegments);
                    }
                }
                if (values[i] is BanterFloat)
                {
                    var valradius = (BanterFloat)values[i];
                    if (valradius.n == PropertyName.radius)
                    {
                        radius = valradius.x;
                        changedProperties.Add(PropertyName.radius);
                    }
                }
                if (values[i] is BanterInt)
                {
                    var valsegments = (BanterInt)values[i];
                    if (valsegments.n == PropertyName.segments)
                    {
                        segments = valsegments.x;
                        changedProperties.Add(PropertyName.segments);
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
                if (values[i] is BanterFloat)
                {
                    var valphiStart = (BanterFloat)values[i];
                    if (valphiStart.n == PropertyName.phiStart)
                    {
                        phiStart = valphiStart.x;
                        changedProperties.Add(PropertyName.phiStart);
                    }
                }
                if (values[i] is BanterFloat)
                {
                    var valphiLength = (BanterFloat)values[i];
                    if (valphiLength.n == PropertyName.phiLength)
                    {
                        phiLength = valphiLength.x;
                        changedProperties.Add(PropertyName.phiLength);
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
                    var valradiusTop = (BanterFloat)values[i];
                    if (valradiusTop.n == PropertyName.radiusTop)
                    {
                        radiusTop = valradiusTop.x;
                        changedProperties.Add(PropertyName.radiusTop);
                    }
                }
                if (values[i] is BanterFloat)
                {
                    var valradiusBottom = (BanterFloat)values[i];
                    if (valradiusBottom.n == PropertyName.radiusBottom)
                    {
                        radiusBottom = valradiusBottom.x;
                        changedProperties.Add(PropertyName.radiusBottom);
                    }
                }
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
                    var valtube = (BanterFloat)values[i];
                    if (valtube.n == PropertyName.tube)
                    {
                        tube = valtube.x;
                        changedProperties.Add(PropertyName.tube);
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
                if (values[i] is BanterInt)
                {
                    var valstacks = (BanterInt)values[i];
                    if (valstacks.n == PropertyName.stacks)
                    {
                        stacks = valstacks.x;
                        changedProperties.Add(PropertyName.stacks);
                    }
                }
                if (values[i] is BanterInt)
                {
                    var valslices = (BanterInt)values[i];
                    if (valslices.n == PropertyName.slices)
                    {
                        slices = valslices.x;
                        changedProperties.Add(PropertyName.slices);
                    }
                }
                if (values[i] is BanterFloat)
                {
                    var valdetail = (BanterFloat)values[i];
                    if (valdetail.n == PropertyName.detail)
                    {
                        detail = valdetail.x;
                        changedProperties.Add(PropertyName.detail);
                    }
                }
                if (values[i] is BanterString)
                {
                    var valparametricPoints = (BanterString)values[i];
                    if (valparametricPoints.n == PropertyName.parametricPoints)
                    {
                        parametricPoints = valparametricPoints.x;
                        changedProperties.Add(PropertyName.parametricPoints);
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
                    name = PropertyName.geometryType,
                    type = PropertyType.Int,
                    value = geometryType,
                    componentType = ComponentType.BanterGeometry,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.parametricType,
                    type = PropertyType.Int,
                    value = parametricType,
                    componentType = ComponentType.BanterGeometry,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.width,
                    type = PropertyType.Float,
                    value = width,
                    componentType = ComponentType.BanterGeometry,
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
                    componentType = ComponentType.BanterGeometry,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.depth,
                    type = PropertyType.Float,
                    value = depth,
                    componentType = ComponentType.BanterGeometry,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.widthSegments,
                    type = PropertyType.Int,
                    value = widthSegments,
                    componentType = ComponentType.BanterGeometry,
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
                    componentType = ComponentType.BanterGeometry,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.depthSegments,
                    type = PropertyType.Int,
                    value = depthSegments,
                    componentType = ComponentType.BanterGeometry,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.radius,
                    type = PropertyType.Float,
                    value = radius,
                    componentType = ComponentType.BanterGeometry,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.segments,
                    type = PropertyType.Int,
                    value = segments,
                    componentType = ComponentType.BanterGeometry,
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
                    componentType = ComponentType.BanterGeometry,
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
                    componentType = ComponentType.BanterGeometry,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.phiStart,
                    type = PropertyType.Float,
                    value = phiStart,
                    componentType = ComponentType.BanterGeometry,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.phiLength,
                    type = PropertyType.Float,
                    value = phiLength,
                    componentType = ComponentType.BanterGeometry,
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
                    componentType = ComponentType.BanterGeometry,
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
                    componentType = ComponentType.BanterGeometry,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.radiusTop,
                    type = PropertyType.Float,
                    value = radiusTop,
                    componentType = ComponentType.BanterGeometry,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.radiusBottom,
                    type = PropertyType.Float,
                    value = radiusBottom,
                    componentType = ComponentType.BanterGeometry,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.innerRadius,
                    type = PropertyType.Float,
                    value = innerRadius,
                    componentType = ComponentType.BanterGeometry,
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
                    componentType = ComponentType.BanterGeometry,
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
                    componentType = ComponentType.BanterGeometry,
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
                    componentType = ComponentType.BanterGeometry,
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
                    componentType = ComponentType.BanterGeometry,
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
                    componentType = ComponentType.BanterGeometry,
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
                    componentType = ComponentType.BanterGeometry,
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
                    componentType = ComponentType.BanterGeometry,
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
                    componentType = ComponentType.BanterGeometry,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.stacks,
                    type = PropertyType.Int,
                    value = stacks,
                    componentType = ComponentType.BanterGeometry,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.slices,
                    type = PropertyType.Int,
                    value = slices,
                    componentType = ComponentType.BanterGeometry,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.detail,
                    type = PropertyType.Float,
                    value = detail,
                    componentType = ComponentType.BanterGeometry,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.parametricPoints,
                    type = PropertyType.String,
                    value = parametricPoints,
                    componentType = ComponentType.BanterGeometry,
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