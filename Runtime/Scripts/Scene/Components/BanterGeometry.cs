using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Banter
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
    [RequireComponent(typeof(BanterObjectId))]
    [WatchComponent]
    public class BanterGeometry : BanterComponentBase
    {
        [Tooltip("The type of primitive shape to generate.")]
        [See(initial = "0")] public GeometryType geometryType;
        [Tooltip("The type of parametric shape to generate if using ParametricGeometry.")]
        [See(initial = "0")] public ParametricGeometryType parametricType;
        [Tooltip("The width of the shape.")]
        [See(initial = "1")] public float width = 1;
        [Tooltip("The height of the shape.")]
        [See(initial = "1")] public float height = 1;
        [Tooltip("The depth of the shape.")]
        [See(initial = "1")] public float depth = 1;
        [Tooltip("The number of width segments to divide the shape into.")]
        [See(initial = "1")] public int widthSegments = 1;
        [Tooltip("The number of height segments to divide the shape into.")]
        [See(initial = "1")] public int heightSegments = 1;
        [Tooltip("The number of depth segments to divide the shape into.")]
        [See(initial = "1")] public int depthSegments = 1;

        [Tooltip("The radius of the shape.")]
        [See(initial = "1")] public float radius = 1;
        [Tooltip("The number of segments to divide the shape into.")]
        [See(initial = "24")] public int segments = 24;
        [Tooltip("The starting x angle of the shape.")]
        [See(initial = "0")] public float thetaStart = 0;
        [Tooltip("The ending x angle of the shape.")]
        [See(initial = "6.283185")] public float thetaLength = Mathf.PI * 2f;

        [Tooltip("The starting y angle of the shape.")]
        [See(initial = "0")] public float phiStart = 0;
        [Tooltip("The ending y angle of the shape.")]
        [See(initial = "6.283185")] public float phiLength = Mathf.PI * 2f;

        [Tooltip("The number of radial segments to divide the shape into.")]
        [See(initial = "8")] public int radialSegments = 8;
        [Tooltip("Whether the object is open on the end or not.")]
        [See(initial = "false")] public bool openEnded = false;

        [Tooltip("The radius of the top of the shape.")]
        [See(initial = "1")] public float radiusTop = 1;
        [Tooltip("The radius of the bottom of the shape.")]
        [See(initial = "1")] public float radiusBottom = 1;

        [Tooltip("The inner radius of the shape.")]
        [See(initial = "0.3")] public float innerRadius = 0.3f;
        [Tooltip("The outer radius of the shape.")]
        [See(initial = "1")] public float outerRadius = 1f;
        [Tooltip("The number of angular x segments to divide the shape into.")]
        [See(initial = "24")] public int thetaSegments = 24;
        [Tooltip("The number of angular y segments to divide the shape into.")]
        [See(initial = "8")] public int phiSegments = 8;
        [Tooltip("The tube size.")]
        [See(initial = "0.4")] public float tube = 0.4f;
        [Tooltip("The number of tubular segments to divide the tube into.")]
        [See(initial = "16")] public int tubularSegments = 16;
        [Tooltip("The arc of the shape.")]
        [See(initial = "6.283185")] public float arc = Mathf.PI * 2;
        [Tooltip("The number of p segments to divide the shape into.")]
        [See(initial = "2")] public int p = 2;
        [Tooltip("The number of q segments to divide the shape into.")]
        [See(initial = "3")] public int q = 3;
        [Tooltip("The number of stacks to divide the shape into.")]
        [See(initial = "5")] public int stacks = 5;
        [Tooltip("The number of slices to divide the shape into.")]
        [See(initial = "5")] public int slices = 5;
        [See(initial = "0")] public float detail = 0;
        [See(initial = "")] public string parametricPoints = "";
        MeshFilter _filter;
        // bool alreadyStarted = false;
        public override void StartStuff()
        {
            SetGeometry();
        }
        public void UpdateCallback(List<PropertyName> changedProperties)
        {
            SetGeometry(changedProperties);
        }
        void SetGeometry(List<PropertyName> changedProperties = null)
        {
            SetLoadedIfNot();
            _filter = GetComponent<MeshFilter>();
            if (_filter == null)
            {
                _filter = gameObject.AddComponent<MeshFilter>();
            }
            switch (geometryType)
            {
                case GeometryType.BoxGeometry:
                    _filter.sharedMesh = new Box(width, height, depth, widthSegments, heightSegments, depthSegments).generate();
                    break;
                case GeometryType.CircleGeometry:
                    _filter.sharedMesh = new Circle(radius, segments, thetaStart, thetaLength).generate();
                    break;
                case GeometryType.ConeGeometry:
                    _filter.sharedMesh = new Cylinder(radiusTop, radiusBottom, height, radialSegments, heightSegments, openEnded, thetaStart, thetaLength).generate();
                    break;
                case GeometryType.CylinderGeometry:
                    _filter.sharedMesh = new Cylinder(radiusTop, radiusBottom, height, radialSegments, heightSegments, openEnded, thetaStart, thetaLength).generate();
                    break;
                case GeometryType.PlaneGeometry:
                    _filter.sharedMesh = new Plane(width, height, widthSegments, heightSegments).generate();
                    break;
                case GeometryType.RingGeometry:
                    _filter.sharedMesh = new Ring(innerRadius, outerRadius, thetaSegments, phiSegments, thetaStart, thetaLength).generate();
                    break;
                case GeometryType.SphereGeometry:
                    _filter.sharedMesh = new Sphere(radius, widthSegments, heightSegments, phiStart, phiLength, thetaStart, thetaLength).generate();
                    break;
                case GeometryType.TorusGeometry:
                    _filter.sharedMesh = new Torus(radius, tube, radialSegments, tubularSegments, arc).generate();
                    break;
                case GeometryType.TorusKnotGeometry:
                    _filter.sharedMesh = new TorusKnot(radius, tube, radialSegments, tubularSegments, p, q).generate();
                    break;
                case GeometryType.ParametricGeometry:
                    switch (parametricType)
                    {
                        case ParametricGeometryType.Klein:
                            _filter.sharedMesh = new ParametricGeometry(stacks, slices, ParametricGeometry.Klein).generate();
                            break;
                        case ParametricGeometryType.Apple:
                            _filter.sharedMesh = new ParametricGeometry(stacks, slices, ParametricGeometry.Apple).generate();
                            break;
                        case ParametricGeometryType.Fermet:
                            _filter.sharedMesh = new ParametricGeometry(stacks, slices, ParametricGeometry.Fermet).generate();
                            break;
                        case ParametricGeometryType.Catenoid:
                            _filter.sharedMesh = new ParametricGeometry(stacks, slices, ParametricGeometry.Catenoid).generate();
                            break;
                        case ParametricGeometryType.Helicoid:
                            _filter.sharedMesh = new ParametricGeometry(stacks, slices, ParametricGeometry.Helicoid).generate();
                            break;
                        case ParametricGeometryType.Horn:
                            _filter.sharedMesh = new ParametricGeometry(stacks, slices, ParametricGeometry.Horn).generate();
                            break;
                        case ParametricGeometryType.Mobius:
                            _filter.sharedMesh = new ParametricGeometry(stacks, slices, ParametricGeometry.Mobius).generate();
                            break;
                        case ParametricGeometryType.Mobius3d:
                            _filter.sharedMesh = new ParametricGeometry(stacks, slices, ParametricGeometry.Mobius3d).generate();
                            break;
                        case ParametricGeometryType.Natica:
                            _filter.sharedMesh = new ParametricGeometry(stacks, slices, ParametricGeometry.Natica).generate();
                            break;
                        case ParametricGeometryType.Pillow:
                            _filter.sharedMesh = new ParametricGeometry(stacks, slices, ParametricGeometry.Pillow).generate();
                            break;
                        case ParametricGeometryType.Scherk:
                            _filter.sharedMesh = new ParametricGeometry(stacks, slices, ParametricGeometry.Scherk).generate();
                            break;
                        case ParametricGeometryType.Snail:
                            _filter.sharedMesh = new ParametricGeometry(stacks, slices, ParametricGeometry.Snail).generate();
                            break;
                        case ParametricGeometryType.Spiral:
                            _filter.sharedMesh = new ParametricGeometry(stacks, slices, ParametricGeometry.Spiral).generate();
                            break;
                        case ParametricGeometryType.Spring:
                            _filter.sharedMesh = new ParametricGeometry(stacks, slices, ParametricGeometry.Spring).generate();
                            break;
                    }
                    break;
            }
            if (changedProperties != null && changedProperties.Contains(PropertyName.parametricPoints)
            && geometryType == GeometryType.ParametricGeometry && parametricType == ParametricGeometryType.Custom)
            {
                var pointsData = JsonUtility.FromJson<ParametricPoints>(parametricPoints);
                _filter.sharedMesh = new ParametricGeometry(stacks, slices, null, pointsData.points).generate();
            }
        }

        public override void DestroyStuff()
        {
            if (_filter != null)
            {
                Destroy(_filter);
            }
        }
        // BANTER COMPILED CODE 
        BanterScene scene;

        bool alreadyStarted = false;

        void Start()
        {
            Init();
            StartStuff();
        }
        public override void ReSetup()
        {
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.geometryType, PropertyName.parametricType, PropertyName.width, PropertyName.height, PropertyName.depth, PropertyName.widthSegments, PropertyName.heightSegments, PropertyName.depthSegments, PropertyName.radius, PropertyName.segments, PropertyName.thetaStart, PropertyName.thetaLength, PropertyName.phiStart, PropertyName.phiLength, PropertyName.radialSegments, PropertyName.openEnded, PropertyName.radiusTop, PropertyName.radiusBottom, PropertyName.innerRadius, PropertyName.outerRadius, PropertyName.thetaSegments, PropertyName.phiSegments, PropertyName.tube, PropertyName.tubularSegments, PropertyName.arc, PropertyName.p, PropertyName.q, PropertyName.stacks, PropertyName.slices, PropertyName.detail, PropertyName.parametricPoints, };
            UpdateCallback(changedProperties);
        }


        public override void Init()
        {
            scene = BanterScene.Instance();
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(gameObject.GetInstanceID(), GetInstanceID(), ComponentType.BanterGeometry);


            oid = gameObject.GetInstanceID();
            cid = GetInstanceID();
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
        public override object CallMethod(string methodName, List<object> parameters)
        {
            return null;
        }

        public override void Deserialise(List<object> values)
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
        public override void SyncProperties(bool force = false, Action callback = null)
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
        public override void WatchProperties(PropertyName[] properties)
        {
        }
        // END BANTER COMPILED CODE 
    }
}