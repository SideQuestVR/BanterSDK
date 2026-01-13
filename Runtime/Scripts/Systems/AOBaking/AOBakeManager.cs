using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace Banter.SDK
{
    /// <summary>
    /// Manages batch AO baking with distance-based priority.
    /// Queues PrimitiveMergerAO objects and processes them without blocking the main thread.
    /// </summary>
    public class AOBakeManager : MonoBehaviour
    {
        public static AOBakeManager Instance { get; private set; }

        [Header("Settings")]
        [Tooltip("Reference to player/camera for distance calculations")]
        public Transform playerTransform;

        [Tooltip("Tag to identify objects that should have their children baked")]
        public string bakeTag = "Respawn";

        [Header("Bake Settings (applied to auto-created components)")]
        public int subdivisionLevel = 1;
        public int sampleCount = 64;
        public float aoIntensity = 1f;

        [Tooltip("Shader to apply to merged meshes (keeps original material properties)")]
        public Shader targetShader;

        /// <summary>
        /// Simple entry point: Call this to bake all objects with the configured tag
        /// </summary>
        public static void BakeAll()
        {
            var manager = GetOrCreateInstance();
            manager.QueueAllTagged();
        }

        /// <summary>
        /// Bake all objects with a specific tag
        /// </summary>
        public static void BakeAllWithTag(string tag)
        {
            var manager = GetOrCreateInstance();
            manager.QueueAllByTag(tag);
        }

        private static AOBakeManager GetOrCreateInstance()
        {
            if (Instance != null) return Instance;

            var go = new GameObject("AOBakeManager");
            return go.AddComponent<AOBakeManager>();
        }

        [Tooltip("Maximum concurrent bake operations")]
        [Range(1, 16)]
        public int maxConcurrentBakes = 4;

        [Tooltip("How often to re-sort queue by distance (seconds)")]
        public float reprioritizeInterval = 0.5f;

        [Tooltip("Auto-start baking when objects are queued")]
        public bool autoStart = true;

        [Header("Status")]
        [SerializeField] private int pendingCount;
        [SerializeField] private int activeCount;
        [SerializeField] private int completedCount;

        // Diagnostics
        private System.Diagnostics.Stopwatch _totalTimer;
        private int _totalObjects;
        private int _totalVertices;
        private int _totalTriangles;
        private int _totalRaycasts;

        // Internal queue management
        private List<AOBakeRequest> _pendingRequests = new List<AOBakeRequest>();
        private List<AOBakeRequest> _activeRequests = new List<AOBakeRequest>();
        private Coroutine _reprioritizeCoroutine;
        private bool _isProcessing = false;

        public struct AOBakeRequest
        {
            public PrimitiveMergerAO Target;
            public float Priority; // Lower = higher priority (distance)
            public System.Action OnComplete;
        }

        // Events
        public event System.Action<PrimitiveMergerAO> OnBakeStarted;
        public event System.Action<PrimitiveMergerAO> OnBakeCompleted;
        public event System.Action OnAllBakesCompleted;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            // playerTransform can be left unassigned - will use Vector3.zero for distance calculations
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        /// <summary>
        /// Queue a PrimitiveMergerAO for async baking
        /// </summary>
        public void QueueBake(PrimitiveMergerAO target, System.Action onComplete = null)
        {
            if (target == null) return;

            // Check if already queued or active
            if (_pendingRequests.Exists(r => r.Target == target) ||
                _activeRequests.Exists(r => r.Target == target))
            {
                Debug.LogWarning($"[AOBakeManager] {target.name} is already queued or baking");
                return;
            }

            float distance = GetDistanceToPlayer(target.transform);

            _pendingRequests.Add(new AOBakeRequest
            {
                Target = target,
                Priority = distance,
                OnComplete = onComplete
            });

            // Sort by priority (closest first)
            _pendingRequests.Sort((a, b) => a.Priority.CompareTo(b.Priority));
            pendingCount = _pendingRequests.Count;

            Debug.Log($"[AOBakeManager] Queued {target.name} (distance: {distance:F1}, queue position: {_pendingRequests.Count})");

            if (autoStart && !_isProcessing)
            {
                StartProcessing();
            }
        }

        /// <summary>
        /// Queue multiple objects at once
        /// </summary>
        public void QueueBakeAll(IEnumerable<PrimitiveMergerAO> targets)
        {
            foreach (var target in targets)
            {
                QueueBake(target);
            }
        }

        /// <summary>
        /// Queue all PrimitiveMergerAO in scene
        /// </summary>
        public void QueueAllInScene()
        {
            var allMergers = FindObjectsOfType<PrimitiveMergerAO>();
            QueueBakeAll(allMergers);
            Debug.Log($"[AOBakeManager] Queued {allMergers.Length} objects from scene");
        }

        /// <summary>
        /// Queue all objects with the specified tag for baking.
        /// Creates PrimitiveMergerAO components if needed.
        /// </summary>
        public void QueueAllByTag(string tag = null)
        {
            string targetTag = tag ?? bakeTag;
            var taggedObjects = GameObject.FindGameObjectsWithTag(targetTag);

            if (taggedObjects.Length == 0)
            {
                Debug.LogWarning($"[AOBakeManager] No objects found with tag '{targetTag}'");
                return;
            }

            // Reset diagnostics
            _totalTimer = System.Diagnostics.Stopwatch.StartNew();
            _totalObjects = 0;
            _totalVertices = 0;
            _totalTriangles = 0;
            _totalRaycasts = 0;
            completedCount = 0;

            int queued = 0;
            foreach (var go in taggedObjects)
            {
                // Check if it has children with meshes
                var meshFilters = go.GetComponentsInChildren<MeshFilter>(true);
                bool hasChildMeshes = false;
                foreach (var mf in meshFilters)
                {
                    if (mf.transform != go.transform && mf.sharedMesh != null)
                    {
                        var mr = mf.GetComponent<MeshRenderer>();
                        if (mr != null)
                        {
                            hasChildMeshes = true;
                            break;
                        }
                    }
                }

                if (!hasChildMeshes)
                {
                    Debug.Log($"[AOBakeManager] Skipping '{go.name}' - no child meshes found");
                    continue;
                }

                // Get or add PrimitiveMergerAO component
                var merger = go.GetComponent<PrimitiveMergerAO>();
                if (merger == null)
                {
                    merger = go.AddComponent<PrimitiveMergerAO>();
                    merger.useChildren = true;
                    merger.hideSourceObjects = true;
                    merger.subdivisionLevel = subdivisionLevel;
                    merger.sampleCount = sampleCount;
                    merger.aoIntensity = aoIntensity;
                    if (targetShader != null)
                        merger.targetShader = targetShader;
                    Debug.Log($"[AOBakeManager] Added PrimitiveMergerAO to '{go.name}'");
                }

                QueueBake(merger);
                queued++;
            }

            Debug.Log($"[AOBakeManager] Queued {queued} tagged objects (tag: '{targetTag}')");
        }

        /// <summary>
        /// Queue all objects with the default bake tag
        /// </summary>
        public void QueueAllTagged()
        {
            QueueAllByTag(bakeTag);
        }

        /// <summary>
        /// Simple method to start baking all tagged objects.
        /// Same as QueueAllTagged() but with a clearer name.
        /// </summary>
        public void StartBaking()
        {
            QueueAllTagged();
        }

        /// <summary>
        /// Start processing the queue
        /// </summary>
        public void StartProcessing()
        {
            if (_isProcessing) return;

            _isProcessing = true;
            StartCoroutine(ProcessQueueCoroutine());

            if (reprioritizeInterval > 0)
            {
                _reprioritizeCoroutine = StartCoroutine(ReprioritizeCoroutine());
            }
        }

        /// <summary>
        /// Stop processing (current bakes will complete)
        /// </summary>
        public void StopProcessing()
        {
            _isProcessing = false;

            if (_reprioritizeCoroutine != null)
            {
                StopCoroutine(_reprioritizeCoroutine);
                _reprioritizeCoroutine = null;
            }
        }

        /// <summary>
        /// Clear all pending requests (active bakes continue)
        /// </summary>
        public void ClearQueue()
        {
            _pendingRequests.Clear();
            pendingCount = 0;
            Debug.Log("[AOBakeManager] Queue cleared");
        }

        private IEnumerator ProcessQueueCoroutine()
        {
            while (_isProcessing)
            {
                // Start new bakes if slots available
                while (_activeRequests.Count < maxConcurrentBakes && _pendingRequests.Count > 0)
                {
                    var request = _pendingRequests[0];
                    _pendingRequests.RemoveAt(0);
                    pendingCount = _pendingRequests.Count;

                    if (request.Target == null)
                        continue;

                    _activeRequests.Add(request);
                    activeCount = _activeRequests.Count;

                    StartCoroutine(ProcessBakeRequest(request));
                }

                // Check if all done
                if (_pendingRequests.Count == 0 && _activeRequests.Count == 0)
                {
                    _isProcessing = false;
                    _totalTimer?.Stop();

                    // Print final summary
                    string summary = $"=== AOBakeManager Complete ===\n" +
                        $"Objects: {_totalObjects}\n" +
                        $"Total Vertices: {_totalVertices:N0}\n" +
                        $"Total Triangles: {_totalTriangles:N0}\n" +
                        $"Total Raycasts: {_totalRaycasts:N0}\n" +
                        $"---\n" +
                        $"Total Time: {_totalTimer?.ElapsedMilliseconds ?? 0}ms ({(_totalTimer?.ElapsedMilliseconds ?? 0) / 1000f:F1}s)";

                    Debug.Log(summary);

                    OnAllBakesCompleted?.Invoke();
                    yield break;
                }

                yield return null;
            }
        }

        private IEnumerator ProcessBakeRequest(AOBakeRequest request)
        {
            OnBakeStarted?.Invoke(request.Target);
            Debug.Log($"[AOBakeManager] Starting bake: {request.Target.name}");

            // Run the async bake
            yield return request.Target.StartCoroutine(request.Target.BakeAOAsync());

            // Collect stats from the generated mesh
            var generatedMesh = request.Target.GetComponentInChildren<MeshFilter>()?.sharedMesh;
            if (generatedMesh != null)
            {
                _totalVertices += generatedMesh.vertexCount;
                _totalTriangles += generatedMesh.triangles.Length / 3;
                _totalRaycasts += generatedMesh.vertexCount * request.Target.sampleCount;
            }
            _totalObjects++;

            // Complete
            _activeRequests.Remove(request);
            activeCount = _activeRequests.Count;
            completedCount++;

            request.OnComplete?.Invoke();
            OnBakeCompleted?.Invoke(request.Target);

            Debug.Log($"[AOBakeManager] Completed: {request.Target.name} ({completedCount}/{_totalObjects})");
        }

        private IEnumerator ReprioritizeCoroutine()
        {
            while (_isProcessing)
            {
                yield return new WaitForSeconds(reprioritizeInterval);

                if (playerTransform == null || _pendingRequests.Count < 2)
                    continue;

                // Update priorities based on current player position
                for (int i = 0; i < _pendingRequests.Count; i++)
                {
                    var request = _pendingRequests[i];
                    if (request.Target != null)
                    {
                        request.Priority = GetDistanceToPlayer(request.Target.transform);
                        _pendingRequests[i] = request;
                    }
                }

                // Re-sort
                _pendingRequests.Sort((a, b) => a.Priority.CompareTo(b.Priority));
            }
        }

        private float GetDistanceToPlayer(Transform target)
        {
            Vector3 referencePoint = playerTransform != null ? playerTransform.position : Vector3.zero;
            return Vector3.Distance(referencePoint, target.position);
        }

        /// <summary>
        /// Get current queue status
        /// </summary>
        public (int pending, int active, int completed) GetStatus()
        {
            return (pendingCount, activeCount, completedCount);
        }
    }
}
