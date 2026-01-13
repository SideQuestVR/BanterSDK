using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Collections;
using Unity.Jobs;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Banter.SDK
{
    /// <summary>
    /// Merges child primitive objects, optionally subdivides, and bakes ambient occlusion
    /// into vertex colors using a depth-based GeoAO technique.
    /// </summary>
    [ExecuteInEditMode]
    public class PrimitiveMergerAO : MonoBehaviour
    {
        [Header("Source Objects")]
        [Tooltip("Use all children (recursive) with MeshFilter+MeshRenderer as source objects")]
        public bool useChildren = true;

        [Tooltip("Specific objects to merge (if useChildren is false)")]
        public Transform[] sourceObjects;

        [Header("Subdivision")]
        [Tooltip("Subdivision level: 0=none, 1=4x, 2=16x, 3=64x triangles")]
        [Range(0, 3)]
        public int subdivisionLevel = 0;

        [Tooltip("Recalculate normals after subdivision (disable for smooth primitives like spheres/cylinders)")]
        public bool recalculateNormals = false;

        [Header("AO Baking")]
        [Tooltip("Number of ray samples per vertex (higher = better quality, slower)")]
        [Range(16, 256)]
        public int sampleCount = 64;

        [Tooltip("How far to check for occlusion (0 = auto based on mesh size)")]
        public float aoRadius = 0f;

        [Tooltip("Intensity of the AO effect")]
        [Range(0f, 2f)]
        public float aoIntensity = 1f;

        [Tooltip("Bias to avoid self-intersection")]
        [Range(0.001f, 0.1f)]
        public float aoBias = 0.005f;

        [Header("Output")]
        [Tooltip("Shader to apply to the merged mesh (keeps original material properties)")]
        public Shader targetShader;

        [Tooltip("Hide source objects after merging")]
        public bool hideSourceObjects = true;

        // Internal state
        private MeshFilter _generatedMeshFilter;
        private MeshRenderer _generatedMeshRenderer;
        private Mesh _generatedMesh;

        // Async baking state
        private bool _isAsyncBaking = false;
        private AOBakeJobData _jobData;
        private GameObject _tempColliderObj;

        /// <summary>
        /// Is an async bake operation currently running?
        /// </summary>
        public bool IsAsyncBaking => _isAsyncBaking;

        /// <summary>
        /// Main entry point: Merge, subdivide, and bake AO
        /// </summary>
        [ContextMenu("Merge and Bake AO")]
        public void MergeAndBakeAO()
        {
            ValidateConfiguration();

            var totalTimer = Stopwatch.StartNew();
            var stepTimer = new Stopwatch();

            // Step 1: Collect source meshes
            stepTimer.Restart();
            var sources = CollectSourceMeshes();
            long collectMs = stepTimer.ElapsedMilliseconds;

            if (sources.Count == 0)
            {
                UnityEngine.Debug.LogError("No source meshes found!");
                return;
            }

            // Step 2: Merge meshes by material
            stepTimer.Restart();
            var meshGroups = MergeMeshesByMaterial(sources);
            long mergeMs = stepTimer.ElapsedMilliseconds;

            if (meshGroups.Count == 0)
            {
                UnityEngine.Debug.LogError("Failed to merge meshes!");
                return;
            }

            int totalPreSubdivVerts = 0;
            int totalPreSubdivTris = 0;
            long subdivideMs = 0;
            int totalPostSubdivVerts = 0;
            int totalPostSubdivTris = 0;

            // Step 3: Subdivide each group if requested
            foreach (var group in meshGroups)
            {
                totalPreSubdivVerts += group.mesh.vertexCount;
                totalPreSubdivTris += group.mesh.triangles.Length / 3;
            }

            if (subdivisionLevel > 0)
            {
                stepTimer.Restart();
                for (int i = 0; i < meshGroups.Count; i++)
                {
                    var group = meshGroups[i];
                    group.mesh = SubdivideMesh(group.mesh, subdivisionLevel);
                    meshGroups[i] = group;
                }
                subdivideMs = stepTimer.ElapsedMilliseconds;
            }

            // Step 4: Bake AO into vertex colors for each group
            stepTimer.Restart();
            foreach (var group in meshGroups)
            {
                BakeAO(group.mesh);
                totalPostSubdivVerts += group.mesh.vertexCount;
                totalPostSubdivTris += group.mesh.triangles.Length / 3;
            }
            long bakeMs = stepTimer.ElapsedMilliseconds;

            // Step 5: Apply all mesh groups
            ApplyGeneratedMeshGroups(meshGroups);

            // Step 6: Hide source objects
            if (hideSourceObjects)
            {
                HideSourceObjects(sources);
            }

            totalTimer.Stop();

            // Performance report
            int totalRaycasts = totalPostSubdivVerts * sampleCount;
            string report = $"=== PrimitiveMergerAO Complete ===\n" +
                $"Sources: {sources.Count} meshes → {meshGroups.Count} material groups\n" +
                $"Vertices: {totalPreSubdivVerts} → {totalPostSubdivVerts} (subdiv {subdivisionLevel})\n" +
                $"Triangles: {totalPreSubdivTris} → {totalPostSubdivTris}\n" +
                $"Raycasts: {totalRaycasts:N0} ({sampleCount} samples × {totalPostSubdivVerts} verts)\n" +
                $"---\n" +
                $"Collect: {collectMs}ms | Merge: {mergeMs}ms | Subdivide: {subdivideMs}ms | Bake AO: {bakeMs}ms\n" +
                $"Total: {totalTimer.ElapsedMilliseconds}ms";

            UnityEngine.Debug.Log(report);
        }

        /// <summary>
        /// Preview merge without baking AO
        /// </summary>
        [ContextMenu("Preview Merge (No AO)")]
        public void PreviewMerge()
        {
            ValidateConfiguration();

            var sources = CollectSourceMeshes();
            if (sources.Count == 0)
            {
                UnityEngine.Debug.LogError("No source meshes found!");
                return;
            }

            var meshGroups = MergeMeshesByMaterial(sources);
            if (meshGroups.Count == 0) return;

            int totalVerts = 0;
            for (int i = 0; i < meshGroups.Count; i++)
            {
                var group = meshGroups[i];

                if (subdivisionLevel > 0)
                {
                    group.mesh = SubdivideMesh(group.mesh, subdivisionLevel);
                    meshGroups[i] = group;
                }

                // Set white vertex colors (no AO)
                Color[] colors = new Color[group.mesh.vertexCount];
                for (int j = 0; j < colors.Length; j++)
                    colors[j] = Color.white;
                group.mesh.colors = colors;

                totalVerts += group.mesh.vertexCount;
            }

            ApplyGeneratedMeshGroups(meshGroups);

            UnityEngine.Debug.Log($"Preview: {sources.Count} meshes → {meshGroups.Count} material groups, {totalVerts} total vertices");
        }

        /// <summary>
        /// Clear the generated mesh(es)
        /// </summary>
        [ContextMenu("Clear Generated Mesh")]
        public void ClearGeneratedMesh()
        {
            ClearExistingGeneratedMeshes();

            // Show source objects again
            var sources = CollectSourceMeshes();
            foreach (var source in sources)
            {
                source.renderer.enabled = true;
            }
        }

        #region Mesh Collection

        private struct SourceMesh
        {
            public Mesh mesh;
            public Matrix4x4 transform;
            public MeshRenderer renderer;
            public Material material;
        }

        /// <summary>
        /// Represents a merged mesh group with its associated material
        /// </summary>
        private struct MergedMeshGroup
        {
            public Mesh mesh;
            public Material material;
        }

        private List<SourceMesh> CollectSourceMeshes()
        {
            var sources = new List<SourceMesh>();

            if (useChildren)
            {
                // Recursively find all MeshFilters in children (includes deeply nested objects)
                var meshFilters = GetComponentsInChildren<MeshFilter>(true);

                foreach (var mf in meshFilters)
                {
                    // Skip self (this GameObject)
                    if (mf.transform == transform) continue;

                    // Skip the generated mesh object(s)
                    if (mf.transform.name.StartsWith("_GeneratedMesh")) continue;

                    // Must have both MeshFilter with mesh AND MeshRenderer
                    var mr = mf.GetComponent<MeshRenderer>();
                    if (mf.sharedMesh != null && mr != null)
                    {
                        sources.Add(new SourceMesh
                        {
                            mesh = mf.sharedMesh,
                            transform = mf.transform.localToWorldMatrix,
                            renderer = mr,
                            material = mr.sharedMaterial
                        });
                    }
                }
            }
            else if (sourceObjects != null)
            {
                foreach (var obj in sourceObjects)
                {
                    if (obj == null) continue;
                    var mf = obj.GetComponent<MeshFilter>();
                    var mr = obj.GetComponent<MeshRenderer>();
                    if (mf != null && mf.sharedMesh != null && mr != null)
                    {
                        sources.Add(new SourceMesh
                        {
                            mesh = mf.sharedMesh,
                            transform = obj.localToWorldMatrix,
                            renderer = mr,
                            material = mr.sharedMaterial
                        });
                    }
                }
            }

            return sources;
        }

        /// <summary>
        /// Gets the primary material from source meshes (uses first valid material)
        /// </summary>
        private Material GetPrimaryMaterial(List<SourceMesh> sources)
        {
            foreach (var source in sources)
            {
                if (source.material != null)
                    return source.material;
            }
            return null;
        }

        /// <summary>
        /// Groups source meshes by their material for separate merging
        /// </summary>
        private Dictionary<Material, List<SourceMesh>> GroupSourcesByMaterial(List<SourceMesh> sources)
        {
            var groups = new Dictionary<Material, List<SourceMesh>>();

            foreach (var source in sources)
            {
                Material mat = source.material;

                if (!groups.ContainsKey(mat))
                {
                    groups[mat] = new List<SourceMesh>();
                }
                groups[mat].Add(source);
            }

            return groups;
        }

        /// <summary>
        /// Merges source meshes grouped by material, returns list of merged mesh groups
        /// </summary>
        private List<MergedMeshGroup> MergeMeshesByMaterial(List<SourceMesh> sources)
        {
            var groups = GroupSourcesByMaterial(sources);
            var results = new List<MergedMeshGroup>();

            foreach (var kvp in groups)
            {
                Mesh merged = MergeMeshes(kvp.Value);
                if (merged != null)
                {
                    results.Add(new MergedMeshGroup
                    {
                        mesh = merged,
                        material = kvp.Key
                    });
                }
            }

            return results;
        }

        #endregion

        #region Mesh Merging

        private Mesh MergeMeshes(List<SourceMesh> sources)
        {
            var combineInstances = new CombineInstance[sources.Count];
            Matrix4x4 parentInverse = transform.worldToLocalMatrix;

            for (int i = 0; i < sources.Count; i++)
            {
                combineInstances[i].mesh = sources[i].mesh;
                // Transform to local space of this GameObject
                combineInstances[i].transform = parentInverse * sources[i].transform;
            }

            Mesh merged = new Mesh();
            merged.name = "MergedPrimitives";
            merged.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32; // Support large meshes
            merged.CombineMeshes(combineInstances, true, true);

            if (recalculateNormals)
            {
                merged.RecalculateNormals();
            }
            merged.RecalculateBounds();

            return merged;
        }

        #endregion

        #region Mesh Subdivision

        private Mesh SubdivideMesh(Mesh source, int levels)
        {
            Mesh result = source;

            for (int level = 0; level < levels; level++)
            {
                result = SubdivideOnce(result);
            }

            return result;
        }

        private Mesh SubdivideOnce(Mesh source)
        {
            Vector3[] oldVerts = source.vertices;
            Vector3[] oldNormals = source.normals;
            int[] oldTris = source.triangles;

            // Dictionary to track edge midpoints (avoid duplicates)
            var edgeMidpoints = new Dictionary<long, int>();
            var newVerts = new List<Vector3>(oldVerts);
            var newNormals = new List<Vector3>(oldNormals);
            var newTris = new List<int>();

            // For each triangle, create 4 new triangles
            for (int i = 0; i < oldTris.Length; i += 3)
            {
                int v0 = oldTris[i];
                int v1 = oldTris[i + 1];
                int v2 = oldTris[i + 2];

                // Get or create midpoint vertices
                int m01 = GetOrCreateMidpoint(v0, v1, oldVerts, oldNormals, newVerts, newNormals, edgeMidpoints);
                int m12 = GetOrCreateMidpoint(v1, v2, oldVerts, oldNormals, newVerts, newNormals, edgeMidpoints);
                int m20 = GetOrCreateMidpoint(v2, v0, oldVerts, oldNormals, newVerts, newNormals, edgeMidpoints);

                // Create 4 new triangles
                // Triangle 0: v0, m01, m20
                newTris.Add(v0); newTris.Add(m01); newTris.Add(m20);
                // Triangle 1: m01, v1, m12
                newTris.Add(m01); newTris.Add(v1); newTris.Add(m12);
                // Triangle 2: m20, m12, v2
                newTris.Add(m20); newTris.Add(m12); newTris.Add(v2);
                // Triangle 3: m01, m12, m20 (center)
                newTris.Add(m01); newTris.Add(m12); newTris.Add(m20);
            }

            Mesh result = new Mesh();
            result.name = source.name + "_Subdivided";
            result.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            result.SetVertices(newVerts);
            result.SetNormals(newNormals);
            result.SetTriangles(newTris, 0);

            if (recalculateNormals)
            {
                result.RecalculateNormals();
            }
            result.RecalculateBounds();

            return result;
        }

        private int GetOrCreateMidpoint(int v0, int v1, Vector3[] oldVerts, Vector3[] oldNormals,
            List<Vector3> newVerts, List<Vector3> newNormals, Dictionary<long, int> edgeMidpoints)
        {
            // Create unique key for this edge (order-independent)
            int minV = Mathf.Min(v0, v1);
            int maxV = Mathf.Max(v0, v1);
            long key = ((long)minV << 32) | (uint)maxV;

            if (edgeMidpoints.TryGetValue(key, out int existingIndex))
            {
                return existingIndex;
            }

            // Create new midpoint vertex
            Vector3 midPos = (oldVerts[v0] + oldVerts[v1]) * 0.5f;
            Vector3 midNormal = ((oldNormals.Length > v0 && oldNormals.Length > v1)
                ? (oldNormals[v0] + oldNormals[v1]).normalized
                : Vector3.up);

            int newIndex = newVerts.Count;
            newVerts.Add(midPos);
            newNormals.Add(midNormal);
            edgeMidpoints[key] = newIndex;

            return newIndex;
        }

        #endregion

        #region AO Baking (Raycast-based)

        private void BakeAO(Mesh mesh)
        {
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Color[] colors = new Color[vertices.Length];

            // Create temporary collider for raycasting
            GameObject tempObj = new GameObject("_AOBakeCollider");
            tempObj.transform.SetPositionAndRotation(transform.position, transform.rotation);
            tempObj.transform.localScale = transform.lossyScale;
            var meshCollider = tempObj.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = mesh;

            // Wait for physics to update
            Physics.SyncTransforms();

            // Calculate AO radius (auto or manual)
            Bounds bounds = mesh.bounds;
            float effectiveRadius = aoRadius > 0 ? aoRadius : bounds.extents.magnitude * 0.5f;

            // Pre-generate hemisphere sample directions (reused for all vertices)
            // We'll rotate these to align with each vertex's normal
            var baseSamples = GenerateBaseHemisphereSamples(sampleCount);

            // Process each vertex
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 worldVert = transform.TransformPoint(vertices[i]);
                Vector3 worldNormal = transform.TransformDirection(normals[i]).normalized;

                // Offset slightly along normal to avoid self-intersection
                Vector3 origin = worldVert + worldNormal * aoBias;

                // Generate sample directions aligned with this vertex's normal
                var sampleDirs = AlignSamplesToNormal(baseSamples, worldNormal);

                int hitCount = 0;
                foreach (var dir in sampleDirs)
                {
                    // Cast ray and check for hits
                    if (Physics.Raycast(origin, dir, effectiveRadius))
                    {
                        hitCount++;
                    }
                }

                // Calculate AO value (more hits = more occlusion = darker)
                float occlusion = (float)hitCount / sampleCount;
                float ao = 1f - Mathf.Clamp01(occlusion * aoIntensity);
                colors[i] = new Color(ao, ao, ao, 1f);
            }

            // Cleanup
            DestroyImmediate(tempObj);

            // Apply colors to mesh
            mesh.colors = colors;
        }

        /// <summary>
        /// Async version: Merge, subdivide, and bake AO without blocking the main thread.
        /// Uses Unity Job System with RaycastCommand for parallel processing.
        /// </summary>
        public IEnumerator BakeAOAsync(System.Action onComplete = null)
        {
            if (_isAsyncBaking)
            {
                UnityEngine.Debug.LogWarning($"[{name}] Async bake already in progress!");
                yield break;
            }

            ValidateConfiguration();

            _isAsyncBaking = true;
            var totalTimer = Stopwatch.StartNew();

            // Step 1: Collect and merge by material (must be on main thread)
            var sources = CollectSourceMeshes();
            if (sources.Count == 0)
            {
                UnityEngine.Debug.LogError($"[{name}] No source meshes found!");
                _isAsyncBaking = false;
                yield break;
            }

            var meshGroups = MergeMeshesByMaterial(sources);
            if (meshGroups.Count == 0)
            {
                UnityEngine.Debug.LogError($"[{name}] Failed to merge meshes!");
                _isAsyncBaking = false;
                yield break;
            }

            // Step 2: Subdivide each group if requested
            if (subdivisionLevel > 0)
            {
                for (int i = 0; i < meshGroups.Count; i++)
                {
                    var group = meshGroups[i];
                    group.mesh = SubdivideMesh(group.mesh, subdivisionLevel);
                    meshGroups[i] = group;
                }
            }

            // Step 3: Async AO baking for each group
            int totalVerts = 0;
            foreach (var group in meshGroups)
            {
                yield return StartCoroutine(BakeAOAsyncInternal(group.mesh));
                totalVerts += group.mesh.vertexCount;
            }

            // Step 4: Apply all mesh groups
            ApplyGeneratedMeshGroups(meshGroups);

            if (hideSourceObjects)
            {
                HideSourceObjects(sources);
            }

            totalTimer.Stop();

            int totalRaycasts = totalVerts * sampleCount;
            UnityEngine.Debug.Log($"[{name}] Async bake complete: {meshGroups.Count} groups, {totalVerts} verts, {totalRaycasts:N0} raycasts, {totalTimer.ElapsedMilliseconds}ms total");

            _isAsyncBaking = false;
            onComplete?.Invoke();
        }

        /// <summary>
        /// Internal async AO baking using Job System and RaycastCommand
        /// </summary>
        private IEnumerator BakeAOAsyncInternal(Mesh mesh)
        {
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            int vertexCount = vertices.Length;
            int totalRays = vertexCount * sampleCount;

            // Create temporary collider for raycasting
            _tempColliderObj = new GameObject("_AOBakeCollider_Async");
            _tempColliderObj.transform.SetPositionAndRotation(transform.position, transform.rotation);
            _tempColliderObj.transform.localScale = transform.lossyScale;
            var meshCollider = _tempColliderObj.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = mesh;

            Physics.SyncTransforms();

            // Allow physics to settle
            yield return new WaitForFixedUpdate();

            // Calculate AO radius
            Bounds bounds = mesh.bounds;
            float effectiveRadius = aoRadius > 0 ? aoRadius : bounds.extents.magnitude * 0.5f;

            // Allocate NativeArrays
            _jobData = new AOBakeJobData();
            _jobData.Allocate(vertexCount, sampleCount);

            // Populate world vertices and normals
            for (int i = 0; i < vertexCount; i++)
            {
                _jobData.WorldVertices[i] = transform.TransformPoint(vertices[i]);
                _jobData.WorldNormals[i] = transform.TransformDirection(normals[i]).normalized;
            }

            // Generate hemisphere samples
            var baseSamples = GenerateBaseHemisphereSamples(sampleCount);
            for (int i = 0; i < sampleCount; i++)
            {
                _jobData.HemisphereSamples[i] = baseSamples[i];
            }

            // Schedule prepare job
            var prepareJob = new PrepareAORaycastsJob
            {
                WorldVertices = _jobData.WorldVertices,
                WorldNormals = _jobData.WorldNormals,
                HemisphereSamples = _jobData.HemisphereSamples,
                AOBias = aoBias,
                AORadius = effectiveRadius,
                SampleCount = sampleCount,
                Commands = _jobData.RaycastCommands
            };

            JobHandle prepareHandle = prepareJob.Schedule(totalRays, 64);

            // Schedule raycast batch (depends on prepare job)
            JobHandle raycastHandle = RaycastCommand.ScheduleBatch(
                _jobData.RaycastCommands,
                _jobData.RaycastResults,
                32,
                prepareHandle
            );

            // Schedule result processing (depends on raycasts)
            var processJob = new ProcessAOResultsJob
            {
                RaycastResults = _jobData.RaycastResults,
                SampleCount = sampleCount,
                AOIntensity = aoIntensity,
                VertexColors = _jobData.VertexColors
            };

            JobHandle processHandle = processJob.Schedule(vertexCount, 64, raycastHandle);

            // Non-blocking wait for completion
            while (!processHandle.IsCompleted)
            {
                yield return null;
            }

            processHandle.Complete();

            // Copy results to mesh
            Color[] colors = new Color[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                var c = _jobData.VertexColors[i];
                colors[i] = new Color(c.r / 255f, c.g / 255f, c.b / 255f, 1f);
            }
            mesh.colors = colors;

            // Cleanup
            _jobData.Dispose();
            if (_tempColliderObj != null)
            {
                DestroyImmediate(_tempColliderObj);
                _tempColliderObj = null;
            }
        }

        /// <summary>
        /// Cancel any running async bake operation
        /// </summary>
        public void CancelAsyncBake()
        {
            if (!_isAsyncBaking) return;

            StopAllCoroutines();

            if (_jobData.IsCreated)
            {
                _jobData.Dispose();
            }

            if (_tempColliderObj != null)
            {
                DestroyImmediate(_tempColliderObj);
                _tempColliderObj = null;
            }

            _isAsyncBaking = false;
            UnityEngine.Debug.Log($"[{name}] Async bake cancelled");
        }

        private void OnDestroy()
        {
            CancelAsyncBake();
        }

        /// <summary>
        /// Generate evenly distributed sample directions in a hemisphere (pointing up)
        /// </summary>
        private List<Vector3> GenerateBaseHemisphereSamples(int count)
        {
            var samples = new List<Vector3>();

            // Fibonacci hemisphere sampling for even distribution
            float goldenRatio = (1f + Mathf.Sqrt(5f)) / 2f;

            for (int i = 0; i < count; i++)
            {
                // t goes from 0 to 1
                float t = (float)i / (count - 1);

                // For hemisphere, inclination goes from 0 (up) to PI/2 (horizontal)
                float inclination = Mathf.Acos(1f - t);
                float azimuth = 2f * Mathf.PI * goldenRatio * i;

                // Convert spherical to cartesian (Z-up hemisphere)
                float sinInc = Mathf.Sin(inclination);
                Vector3 dir = new Vector3(
                    sinInc * Mathf.Cos(azimuth),
                    sinInc * Mathf.Sin(azimuth),
                    Mathf.Cos(inclination)
                );

                samples.Add(dir.normalized);
            }

            return samples;
        }

        /// <summary>
        /// Rotate base hemisphere samples to align with a given normal direction
        /// </summary>
        private List<Vector3> AlignSamplesToNormal(List<Vector3> baseSamples, Vector3 normal)
        {
            var aligned = new List<Vector3>(baseSamples.Count);

            // Create rotation from Z-up to normal direction
            Quaternion rotation = Quaternion.FromToRotation(Vector3.forward, normal);

            foreach (var sample in baseSamples)
            {
                aligned.Add(rotation * sample);
            }

            return aligned;
        }

        #endregion

        #region Apply Generated Mesh

        /// <summary>
        /// Clears all existing generated mesh children
        /// </summary>
        private void ClearExistingGeneratedMeshes()
        {
            var children = new List<Transform>();
            foreach (Transform child in transform)
            {
                if (child.name.StartsWith("_GeneratedMesh"))
                {
                    children.Add(child);
                }
            }

            foreach (var child in children)
            {
                if (Application.isPlaying)
                    Destroy(child.gameObject);
                else
                    DestroyImmediate(child.gameObject);
            }

            _generatedMeshFilter = null;
            _generatedMeshRenderer = null;
            _generatedMesh = null;
        }

        /// <summary>
        /// Applies multiple merged mesh groups, creating child GameObjects for each
        /// </summary>
        private void ApplyGeneratedMeshGroups(List<MergedMeshGroup> meshGroups)
        {
            ClearExistingGeneratedMeshes();

            for (int i = 0; i < meshGroups.Count; i++)
            {
                var group = meshGroups[i];
                string childName = meshGroups.Count == 1 ? "_GeneratedMesh" : $"_GeneratedMesh_{i}";

                ApplyGeneratedMeshSingle(group.mesh, group.material, childName);
            }
        }

        /// <summary>
        /// Applies a single mesh with material handling
        /// </summary>
        private void ApplyGeneratedMeshSingle(Mesh mesh, Material sourceMaterial, string childName)
        {
            Transform existingChild = transform.Find(childName);
            GameObject meshObj;

            if (existingChild != null)
            {
                meshObj = existingChild.gameObject;
            }
            else
            {
                meshObj = new GameObject(childName);
                meshObj.transform.SetParent(transform, false);
                meshObj.transform.localPosition = Vector3.zero;
                meshObj.transform.localRotation = Quaternion.identity;
                meshObj.transform.localScale = Vector3.one;
            }

            var meshFilter = meshObj.GetComponent<MeshFilter>();
            if (meshFilter == null)
                meshFilter = meshObj.AddComponent<MeshFilter>();

            var meshRenderer = meshObj.GetComponent<MeshRenderer>();
            if (meshRenderer == null)
                meshRenderer = meshObj.AddComponent<MeshRenderer>();

            meshFilter.sharedMesh = mesh;

            // Apply material with shader swap logic
            if (targetShader != null && sourceMaterial != null)
            {
                // Create new material from source, swap shader only (preserves properties)
                Material newMaterial = new Material(sourceMaterial);
                newMaterial.shader = targetShader;
                meshRenderer.sharedMaterial = newMaterial;
            }
            else if (sourceMaterial != null)
            {
                // No target shader - use source material as-is
                meshRenderer.sharedMaterial = sourceMaterial;
            }

            // Track first mesh for backward compatibility
            if (_generatedMeshFilter == null)
            {
                _generatedMeshFilter = meshFilter;
                _generatedMeshRenderer = meshRenderer;
                _generatedMesh = mesh;
            }
        }

        /// <summary>
        /// Legacy single mesh apply (for backward compatibility)
        /// </summary>
        private void ApplyGeneratedMesh(Mesh mesh, Material sourceMaterial = null)
        {
            ClearExistingGeneratedMeshes();
            ApplyGeneratedMeshSingle(mesh, sourceMaterial, "_GeneratedMesh");
        }

        private void HideSourceObjects(List<SourceMesh> sources)
        {
            foreach (var source in sources)
            {
                if (source.renderer != null)
                {
                    source.renderer.enabled = false;
                }
            }
        }

        #endregion

        #region Validation

        /// <summary>
        /// Validates that the target shader supports vertex colors.
        /// Logs a warning if the shader may not use baked vertex colors.
        /// </summary>
        private bool ValidateShaderSupportsVertexColors(Shader shader)
        {
            if (shader == null) return false;

            // Heuristic: Check shader name for common vertex color indicators
            string shaderName = shader.name.ToLower();
            bool likelySupportsVertexColors =
                shaderName.Contains("vertexcolor") ||
                shaderName.Contains("vertex color") ||
                shaderName.Contains("baked") ||
                shaderName.Contains("ao") ||
                shaderName.Contains("stylized") ||
                shaderName.Contains("mobile/stylized");

            if (!likelySupportsVertexColors)
            {
                UnityEngine.Debug.LogWarning(
                    $"[PrimitiveMergerAO] Shader '{shader.name}' may not support vertex colors. " +
                    $"Baked AO may not be visible. Consider using a vertex color compatible shader.");
            }

            return true;
        }

        /// <summary>
        /// Validates configuration at the start of bake operations
        /// </summary>
        private void ValidateConfiguration()
        {
            if (targetShader != null)
            {
                ValidateShaderSupportsVertexColors(targetShader);
            }
            else
            {
                UnityEngine.Debug.LogWarning(
                    "[PrimitiveMergerAO] No target shader specified. " +
                    "Baked AO will use source material shaders which may not support vertex colors.");
            }
        }

        #endregion

        #region Editor Gizmos

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (_generatedMesh == null) return;

            // Draw bounds
            Bounds bounds = _generatedMesh.bounds;
            Gizmos.color = Color.cyan;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }
#endif

        #endregion
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(PrimitiveMergerAO))]
    public class PrimitiveMergerAOEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            PrimitiveMergerAO script = (PrimitiveMergerAO)target;

            EditorGUILayout.Space(10);

            // Main buttons
            EditorGUILayout.BeginHorizontal();

            GUI.enabled = !script.IsAsyncBaking;
            if (GUILayout.Button("Merge & Bake AO", GUILayout.Height(30)))
            {
                script.MergeAndBakeAO();
            }

            if (GUILayout.Button("Preview", GUILayout.Height(30)))
            {
                script.PreviewMerge();
            }

            if (GUILayout.Button("Clear", GUILayout.Height(30)))
            {
                script.ClearGeneratedMesh();
            }
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();

            // Async baking section
            EditorGUILayout.Space(5);

            if (Application.isPlaying)
            {
                EditorGUILayout.BeginHorizontal();

                if (script.IsAsyncBaking)
                {
                    EditorGUILayout.HelpBox("Async bake in progress...", MessageType.Info);
                    if (GUILayout.Button("Cancel", GUILayout.Width(60), GUILayout.Height(38)))
                    {
                        script.CancelAsyncBake();
                    }
                }
                else
                {
                    if (GUILayout.Button("Bake Async (Non-blocking)", GUILayout.Height(25)))
                    {
                        script.StartCoroutine(script.BakeAOAsync());
                    }
                }

                EditorGUILayout.EndHorizontal();

                // Batch baking
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Batch Processing", EditorStyles.boldLabel);

                EditorGUILayout.BeginHorizontal();

                if (GUILayout.Button("Queue This Object"))
                {
                    var manager = AOBakeManager.Instance;
                    if (manager == null)
                    {
                        var go = new GameObject("AOBakeManager");
                        manager = go.AddComponent<AOBakeManager>();
                    }
                    manager.QueueBake(script);
                }

                if (GUILayout.Button("Queue All Tagged"))
                {
                    var manager = AOBakeManager.Instance;
                    if (manager == null)
                    {
                        var go = new GameObject("AOBakeManager");
                        manager = go.AddComponent<AOBakeManager>();
                    }
                    manager.QueueAllTagged();
                }

                EditorGUILayout.EndHorizontal();

                if (AOBakeManager.Instance != null)
                {
                    var (pending, active, completed) = AOBakeManager.Instance.GetStatus();
                    string tag = AOBakeManager.Instance.bakeTag;
                    EditorGUILayout.HelpBox($"Tag: '{tag}' | Queue: {pending} pending, {active} active, {completed} completed", MessageType.None);
                }
                else
                {
                    EditorGUILayout.HelpBox("Uses tag 'Respawn' by default. Change in AOBakeManager.", MessageType.None);
                }
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Async & batch baking available in Play Mode.\n" +
                    "Use 'Merge & Bake AO' for editor baking.",
                    MessageType.Info);
            }

            EditorGUILayout.Space(5);

            EditorGUILayout.HelpBox(
                "1. Add primitive children to this object\n" +
                "2. Set subdivision level (0-3) for better AO\n" +
                "3. Assign a BakedAOStylized material\n" +
                "4. Click 'Merge & Bake AO'",
                MessageType.Info);
        }
    }
#endif
}
