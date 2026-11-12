using UnityEngine;

namespace BS
{
    /// <summary>
    /// The (p,q) torus knot curve, as three.js computes it.
    /// </summary>
    public class TorusKnotCurve : Curve
    {
        readonly float radius;
        readonly int p;
        readonly int q;

        public TorusKnotCurve(float radius, int p, int q)
        {
            this.radius = radius;
            this.p = p;
            this.q = q;
        }

        public override Vector3 GetPoint(float t)
        {
            var u = t * p * Mathf.PI * 2;

            var cu = Mathf.Cos(u);
            var su = Mathf.Sin(u);
            var quOverP = (float)q / p * u;
            var cs = Mathf.Cos(quOverP);

            return new Vector3(
                radius * (2 + cs) * 0.5f * cu,
                radius * (2 + cs) * su * 0.5f,
                radius * Mathf.Sin(quOverP) * 0.5f);
        }
    }

    /// <summary>
    /// A tube swept along a (p,q) torus knot.
    ///
    /// three.js builds this with its own inline frame, `N = P2 + P1`, which points radially from
    /// the origin. On a (2,3) knot the curve passes either side of the origin, so that frame
    /// reverses handedness partway round: adjacent rings come out mirrored, the quads between them
    /// become bow-ties, and the surface renders shredded. Sweeping with <see cref="Tube"/> instead
    /// uses rotation-minimising frames, which carry a consistent normal along the whole curve and
    /// cannot flip.
    ///
    /// The Tube it delegates to has already converted handedness and recentred, so this must not
    /// do either again.
    /// </summary>
    public class TorusKnot : Geometry
    {
        // Largest extent is 3*radius + 2*tube: the tube offset runs along +x at u = 0, where
        // cos((q/p)u) peaks, independent of p and q. 3(0.25) + 2(0.1) = 0.95.
        public TorusKnot(float radius = 0.25f, float tube = 0.1f, int radialSegments = 8, int tubularSegments = 64, int p = 2, int q = 3)
        {
            var curve = new TorusKnotCurve(radius, Mathf.Max(1, p), Mathf.Max(1, q));
            var tubeMesh = new Tube(curve, tubularSegments, tube, radialSegments, true);

            indices = tubeMesh.indices;
            vertices = tubeMesh.vertices;
            normals = tubeMesh.normals;
            uvs = tubeMesh.uvs;
        }
    }
}
