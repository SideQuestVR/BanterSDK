using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BS
{
    /// <summary>
    /// Base for every mesh builder in this folder. Subclasses fill the four lists in their
    /// constructor and inherit <see cref="Generate"/>.
    ///
    /// The builders are transcribed from three.js, which is right-handed with counter-clockwise
    /// front faces; Unity is left-handed with clockwise front faces. Subclasses must therefore
    /// keep three.js's coordinates and index order verbatim and call
    /// <see cref="ConvertToUnityHandedness"/> once at the end, rather than negating axes or
    /// flipping indices inline. Doing it inline is what left Sphere, Cylinder, Circle, Ring and
    /// Plane each converting differently - Circle and Ring ended up with normals opposing their
    /// own winding, and Plane was never converted at all.
    /// </summary>
    [System.Serializable]
    public class Geometry
    {
        public List<int> indices;
        public List<Vector3> vertices;
        public List<Vector3> normals;
        public List<Vector2> uvs;

        public Mesh Generate()
        {
            Mesh mesh = new Mesh();
            if (vertices.Count > 65535)
            {
                mesh.indexFormat = IndexFormat.UInt32;
            }
            mesh.vertices = vertices.ToArray();
            mesh.triangles = indices.ToArray();
            mesh.uv = uvs.ToArray();

            // The custom-points parametric path has no surface function to differentiate, so it
            // leaves normals empty and relies on this.
            if (normals == null || normals.Count != vertices.Count)
            {
                mesh.RecalculateNormals();
            }
            else
            {
                mesh.normals = normals.ToArray();
            }
            return mesh;
        }

        /// <summary>
        /// Converts a verbatim three.js mesh into Unity's coordinate system: negate z on
        /// positions and normals, and reverse each triangle's winding. Both are required - the
        /// negation is what stops chiral shapes coming out mirrored, and the winding flip is
        /// what stops flat shapes (where z is already 0) facing backwards.
        /// </summary>
        protected void ConvertToUnityHandedness()
        {
            for (int i = 0; i < vertices.Count; i++)
            {
                var v = vertices[i];
                vertices[i] = new Vector3(v.x, v.y, -v.z);
            }

            if (normals != null)
            {
                for (int i = 0; i < normals.Count; i++)
                {
                    var n = normals[i];
                    normals[i] = new Vector3(n.x, n.y, -n.z);
                }
            }

            for (int i = 0; i + 2 < indices.Count; i += 3)
            {
                int first = indices[i];
                indices[i] = indices[i + 2];
                indices[i + 2] = first;
            }
        }

        /// <summary>
        /// Replaces the normals with the area-weighted average of the faces meeting each vertex.
        ///
        /// For a surface built by differentiating a function, the normal is cross(du, dv), whose
        /// sign follows the parameterisation rather than the surface. Where the parameterisation
        /// folds back on itself - which most of the named parametric surfaces do somewhere - that
        /// sign inverts, and the affected region renders lit from behind: flat and dull with no
        /// specular falloff.
        ///
        /// Flipping the offending normals individually fixes the direction but leaves the field
        /// discontinuous, which shows up as a hard band wherever a flipped vertex neighbours an
        /// unflipped one. Deriving every normal from the faces instead is smooth and consistent
        /// with the winding by construction, which is what makes these shade like the analytic
        /// primitives rather than like paper.
        ///
        /// Cross products are left unnormalised on purpose: their magnitude is twice the triangle
        /// area, so larger faces weigh more, which is the standard way to keep a normal field
        /// stable across uneven tessellation.
        /// </summary>
        protected void RecomputeSmoothNormals()
        {
            if (vertices == null || indices == null)
            {
                return;
            }

            var accumulated = new Vector3[vertices.Count];

            for (int i = 0; i + 2 < indices.Count; i += 3)
            {
                int ia = indices[i], ib = indices[i + 1], ic = indices[i + 2];
                var face = Vector3.Cross(vertices[ib] - vertices[ia], vertices[ic] - vertices[ia]);
                accumulated[ia] += face;
                accumulated[ib] += face;
                accumulated[ic] += face;
            }

            if (normals == null)
            {
                normals = new List<Vector3>(vertices.Count);
            }
            while (normals.Count < vertices.Count)
            {
                normals.Add(Vector3.up);
            }

            for (int i = 0; i < vertices.Count; i++)
            {
                // Opposed faces cancel at a genuine crease. Keep whatever the builder had rather
                // than emitting a zero normal, which renders black.
                if (accumulated[i].sqrMagnitude > 1e-16f)
                {
                    normals[i] = accumulated[i].normalized;
                }
            }
        }

        /// <summary>
        /// Moves the mesh so its bounding box is centred on the origin, without changing its
        /// size. Shapes whose maths is not symmetric about the origin - the torus knot, whose
        /// curve is offset along x - need this to sit on their pivot.
        /// </summary>
        protected void Recentre()
        {
            if (!TryGetBounds(out var centre, out _))
            {
                return;
            }

            for (int i = 0; i < vertices.Count; i++)
            {
                vertices[i] = IsFinite(vertices[i]) ? vertices[i] - centre : Vector3.zero;
            }
        }

        /// <summary>
        /// Recentres the mesh on its bounding box and scales it uniformly so its largest extent
        /// is 1m. For shapes that expose no size parameters - the parametric surfaces, which
        /// range from roughly 4m to 15m across - this is the only way to reach a sane default
        /// size. Uniform scale plus translation leaves normals valid, so they are untouched.
        /// </summary>
        protected void FitToUnitCube()
        {
            if (!TryGetBounds(out var centre, out var size))
            {
                return;
            }

            var extent = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            var scale = extent > 1e-6f ? 1f / extent : 1f;

            for (int i = 0; i < vertices.Count; i++)
            {
                var v = vertices[i];
                vertices[i] = IsFinite(v) ? (v - centre) * scale : Vector3.zero;
            }
        }

        /// <summary>
        /// Bounding box over the finite vertices only. Several surface functions go non-finite at
        /// their domain edges (Apple and Scherk both take a log); one NaN would poison the bounds
        /// and lose the entire mesh, so they are excluded here and flattened to the centre by the
        /// caller.
        /// </summary>
        bool TryGetBounds(out Vector3 centre, out Vector3 size)
        {
            centre = Vector3.zero;
            size = Vector3.zero;

            if (vertices == null || vertices.Count == 0)
            {
                return false;
            }

            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            bool any = false;

            for (int i = 0; i < vertices.Count; i++)
            {
                var v = vertices[i];
                if (!IsFinite(v))
                {
                    continue;
                }
                min = Vector3.Min(min, v);
                max = Vector3.Max(max, v);
                any = true;
            }

            if (!any)
            {
                return false;
            }

            centre = (min + max) * 0.5f;
            size = max - min;
            return true;
        }

        static bool IsFinite(Vector3 v)
        {
            return !float.IsNaN(v.x) && !float.IsInfinity(v.x)
                && !float.IsNaN(v.y) && !float.IsInfinity(v.y)
                && !float.IsNaN(v.z) && !float.IsInfinity(v.z);
        }
    }
}
