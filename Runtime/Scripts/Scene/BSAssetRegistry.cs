using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace BS
{
    /// <summary>
    /// Metadata for assets in the registry
    /// </summary>
    [Serializable]
    public struct AssetMetadata
    {
        public string id;
        public AssetType type;
        public string url;
        public bool loaded;
        public long memorySize;
        public DateTime createdAt;
        public string tag;
    }

    /// <summary>
    /// The way in to the current world's assets, which live in a <see cref="BSAssetDatabase"/> for as long as the world
    /// does. Assets are looked up by id or key, and their events and the page's messages come from here.
    /// Singleton pattern - use BSAssetRegistry.Instance
    /// </summary>
    public class BSAssetRegistry : MonoBehaviour, IBSAssetListener
    {
        private static BSAssetRegistry _instance;

        public static BSAssetRegistry Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("BSAssetRegistry");
                    _instance = go.AddComponent<BSAssetRegistry>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        BSAssetDatabase database;
        BSAssetSources sources;
        BSAssetMemoryPolicy memory;

        internal BSAssetMemoryPolicy Memory => memory ??= new BSAssetMemoryPolicy(new BSDeviceFreeMemory(), () => Time.realtimeSinceStartup);

        /// <summary>The current world's assets.</summary>
        internal BSAssetDatabase Database => database ??= new BSAssetDatabase(this, Memory);

        /// <summary>Where the current world's keys lead.</summary>
        internal BSAssetSources Sources => sources ??= new BSAssetSources();

        /// <summary>
        /// Events
        /// </summary>
        public event Action<string, AssetType> OnAssetRegistered;
        public event Action<string> OnAssetLoaded;
        public event Action<string, string> OnAssetFailed;
        public event Action<string> OnAssetDestroyed;
        /// <summary>Raised when <see cref="SetAsset"/> replaces the object under an existing id.</summary>
        public event Action<string> OnAssetUpdated;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
            Application.lowMemory += OnLowMemory;
        }

        void OnDestroy()
        {
            Application.lowMemory -= OnLowMemory;
        }

        void Update()
        {
            Database.Tick();
        }

        // No Resources.UnloadUnusedAssets() here: it runs a full, blocking collection that can stall the frame and audio
        // for a long time on a device that is already short of memory. Evicted entries are left for Unity's own
        // unloading (world changes).
        void OnLowMemory()
        {
            Memory.LowMemoryReported();
            Database.Tick();
        }

        /// <summary>
        /// The world is being left: its assets stop being found now, and are released once <paramref name="worldGone"/>
        /// completes, when its scene and bundles have gone. Main thread.
        /// </summary>
        internal static void WorldChanging(Task worldGone)
        {
            if (_instance != null) _instance.BeginNextWorld(worldGone);
        }

        void BeginNextWorld(Task worldGone)
        {
            var leaving = Database;
            leaving.Retire();
            database = null;
            sources = null;
            Database.TakeBrowsers(leaving);
            ReleaseWhenGone(leaving, worldGone);
        }

        static async void ReleaseWhenGone(BSAssetDatabase leaving, Task worldGone)
        {
            try
            {
                await worldGone;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[BSAssetRegistry] The world's unload failed; releasing its assets anyway: " + e.Message);
            }
            leaving.ReleaseAll();
        }

        /// <summary>
        /// Register an asset in the registry
        /// </summary>
        public string RegisterAsset(UnityEngine.Object asset, AssetType type, string url = null, string tag = null)
        {
            if (asset == null)
            {
                Debug.LogError("Cannot register null asset");
                return null;
            }

            // Generate unique ID
            string assetId = $"asset_{type}_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
            Database.Store(assetId, asset, type, url, tag, EstimateMemorySize(asset));
            return assetId;
        }

        /// <summary>
        /// Register <paramref name="asset"/> under a fixed id, or replace the object already under it.
        /// A replacement raises <see cref="OnAssetUpdated"/> and tells the page, so anything bound to the
        /// id (a material's <c>asset_</c> texture reference) picks up the new object. For assets that are
        /// recreated at runtime, like a browser's texture after a resize.
        /// </summary>
        public void SetAsset(string assetId, UnityEngine.Object asset, AssetType type, string tag = null)
        {
            SetAsset(assetId, asset, type, tag, null);
        }

        /// <summary>
        /// <see cref="SetAsset(string, UnityEngine.Object, AssetType, string)"/> for an asset that came from
        /// <paramref name="url"/>, which a new id is indexed under.
        /// </summary>
        public void SetAsset(string assetId, UnityEngine.Object asset, AssetType type, string tag, string url)
        {
            if (string.IsNullOrEmpty(assetId) || asset == null)
            {
                Debug.LogError($"Cannot set asset '{assetId}' to null");
                return;
            }

            var entry = Database.Find(assetId);
            if (entry == null)
            {
                Database.MarkLoaded(Database.Store(assetId, asset, type, url, tag, EstimateMemorySize(asset)));
                return;
            }
            if (entry.Asset == asset) return;
            Database.Replace(entry, asset, EstimateMemorySize(asset));
        }

        /// <summary>Nothing else owns this asset's object: it is destroyed with the entry. A page's Material, say.</summary>
        internal void OwnAsset(string assetId)
        {
            var entry = Database.Find(assetId);
            if (entry == null) return;
            var asset = entry.Asset;
            entry.Dispose = () =>
            {
                if (Application.isPlaying) Destroy(asset);
                else DestroyImmediate(asset);
            };
        }

        /// <summary>
        /// Unregister an asset from the registry
        /// </summary>
        public void UnregisterAsset(string assetId)
        {
            Database.Remove(assetId);
        }

        /// <summary>
        /// Get an asset by ID
        /// </summary>
        public T GetAsset<T>(string assetId) where T : UnityEngine.Object
        {
            return Database.Find(assetId)?.Asset as T;
        }

        /// <summary>
        /// Try to get an asset by ID
        /// </summary>
        public bool TryGetAsset<T>(string assetId, out T asset) where T : UnityEngine.Object
        {
            asset = GetAsset<T>(assetId);
            return asset != null;
        }

        /// <summary>
        /// Find all assets of a specific type
        /// </summary>
        public List<UnityEngine.Object> FindAssets(AssetType type)
        {
            return Database.Entries.Where(e => e.Meta.type == type).Select(e => e.Asset).ToList();
        }

        /// <summary>
        /// Find asset by URL
        /// </summary>
        public UnityEngine.Object FindAssetByUrl(string url)
        {
            return Database.Entries.FirstOrDefault(e => e.Meta.url == url)?.Asset;
        }

        /// <summary>
        /// Get asset metadata
        /// </summary>
        public AssetMetadata? GetMetadata(string assetId)
        {
            var entry = Database.Find(assetId);
            return entry != null ? entry.Meta : (AssetMetadata?)null;
        }

        /// <summary>
        /// Mark asset as loaded
        /// </summary>
        public void MarkAssetLoaded(string assetId)
        {
            var entry = Database.Find(assetId);
            if (entry != null) Database.MarkLoaded(entry);
        }

        /// <summary>
        /// Mark asset as failed
        /// </summary>
        public void MarkAssetFailed(string assetId, string error)
        {
            OnAssetFailed?.Invoke(assetId, error);
            SendAssetFailed(assetId, error);
        }

        /// <summary>
        /// Add a reference from one asset to another
        /// </summary>
        public void AddReference(string assetId, string ownerId)
        {
            Database.AddReference(assetId, ownerId);
        }

        /// <summary>
        /// Remove a reference
        /// </summary>
        public void RemoveReference(string assetId, string ownerId)
        {
            Database.RemoveReference(assetId, ownerId);
        }

        /// <summary>
        /// Get reference count for an asset
        /// </summary>
        public int GetReferenceCount(string assetId)
        {
            return Database.ReferenceCount(assetId);
        }

        /// <summary>
        /// Create asset from URL and options
        /// </summary>
        public async void CreateAssetFromURL(string assetId, AssetType type, string url, Dictionary<string, object> options = null)
        {
            // This will be implemented when we create specific asset types
            // For now, register placeholder
            Debug.Log($"Creating asset: {assetId} of type {type} from URL {url}");
        }

        /// <summary>
        /// Registers what the current world's scene uses: its textures, audio clips, materials and meshes. Nothing runs this
        /// by itself: a world's assets are found when a script asks for them (<c>BS.Assets</c>).
        /// </summary>
        public void ScanScene()
        {
            int scannedCount = 0;
            foreach (var (asset, type) in Sources.WorldAssets())
            {
                if (Database.Entries.Any(e => e.Asset == asset)) continue;
                RegisterAsset(asset, type);
                scannedCount++;
            }
            Debug.Log($"Asset registry scanned the world's scene: {scannedCount} new assets registered (total: {Database.Entries.Count()})");
        }

        /// <summary>
        /// Estimate memory size of an asset
        /// </summary>
        internal static long EstimateMemorySize(UnityEngine.Object asset)
        {
            if (asset is Texture2D tex)
            {
                return (long)tex.width * tex.height * 4; // Rough estimate
            }
            else if (asset is AudioClip clip)
            {
                return (long)clip.samples * clip.channels * 2; // 16-bit samples
            }
            else if (asset is Mesh mesh)
            {
                return (long)mesh.vertexCount * 32; // Rough estimate
            }
            return 0;
        }

        /// <summary>
        /// Get total memory usage
        /// </summary>
        public long GetTotalMemoryUsage()
        {
            return Database.Entries.Sum(e => e.Meta.memorySize);
        }

        #region Communication with JavaScript

        void IBSAssetListener.AssetRegistered(BSAssetEntry entry)
        {
            OnAssetRegistered?.Invoke(entry.Meta.id, entry.Meta.type);
            SendAssetRegistered(entry.Meta);
        }

        void IBSAssetListener.AssetLoaded(BSAssetEntry entry)
        {
            OnAssetLoaded?.Invoke(entry.Meta.id);
            SendAssetLoaded(entry.Meta);
        }

        void IBSAssetListener.AssetUpdated(BSAssetEntry entry)
        {
            OnAssetUpdated?.Invoke(entry.Meta.id);
            SendAssetUpdated(entry.Meta);
        }

        void IBSAssetListener.AssetRemoved(BSAssetEntry entry, bool tellPage)
        {
            OnAssetDestroyed?.Invoke(entry.Meta.id);
            if (tellPage) SendAssetDestroyed(entry.Meta.id);
        }

        private void SendAssetRegistered(AssetMetadata meta)
        {
            var message = $"!ar!{MessageDelimiters.PRIMARY}{meta.id}{MessageDelimiters.SECONDARY}" +
                         $"{(int)meta.type}{MessageDelimiters.SECONDARY}" +
                         $"{WireText.Encode(meta.url ?? "")}{MessageDelimiters.SECONDARY}" +
                         $"{(meta.loaded ? "1" : "0")}{MessageDelimiters.SECONDARY}" +
                         $"{WireText.Encode(meta.tag ?? "")}";

            BSScene.Instance()?.link?.Send(message);
        }

        private void SendAssetLoaded(AssetMetadata meta)
        {
            var message = $"!al!{MessageDelimiters.PRIMARY}{meta.id}{MessageDelimiters.SECONDARY}" +
                         $"{meta.memorySize}";

            BSScene.Instance()?.link?.Send(message);
        }

        private void SendAssetUpdated(AssetMetadata meta)
        {
            var message = $"{APICommands.ASSET_UPDATED}{MessageDelimiters.PRIMARY}{meta.id}{MessageDelimiters.SECONDARY}" +
                         $"{meta.memorySize}";

            BSScene.Instance()?.link?.Send(message);
        }

        private void SendAssetFailed(string assetId, string error)
        {
            var message = $"!af!{MessageDelimiters.PRIMARY}{assetId}{MessageDelimiters.SECONDARY}{WireText.Encode(error)}";
            BSScene.Instance()?.link?.Send(message);
        }

        private void SendAssetDestroyed(string assetId)
        {
            var message = $"!ad!{MessageDelimiters.PRIMARY}{assetId}";
            BSScene.Instance()?.link?.Send(message);
        }

        #endregion

        /// <summary>
        /// Debug: Print registry state
        /// </summary>
        public void DebugPrint()
        {
            Debug.Log($"=== Asset Registry ===");
            Debug.Log($"Total Assets: {Database.Entries.Count()}");
            Debug.Log($"Total Memory: {GetTotalMemoryUsage() / 1024f / 1024f:F2} MB");
            Debug.Log($"\nAssets by Type:");
            foreach (var group in Database.Entries.GroupBy(e => e.Meta.type))
            {
                var memory = group.Sum(e => e.Meta.memorySize);
                Debug.Log($"  {group.Key}: {group.Count()} ({memory / 1024f / 1024f:F2} MB)");
            }
        }
    }
}
