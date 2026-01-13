using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Banter.SDK
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
    /// Central registry for all assets in the Banter scene.
    /// Manages asset lifecycle, reference counting, and provides lookup functionality.
    /// Singleton pattern - use BanterAssetRegistry.Instance
    /// </summary>
    public class BanterAssetRegistry : MonoBehaviour
    {
        private static BanterAssetRegistry _instance;

        public static BanterAssetRegistry Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("BanterAssetRegistry");
                    _instance = go.AddComponent<BanterAssetRegistry>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        // Asset storage
        private Dictionary<string, UnityEngine.Object> assets = new Dictionary<string, UnityEngine.Object>();
        private Dictionary<string, AssetMetadata> metadata = new Dictionary<string, AssetMetadata>();
        private Dictionary<string, HashSet<string>> references = new Dictionary<string, HashSet<string>>();

        // Indexing for fast lookups
        private Dictionary<AssetType, HashSet<string>> typeIndex = new Dictionary<AssetType, HashSet<string>>();
        private Dictionary<string, string> urlIndex = new Dictionary<string, string>(); // URL -> assetId

        /// <summary>
        /// Events
        /// </summary>
        public event Action<string, AssetType> OnAssetRegistered;
        public event Action<string> OnAssetLoaded;
        public event Action<string, string> OnAssetFailed;
        public event Action<string> OnAssetDestroyed;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            // Scan the scene for existing assets on startup
            ScanScene();
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

            // Store asset
            assets[assetId] = asset;

            // Store metadata
            metadata[assetId] = new AssetMetadata
            {
                id = assetId,
                type = type,
                url = url,
                loaded = false,
                memorySize = EstimateMemorySize(asset),
                createdAt = DateTime.Now,
                tag = tag
            };

            // Index by type
            if (!typeIndex.ContainsKey(type))
            {
                typeIndex[type] = new HashSet<string>();
            }
            typeIndex[type].Add(assetId);

            // Index by URL if provided
            if (!string.IsNullOrEmpty(url))
            {
                urlIndex[url] = assetId;
            }

            // Initialize reference tracking
            references[assetId] = new HashSet<string>();

            OnAssetRegistered?.Invoke(assetId, type);
            SendAssetRegistered(assetId);

            return assetId;
        }

        /// <summary>
        /// Unregister an asset from the registry
        /// </summary>
        public void UnregisterAsset(string assetId)
        {
            if (!assets.ContainsKey(assetId))
            {
                return;
            }

            var meta = metadata[assetId];

            // Remove from type index
            if (typeIndex.ContainsKey(meta.type))
            {
                typeIndex[meta.type].Remove(assetId);
                if (typeIndex[meta.type].Count == 0)
                {
                    typeIndex.Remove(meta.type);
                }
            }

            // Remove from URL index
            if (!string.IsNullOrEmpty(meta.url) && urlIndex.ContainsKey(meta.url))
            {
                urlIndex.Remove(meta.url);
            }

            // Clean up
            assets.Remove(assetId);
            metadata.Remove(assetId);
            references.Remove(assetId);

            OnAssetDestroyed?.Invoke(assetId);
            SendAssetDestroyed(assetId);
        }

        /// <summary>
        /// Get an asset by ID
        /// </summary>
        public T GetAsset<T>(string assetId) where T : UnityEngine.Object
        {
            if (assets.TryGetValue(assetId, out var asset))
            {
                return asset as T;
            }
            return null;
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
            var results = new List<UnityEngine.Object>();

            if (typeIndex.TryGetValue(type, out var assetIds))
            {
                foreach (var id in assetIds)
                {
                    if (assets.TryGetValue(id, out var asset))
                    {
                        results.Add(asset);
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Find asset by URL
        /// </summary>
        public UnityEngine.Object FindAssetByUrl(string url)
        {
            if (urlIndex.TryGetValue(url, out var assetId))
            {
                return assets.TryGetValue(assetId, out var asset) ? asset : null;
            }
            return null;
        }

        /// <summary>
        /// Get asset metadata
        /// </summary>
        public AssetMetadata? GetMetadata(string assetId)
        {
            return metadata.TryGetValue(assetId, out var meta) ? meta : (AssetMetadata?)null;
        }

        /// <summary>
        /// Mark asset as loaded
        /// </summary>
        public void MarkAssetLoaded(string assetId)
        {
            if (metadata.ContainsKey(assetId))
            {
                var meta = metadata[assetId];
                meta.loaded = true;
                metadata[assetId] = meta;

                OnAssetLoaded?.Invoke(assetId);
                SendAssetLoaded(assetId);
            }
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
            if (!references.ContainsKey(assetId))
            {
                references[assetId] = new HashSet<string>();
            }
            references[assetId].Add(ownerId);
        }

        /// <summary>
        /// Remove a reference
        /// </summary>
        public void RemoveReference(string assetId, string ownerId)
        {
            if (references.TryGetValue(assetId, out var refs))
            {
                refs.Remove(ownerId);
            }
        }

        /// <summary>
        /// Get reference count for an asset
        /// </summary>
        public int GetReferenceCount(string assetId)
        {
            return references.TryGetValue(assetId, out var refs) ? refs.Count : 0;
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
        /// Scan scene for existing assets (called on scene load)
        /// </summary>
        public void ScanScene()
        {
            int scannedCount = 0;

            // Find all textures
            var textures = Resources.FindObjectsOfTypeAll<Texture2D>();
            foreach (var tex in textures)
            {
                // Skip Unity internal assets
                if (IsSceneAsset(tex))
                {
                    RegisterAsset(tex, AssetType.Texture2D);
                    scannedCount++;
                }
            }

            // Find all audio clips
            var audioClips = Resources.FindObjectsOfTypeAll<AudioClip>();
            foreach (var clip in audioClips)
            {
                if (IsSceneAsset(clip))
                {
                    RegisterAsset(clip, AssetType.AudioClip);
                    scannedCount++;
                }
            }

            // Find all materials
            var materials = Resources.FindObjectsOfTypeAll<Material>();
            foreach (var mat in materials)
            {
                if (IsSceneAsset(mat))
                {
                    RegisterAsset(mat, AssetType.Material);
                    scannedCount++;
                }
            }

            // Find all meshes
            var meshes = Resources.FindObjectsOfTypeAll<Mesh>();
            foreach (var mesh in meshes)
            {
                if (IsSceneAsset(mesh))
                {
                    RegisterAsset(mesh, AssetType.Mesh);
                    scannedCount++;
                }
            }

            Debug.Log($"Asset registry scanned scene: {scannedCount} new assets registered (total: {assets.Count})");
        }

        /// <summary>
        /// Check if an asset is a scene asset (not Unity internal/generated)
        /// </summary>
        private bool IsSceneAsset(UnityEngine.Object obj)
        {
            if (obj == null || string.IsNullOrEmpty(obj.name))
                return false;

            // Skip Unity internal assets
            if (obj.name.StartsWith("Hidden") ||
                obj.name.StartsWith("Default-") ||
                obj.name.StartsWith("unity_") ||
                obj.name.StartsWith("Generated"))
                return false;

            // Skip already registered assets
            string testId = $"asset_{obj.GetType().Name}_{obj.GetInstanceID()}";
            if (assets.ContainsKey(testId))
                return false;

            return true;
        }

        /// <summary>
        /// Estimate memory size of an asset
        /// </summary>
        private long EstimateMemorySize(UnityEngine.Object asset)
        {
            if (asset is Texture2D tex)
            {
                return tex.width * tex.height * 4; // Rough estimate
            }
            else if (asset is AudioClip clip)
            {
                return clip.samples * clip.channels * 2; // 16-bit samples
            }
            else if (asset is Mesh mesh)
            {
                return mesh.vertexCount * 32; // Rough estimate
            }
            return 0;
        }

        /// <summary>
        /// Get total memory usage
        /// </summary>
        public long GetTotalMemoryUsage()
        {
            return metadata.Values.Sum(m => m.memorySize);
        }

        #region Communication with JavaScript

        private void SendAssetRegistered(string assetId)
        {
            if (!metadata.TryGetValue(assetId, out var meta))
                return;

            var message = $"!ar!{MessageDelimiters.PRIMARY}{assetId}{MessageDelimiters.SECONDARY}" +
                         $"{(int)meta.type}{MessageDelimiters.SECONDARY}" +
                         $"{meta.url ?? ""}{MessageDelimiters.SECONDARY}" +
                         $"{(meta.loaded ? "1" : "0")}{MessageDelimiters.SECONDARY}" +
                         $"{meta.tag ?? ""}";

            BanterScene.Instance()?.link?.Send(message);
        }

        private void SendAssetLoaded(string assetId)
        {
            if (!metadata.TryGetValue(assetId, out var meta))
                return;

            var message = $"!al!{MessageDelimiters.PRIMARY}{assetId}{MessageDelimiters.SECONDARY}" +
                         $"{meta.memorySize}";

            BanterScene.Instance()?.link?.Send(message);
        }

        private void SendAssetFailed(string assetId, string error)
        {
            var message = $"!af!{MessageDelimiters.PRIMARY}{assetId}{MessageDelimiters.SECONDARY}{error}";
            BanterScene.Instance()?.link?.Send(message);
        }

        private void SendAssetDestroyed(string assetId)
        {
            var message = $"!ad!{MessageDelimiters.PRIMARY}{assetId}";
            BanterScene.Instance()?.link?.Send(message);
        }

        #endregion

        /// <summary>
        /// Debug: Print registry state
        /// </summary>
        public void DebugPrint()
        {
            Debug.Log($"=== Asset Registry ===");
            Debug.Log($"Total Assets: {assets.Count}");
            Debug.Log($"Total Memory: {GetTotalMemoryUsage() / 1024f / 1024f:F2} MB");
            Debug.Log($"\nAssets by Type:");
            foreach (var kvp in typeIndex)
            {
                var memory = kvp.Value.Sum(id => metadata.TryGetValue(id, out var m) ? m.memorySize : 0);
                Debug.Log($"  {kvp.Key}: {kvp.Value.Count} ({memory / 1024f / 1024f:F2} MB)");
            }
        }
    }
}
