using UnityEngine;

namespace Banter.SDK
{
    /// <summary>
    /// Simple helper script to load Quest Home environments with minimal setup.
    /// Just attach to a GameObject, set the APK URL in the Inspector, and it loads automatically on Start.
    /// </summary>
    [AddComponentMenu("Banter/Quest Home Loader")]
    public class QuestHomeLoader : MonoBehaviour
    {
        [Header("Quest Home Settings")]
        [Tooltip("SideQuest listing URL (e.g., https://sidequestvr.com/app/1234/...) or direct APK URL (e.g., https://cdn.sidequestvr.com/file/1234/app.apk)")]
        public string apkUrl = "https://sidequestvr.com/app/167567/canyon-environment";

        [Tooltip("Automatically generate colliders on opaque meshes")]
        public bool addColliders = true;

        [Tooltip("Enable to make surfaces climbable by setting colliders to Grabbable layer (Layer 20)")]
        public bool climbable = false;

        [Space(10)]
        [Tooltip("Load the Quest Home automatically when Start() is called")]
        public bool loadOnStart = true;

        [Header("Status (Read Only)")]
        [Tooltip("Current loading status")]
        [SerializeField] private string status = "Not loaded";

        private GameObject questHomeObject;
        private BanterQuestHome questHomeComponent;
        private bool isLoading = false;

        /// <summary>
        /// Unity Start - Automatically load Quest Home if loadOnStart is enabled
        /// </summary>
        void Start()
        {
            if (loadOnStart)
            {
                LoadQuestHome();
            }
        }

        /// <summary>
        /// Load the Quest Home from the specified APK URL
        /// Can be called manually or automatically on Start
        /// </summary>
        public void LoadQuestHome()
        {
            if (isLoading)
            {
                Debug.LogWarning("[QuestHomeLoader] Already loading a Quest Home, please wait...");
                return;
            }

            if (string.IsNullOrEmpty(apkUrl))
            {
                Debug.LogError("[QuestHomeLoader] APK URL is not set! Please set the URL in the Inspector.");
                status = "Error: No URL";
                return;
            }

            if (!apkUrl.StartsWith("http"))
            {
                Debug.LogError($"[QuestHomeLoader] Invalid APK URL: {apkUrl}. URL must start with http:// or https://");
                status = "Error: Invalid URL";
                return;
            }

            Debug.Log($"[QuestHomeLoader] Loading Quest Home from: {apkUrl}");
            status = "Loading...";
            isLoading = true;

            // Create a child GameObject to hold the Quest Home
            questHomeObject = new GameObject("QuestHome_" + System.DateTime.Now.Ticks);
            questHomeObject.transform.SetParent(transform);
            questHomeObject.transform.localPosition = Vector3.zero;
            questHomeObject.transform.localRotation = Quaternion.identity;
            questHomeObject.transform.localScale = Vector3.one;

            // Add the BanterQuestHome component
            questHomeComponent = questHomeObject.AddComponent<BanterQuestHome>();

            // Configure the component using the public C# API
            questHomeComponent.Url = apkUrl;
            questHomeComponent.AddColliders = addColliders;
            questHomeComponent.Climbable = climbable;

            // Monitor for completion (check the _loaded field via reflection or wait)
            StartCoroutine(MonitorLoadStatus());

            Debug.Log("[QuestHomeLoader] Quest Home component added and loading started");
        }

        /// <summary>
        /// Monitor the loading status and update the status field
        /// </summary>
        private System.Collections.IEnumerator MonitorLoadStatus()
        {
            float startTime = Time.time;
            float timeout = 120f; // 2 minute timeout

            while (isLoading)
            {
                // Check if component was destroyed
                if (questHomeComponent == null)
                {
                    status = "Error: Component destroyed";
                    isLoading = false;
                    yield break;
                }

                // Check for timeout
                if (Time.time - startTime > timeout)
                {
                    status = "Error: Load timeout";
                    isLoading = false;
                    Debug.LogError("[QuestHomeLoader] Quest Home load timed out after 2 minutes");
                    yield break;
                }

                // Update status
                float elapsed = Time.time - startTime;
                status = $"Loading... ({elapsed:F0}s)";

                // Wait a bit before checking again
                yield return new WaitForSeconds(0.5f);

                // Check if loaded (we can check if the component is still active and loading hasn't failed)
                // For now, just check if it's been a reasonable time and no errors
                if (elapsed > 5f && questHomeObject != null && questHomeObject.transform.childCount > 0)
                {
                    // Looks like something loaded
                    status = $"Loaded ({elapsed:F0}s)";
                    isLoading = false;
                    Debug.Log($"[QuestHomeLoader] Quest Home appears to have loaded successfully in {elapsed:F1} seconds");
                    yield break;
                }
            }
        }

        /// <summary>
        /// Unload the current Quest Home
        /// </summary>
        public void UnloadQuestHome()
        {
            if (questHomeObject != null)
            {
                Debug.Log("[QuestHomeLoader] Unloading Quest Home");
                Destroy(questHomeObject);
                questHomeObject = null;
                questHomeComponent = null;
                status = "Unloaded";
                isLoading = false;
            }
        }

        /// <summary>
        /// Reload the Quest Home (unload then load again)
        /// </summary>
        public void ReloadQuestHome()
        {
            UnloadQuestHome();
            LoadQuestHome();
        }

        /// <summary>
        /// Unity Editor - Draw gizmo to show the loader position
        /// </summary>
        void OnDrawGizmos()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, 0.5f);
        }

        /// <summary>
        /// Cleanup when destroyed
        /// </summary>
        void OnDestroy()
        {
            if (questHomeObject != null)
            {
                Destroy(questHomeObject);
            }
        }
    }
}
