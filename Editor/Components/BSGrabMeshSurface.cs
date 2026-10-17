using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

namespace BS.SDKEditor
{
    // Snapshot once when fitting; only the resulting finger angles are saved.
    internal sealed class BSGrabMeshSurface
    {
        struct Triangle
        {
            public Vector3 a, b, c;
            public Bounds bounds;
        }

        readonly List<Triangle> triangles = new List<Triangle>();
        readonly List<Collider> primitives = new List<Collider>();
        public bool IsEmpty => triangles.Count == 0 && primitives.Count == 0;

        internal BSGrabMeshSurface(Collider collider, Vector3 wrist)
            : this(collider.attachedRigidbody != null ? collider.attachedRigidbody.transform : collider.transform,
                collider.attachedRigidbody, true, wrist) { }

        internal BSGrabMeshSurface(GameObject target, Vector3 wrist)
            : this(target.transform, null, false, wrist) { }

        BSGrabMeshSurface() { }

        internal static BSGrabMeshSurface ForColliders(GameObject target, Vector3 wrist, Rigidbody owner = null, bool restrictToOwner = false)
        {
            var result = new BSGrabMeshSurface();
            var reach = new Bounds(wrist, Vector3.one * .6f);
            foreach (var collider in target.GetComponentsInChildren<Collider>())
            {
                if (!collider.enabled || restrictToOwner && collider.attachedRigidbody != owner || !reach.Intersects(collider.bounds)) continue;
                if (collider is MeshCollider mesh && !mesh.convex && mesh.sharedMesh != null)
                    result.Add(mesh.sharedMesh, mesh.transform.localToWorldMatrix, reach);
                else if (collider is BoxCollider || collider is SphereCollider || collider is CapsuleCollider
                    || collider is MeshCollider convex && convex.convex && convex.sharedMesh != null)
                    result.primitives.Add(collider);
            }
            return result;
        }

        BSGrabMeshSurface(Transform root, Rigidbody body, bool filterBodies, Vector3 wrist)
        {
            var reach = new Bounds(wrist, Vector3.one * .6f);
            var lowerLods = new HashSet<Renderer>();
            foreach (var group in root.GetComponentsInChildren<LODGroup>())
            {
                var lods = group.GetLODs();
                for (var i = 1; i < lods.Length; i++)
                    foreach (var visual in lods[i].renderers) lowerLods.Add(visual);
                if (lods.Length > 0)
                    foreach (var visual in lods[0].renderers) lowerLods.Remove(visual);
            }
            foreach (var visual in root.GetComponentsInChildren<Renderer>())
            {
                if (!visual.enabled || lowerLods.Contains(visual)
                    || filterBodies && visual.GetComponentInParent<Rigidbody>() != body || !reach.Intersects(visual.bounds)) continue;
                if (visual is SkinnedMeshRenderer skin && skin.sharedMesh != null)
                {
                    var baked = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                    try
                    {
                        skin.BakeMesh(baked);
                        Add(baked, skin.transform.localToWorldMatrix, reach);
                    }
                    finally { Object.DestroyImmediate(baked); }
                }
                else if (visual is MeshRenderer)
                {
                    var filter = visual.GetComponent<MeshFilter>();
                    if (filter != null && filter.sharedMesh != null)
                        Add(filter.sharedMesh, visual.transform.localToWorldMatrix, reach);
                }
            }
        }

        void Add(Mesh mesh, Matrix4x4 matrix, Bounds reach)
        {
            // Editor snapshot also handles imported meshes with Read/Write disabled.
            using (var snapshot = MeshUtility.AcquireReadOnlyMeshData(mesh))
            using (var vertices = new NativeArray<Vector3>(snapshot[0].vertexCount, Allocator.Temp))
            {
                var data = snapshot[0];
                data.GetVertices(vertices);
                var points = vertices.ToArray();
                for (var i = 0; i < points.Length; i++) points[i] = matrix.MultiplyPoint3x4(points[i]);
                for (var sub = 0; sub < data.subMeshCount; sub++)
                {
                    var descriptor = data.GetSubMesh(sub);
                    if (descriptor.topology != MeshTopology.Triangles) continue;
                    using (var indices = new NativeArray<int>(descriptor.indexCount, Allocator.Temp))
                    {
                        data.GetIndices(indices, sub);
                        for (var i = 0; i + 2 < indices.Length; i += 3)
                        {
                            var triangle = new Triangle { a = points[indices[i]], b = points[indices[i + 1]], c = points[indices[i + 2]] };
                            triangle.bounds = new Bounds(triangle.a, Vector3.zero);
                            triangle.bounds.Encapsulate(triangle.b);
                            triangle.bounds.Encapsulate(triangle.c);
                            if (reach.Intersects(triangle.bounds)) triangles.Add(triangle);
                        }
                    }
                }
            }
        }

        internal bool Touches(Vector3 point, float radius)
        {
            var radiusSquared = radius * radius;
            foreach (var collider in primitives)
                if ((point - collider.ClosestPoint(point)).sqrMagnitude <= radiusSquared) return true;
            foreach (var triangle in triangles)
            {
                if (triangle.bounds.SqrDistance(point) > radiusSquared) continue;
                var ab = triangle.b - triangle.a;
                var ac = triangle.c - triangle.a;
                var normal = Vector3.Cross(ab, ac);
                var normalSquared = normal.sqrMagnitude;
                if (normalSquared > 1e-20f)
                {
                    var projected = point - normal * (Vector3.Dot(point - triangle.a, normal) / normalSquared);
                    if (Vector3.Dot(Vector3.Cross(ab, projected - triangle.a), normal) >= 0f
                        && Vector3.Dot(Vector3.Cross(triangle.c - triangle.b, projected - triangle.b), normal) >= 0f
                        && Vector3.Dot(Vector3.Cross(-ac, projected - triangle.c), normal) >= 0f
                        && (point - projected).sqrMagnitude <= radiusSquared) return true;
                }
                if (SegmentDistanceSquared(point, triangle.a, triangle.b) <= radiusSquared
                    || SegmentDistanceSquared(point, triangle.b, triangle.c) <= radiusSquared
                    || SegmentDistanceSquared(point, triangle.c, triangle.a) <= radiusSquared) return true;
            }
            return false;
        }

        static float SegmentDistanceSquared(Vector3 point, Vector3 from, Vector3 to)
        {
            var edge = to - from;
            var t = edge.sqrMagnitude > 1e-20f ? Mathf.Clamp01(Vector3.Dot(point - from, edge) / edge.sqrMagnitude) : 0f;
            return (point - from - edge * t).sqrMagnitude;
        }
    }
}
