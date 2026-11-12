using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BS
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
        const geometry = await gameObject.AddComponent(new BS.BSGeometry(geometryType, parametricType, width, height, depth, widthSegments, heightSegments, depthSegments, radius, segments, thetaStart, thetaLength, phiStart, phiLength, radialSegments, openEnded, radiusTop, radiusBottom, innerRadius, outerRadius, thetaSegments, phiSegments, tube, tubularSegments, arc, p, q, stacks, slices, detail, parametricPoints));
    ```
    */
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BSObjectId))]
    [WatchComponent]
    public partial class BSGeometry : BSComponentBase
    {
        [Tooltip("The type of primitive shape to generate.")]
        [See(initial = "0")][SerializeField] internal GeometryType geometryType = GeometryType.BoxGeometry;
        [Tooltip("The type of parametric shape to generate if using ParametricGeometry.")]
        [See(initial = "0")][SerializeField] internal ParametricGeometryType parametricType = ParametricGeometryType.Klein;
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

        [Tooltip("The overall radius of the shape. For a torus or torus knot this is the outer radius, not three.js's ring radius, so that one radius means the same thing for every shape.")]
        [See(initial = "0.5")][SerializeField] internal float radius = 0.5f;
        [Tooltip("The number of segments to divide the shape into.")]
        [See(initial = "32")][SerializeField] internal int segments = 32;
        [Tooltip("The starting x angle of the shape.")]
        [See(initial = "0")][SerializeField] internal float thetaStart = 0;
        [Tooltip("How far the shape sweeps from thetaStart, in radians.")]
        [See(initial = "6.283185")][SerializeField] internal float thetaLength = Mathf.PI * 2f;

        [Tooltip("The starting y angle of the shape.")]
        [See(initial = "0")][SerializeField] internal float phiStart = 0;
        [Tooltip("How far the shape sweeps from phiStart, in radians.")]
        [See(initial = "6.283185")][SerializeField] internal float phiLength = Mathf.PI * 2f;

        [Tooltip("The number of radial segments to divide the shape into.")]
        [See(initial = "32")][SerializeField] internal int radialSegments = 32;
        [Tooltip("Whether the object is open on the end or not.")]
        [See(initial = "false")][SerializeField] internal bool openEnded = false;

        [Tooltip("The radius of the top of the shape.")]
        [See(initial = "0.5")][SerializeField] internal float radiusTop = 0.5f;
        [Tooltip("The radius of the bottom of the shape.")]
        [See(initial = "0.5")][SerializeField] internal float radiusBottom = 0.5f;

        [Tooltip("The inner radius of the shape.")]
        [See(initial = "0.15")][SerializeField] internal float innerRadius = 0.15f;
        [Tooltip("The outer radius of the shape.")]
        [See(initial = "0.5")][SerializeField] internal float outerRadius = 0.5f;
        [Tooltip("The number of angular x segments to divide the shape into.")]
        [See(initial = "32")][SerializeField] internal int thetaSegments = 32;
        [Tooltip("The number of angular y segments to divide the shape into.")]
        [See(initial = "1")][SerializeField] internal int phiSegments = 1;
        [Tooltip("The tube size.")]
        [See(initial = "0.15")][SerializeField] internal float tube = 0.15f;
        [Tooltip("The number of tubular segments to divide the tube into.")]
        [See(initial = "32")][SerializeField] internal int tubularSegments = 32;
        [Tooltip("The arc of the shape.")]
        [See(initial = "6.283185")][SerializeField] internal float arc = Mathf.PI * 2;
        [Tooltip("The number of p segments to divide the shape into.")]
        [See(initial = "2")][SerializeField] internal int p = 2;
        [Tooltip("The number of q segments to divide the shape into.")]
        [See(initial = "3")][SerializeField] internal int q = 3;
        [Tooltip("The number of stacks to divide the shape into.")]
        [See(initial = "32")][SerializeField] internal int stacks = 32;
        [Tooltip("The number of slices to divide the shape into.")]
        [See(initial = "32")][SerializeField] internal int slices = 32;
        [See(initial = "0")][SerializeField] internal float detail = 0;
        [See(initial = "")][SerializeField] internal string parametricPoints = "";
        [Tooltip("The 3D curve to sweep along, as JSON. Used by TubeGeometry, and as the extrude path for ExtrudeGeometry.")]
        [See(initial = "")][SerializeField] internal string curvePoints = "";
        [Tooltip("The 2D outline and holes of the shape, as JSON. Used by ShapeGeometry and ExtrudeGeometry.")]
        [See(initial = "")][SerializeField] internal string shapePoints = "";
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

        /// <summary>
        /// Each named surface has its own Geometry class, which is what applies the unit-cube
        /// normalisation - the surface functions have no size parameters of their own and range
        /// from roughly 4m to 15m across.
        /// </summary>
        static readonly Dictionary<ParametricGeometryType, Func<int, int, Geometry>> ParametricFactory =
            new Dictionary<ParametricGeometryType, Func<int, int, Geometry>>
            {
                { ParametricGeometryType.Klein, (st, sl) => new Klein(st, sl) },
                { ParametricGeometryType.Apple, (st, sl) => new Apple(st, sl) },
                { ParametricGeometryType.Fermet, (st, sl) => new Fermet(st, sl) },
                { ParametricGeometryType.Catenoid, (st, sl) => new Catenoid(st, sl) },
                { ParametricGeometryType.Helicoid, (st, sl) => new Helicoid(st, sl) },
                { ParametricGeometryType.Horn, (st, sl) => new Horn(st, sl) },
                { ParametricGeometryType.Mobius, (st, sl) => new Mobius(st, sl) },
                { ParametricGeometryType.Mobius3d, (st, sl) => new Mobius3d(st, sl) },
                { ParametricGeometryType.Natica, (st, sl) => new Natica(st, sl) },
                { ParametricGeometryType.Pillow, (st, sl) => new Pillow(st, sl) },
                { ParametricGeometryType.Scherk, (st, sl) => new Scherk(st, sl) },
                { ParametricGeometryType.Snail, (st, sl) => new Snail(st, sl) },
                { ParametricGeometryType.Spiral, (st, sl) => new Spiral(st, sl) },
                { ParametricGeometryType.Spring, (st, sl) => new Spring(st, sl) },
            };

        Mesh BuildParametric()
        {
            if (parametricType == ParametricGeometryType.Custom)
            {
                // Built here rather than in SetGeometry so it survives the first Start - the old
                // path only ran when changedProperties was non-null, which StartStuff never is.
                if (string.IsNullOrEmpty(parametricPoints))
                {
                    return null;
                }
                if (!PageJson.IsSafe(parametricPoints))
                {
                    Debug.LogWarning("[BSGeometry] parametricPoints are nested too deep to parse.");
                    return null;
                }
                try
                {
                    var pointsData = JsonUtility.FromJson<ParametricPoints>(parametricPoints);
                    return new ParametricGeometry(stacks, slices, null, pointsData.points).Generate();
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[BSGeometry] Could not parse parametricPoints: " + e.Message);
                    return null;
                }
            }

            return ParametricFactory.TryGetValue(parametricType, out var build)
                ? build(stacks, slices).Generate()
                : null;
        }

        private Mesh GetCachedMesh()
        {
            var signature = GetSignature();
            if (geometryCache.ContainsKey(signature))
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
                        // A cone is a cylinder with radiusTop == 0 by definition. radiusTop belongs
                        // to CylinderGeometry and defaults to a non-zero value, and the generated JS
                        // coalesces a caller-supplied 0 back to that default, so the only place this
                        // can be forced is here. Without it ConeGeometry renders a cylinder.
                        mesh = new Cone(radius, height, radialSegments, heightSegments, openEnded, thetaStart, thetaLength).Generate();
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
                        // widthSegments/heightSegments and thetaLength are shared with shapes that
                        // want very different values - a sphere given Box's segment counts of 1, or
                        // a thetaLength of 2*PI, emits zero triangles and renders as nothing. Only
                        // values that are degenerate for a sphere are replaced, so a deliberately
                        // low-poly or hemispherical sphere still works.
                        mesh = new Sphere(radius,
                            widthSegments <= 2 ? 32 : widthSegments,
                            heightSegments <= 1 ? 16 : heightSegments,
                            phiStart, phiLength, thetaStart,
                            Mathf.Min(thetaLength, Mathf.PI)).Generate();
                        break;
                    case GeometryType.TorusGeometry:
                        // `radius` is the overall outer radius here, not three.js's ring radius, so
                        // that one shared radius field means the same thing for every shape and the
                        // defaults land inside a 1m cube.
                        mesh = new Torus(Mathf.Max(0.001f, radius - tube), tube, radialSegments, tubularSegments, arc).Generate();
                        break;
                    case GeometryType.TorusKnotGeometry:
                        // Largest extent of the knot is 3R + 2*tube; solving for an overall radius
                        // keeps `radius` consistent with the other shapes.
                        mesh = new TorusKnot(Mathf.Max(0.001f, (2 * radius - 2 * tube) / 3), tube, radialSegments, tubularSegments, p, q).Generate();
                        break;
                    case GeometryType.CapsuleGeometry:
                        // `height` is the total height and `radius` the overall bounding radius, so
                        // the caps are radius/2 and the straight section is whatever is left. Taking
                        // radius as the cap radius directly would make the default 0.5/1 capsule a
                        // plain sphere. heightSegments doubles as the cap resolution, and its shared
                        // default of 1 (Box's value) would give a two-ring hemisphere.
                        var capRadius = Mathf.Min(radius / 2, height / 2);
                        mesh = new Capsule(capRadius, Mathf.Max(0, height - 2 * capRadius),
                            heightSegments <= 1 ? 8 : heightSegments, radialSegments).Generate();
                        break;
                    case GeometryType.DodecahedronGeometry:
                        mesh = new Dodecahedron(radius, detail).Generate();
                        break;
                    case GeometryType.IcosahedronGeometry:
                        mesh = new Icosahedron(radius, detail).Generate();
                        break;
                    case GeometryType.OctahedronGeometry:
                        mesh = new Octahedron(radius, detail).Generate();
                        break;
                    case GeometryType.TetrahedronGeometry:
                        mesh = new Tetrahedron(radius, detail).Generate();
                        break;
                    case GeometryType.TubeGeometry:
                        var path = CurveDefinition.Parse(curvePoints);
                        if (path != null)
                        {
                            mesh = new Tube(path, tubularSegments, radius, radialSegments,
                                path is CatmullRomCurve3 crc && crc.closed).Generate();
                        }
                        break;
                    case GeometryType.ShapeGeometry:
                        var shape = ShapeCommands.Parse(shapePoints);
                        if (shape != null)
                        {
                            mesh = new ShapeGeometry(shape, segments).Generate();
                        }
                        break;
                    case GeometryType.ExtrudeGeometry:
                        var extrudeShape = ShapeCommands.Parse(shapePoints);
                        if (extrudeShape != null)
                        {
                            // depthSegments doubles as three.js's `steps`; both mean subdivisions
                            // along the extrusion axis and share a default of 1.
                            mesh = new ExtrudeGeometry(extrudeShape, depth, depthSegments, segments,
                                CurveDefinition.Parse(curvePoints)).Generate();
                        }
                        break;
                    case GeometryType.LatheGeometry:
                        var profile = ShapeCommands.Parse(shapePoints);
                        if (profile != null)
                        {
                            mesh = new Lathe(profile.GetPoints2D(segments), radialSegments, phiStart, phiLength).Generate();
                        }
                        break;
                    case GeometryType.ParametricGeometry:
                        mesh = BuildParametric();
                        break;
                }
                if (mesh)
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
        }

        internal override void DestroyStuff()
        {
            if (_filter != null)
            {
                // The mesh is owned by the static cache and shared with every other object of
                // identical geometry, so destroying it here blanked all of them. Unit-cube
                // defaults make identical signatures far more common, which turned this from rare
                // to routine. ClearCache is the only thing that should free these.
                Destroy(_filter);
            }
        }
    }
}