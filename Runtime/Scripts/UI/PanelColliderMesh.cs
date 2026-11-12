using UnityEngine;
using UnityEngine.Rendering;

namespace BS
{
    /// <summary>
    /// Builds the collider mesh a panel needs to turn a pointer ray into a texture UV.
    /// </summary>
    /// <remarks>
    /// Raycasts are what produce <c>RaycastHit.textureCoord</c>, and they ignore back-facing
    /// triangles unless <c>Physics.queriesHitBackfaces</c> is on — which it is not in this project.
    /// A panel surface is normally viewed from its concave side (the mesh is wound outward and
    /// drawn with <c>MaterialSide.Back</c>), so a ray from the viewer would hit nothing at all and
    /// no UV would ever be produced.
    ///
    /// Emitting every triangle in both windings makes the surface hittable from either side without
    /// changing the global physics setting, and without caring how the source mesh was authored.
    /// The vertices are shared with the original, so UVs — and therefore the hit coordinate — are
    /// identical.
    /// </remarks>
    public static class PanelColliderMesh
    {
        /// <summary>
        /// Returns a new mesh with the source geometry wound both ways, or null if the source
        /// cannot produce texture coordinates.
        /// </summary>
        public static Mesh BuildTwoSided(Mesh source)
        {
            if (source == null) return null;

            // Reading geometry off a mesh imported with Read/Write disabled throws. Runtime-built
            // meshes are always readable, so this only rules out authored assets that were never
            // going to work as a pointer target anyway.
            if (!source.isReadable) return null;

            var vertices = source.vertices;
            if (vertices.Length == 0) return null;

            // Without UVs there is nothing to map a hit onto.
            var uvs = source.uv;
            if (uvs == null || uvs.Length != vertices.Length) return null;

            // The back-facing copy gets its own vertices rather than re-indexing the originals.
            // Sharing them would make every mirrored triangle the same vertex triple as one that
            // already exists, and PhysX cooks meshes with duplicate removal on by default — it
            // could quietly discard exactly the triangles this whole thing depends on. Distinct
            // indices put that out of reach. The copies carry the same UVs, so the hit coordinate
            // is identical whichever side is struck.
            var count = vertices.Length;

            var allVertices = new Vector3[count * 2];
            vertices.CopyTo(allVertices, 0);
            vertices.CopyTo(allVertices, count);

            var allUvs = new Vector2[count * 2];
            uvs.CopyTo(allUvs, 0);
            uvs.CopyTo(allUvs, count);

            // Reads across all submeshes; a collider only needs the one.
            var triangles = source.triangles;
            var doubled = new int[triangles.Length * 2];
            triangles.CopyTo(doubled, 0);

            for (var i = 0; i < triangles.Length; i += 3)
            {
                var mirrored = triangles.Length + i;
                doubled[mirrored] = triangles[i] + count;
                doubled[mirrored + 1] = triangles[i + 2] + count;
                doubled[mirrored + 2] = triangles[i + 1] + count;
            }

            var mesh = new Mesh { name = source.name + " (panel collider)" };
            if (allVertices.Length > ushort.MaxValue)
                mesh.indexFormat = IndexFormat.UInt32;

            mesh.vertices = allVertices;
            mesh.uv = allUvs;
            mesh.triangles = doubled;
            mesh.RecalculateBounds();

            return mesh;
        }
    }
}
