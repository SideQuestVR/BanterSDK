using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BS
{
    /// <summary>
    /// Where one world's keys lead: its scene, its kit bundles, the built-in library, downloads. Resolves a key to an
    /// object and lists what a world holds. Dropped with its world. Main thread.
    /// </summary>
    internal sealed class BSAssetSources
    {
        /// <summary>One line of a listing: what <c>BS.Assets.Find</c> answers with.</summary>
        internal struct Listing
        {
            internal string Key;
            internal string Name;
            internal AssetType? Type;
            internal long Size;
            internal bool Loaded;
        }

        /// <summary>Resources-relative root of the built-in library (<see cref="BSKitAsset"/>).</summary>
        const string LibraryRoot = "Assets/";

        // The world's scene, walked once, by lower-case object name; and that scene's handle.
        Dictionary<string, List<(UnityEngine.Object asset, AssetType type)>> worldIndex;
        int worldIndexScene;
        // That scene's manifest, parsed once: null for a world built before them.
        BSAssetManifest worldManifest;
        int? worldManifestScene;
        readonly Dictionary<AssetBundle, BSAssetManifest> kitManifests = new Dictionary<AssetBundle, BSAssetManifest>();

        /// <summary>World and kit assets are the world's own, already downloaded: memory pressure never refuses them.</summary>
        internal static bool IsCritical(BSAssetKey key) => key.Kind != BSAssetKeyKind.Url && key.Kind != BSAssetKeyKind.Library;

        /// <summary>Loads what <paramref name="key"/> names. Throws with a message for the page when it can't.</summary>
        internal async Task<BSLoadedAsset> Load(BSAssetKey key, AssetType? wanted)
        {
            switch (key.Kind)
            {
                case BSAssetKeyKind.World: return LoadWorld(key, wanted);
                case BSAssetKeyKind.Kit: return await LoadKit(key);
                case BSAssetKeyKind.Library: return LoadLibrary(key);
                case BSAssetKeyKind.Url: return await LoadUrl(key, wanted);
                default: throw new InvalidOperationException("nothing is registered under " + key.Text);
            }
        }

        // -------------------------------------------------------------------------------------------------- the world

        /// <summary>Which scene is the world's. Tests point it at a scene of their own.</summary>
        internal Func<Scene> WorldScene = BundleScene;

        /// <summary>The scene of the world's bundle; an invalid scene when it has none, or it hasn't loaded yet.</summary>
        static Scene BundleScene()
        {
            var bundle = BSScene.Instance()?.settings?.SceneAssetBundle?.assetBundle;
            if (bundle != null)
            {
                var paths = bundle.GetAllScenePaths();
                if (paths.Length > 0) return SceneManager.GetSceneByName(System.IO.Path.GetFileNameWithoutExtension(paths[0]));
            }
#if UNITY_EDITOR && !GREENFIELD_PROJECT
            // Play in the SDK: the creator's open scene is the world, and no bundle loaded it.
            return SceneManager.GetActiveScene();
#else
            return default;
#endif
        }

        Dictionary<string, List<(UnityEngine.Object asset, AssetType type)>> WorldIndex()
        {
            var scene = WorldScene();
            if (!scene.IsValid() || !scene.isLoaded) return null;
            if (worldIndex != null && worldIndexScene == scene.handle) return worldIndex;

            var index = new Dictionary<string, List<(UnityEngine.Object, AssetType)>>();
            var seen = new HashSet<UnityEngine.Object>();
            void Add(UnityEngine.Object asset, AssetType type)
            {
                if (asset == null || !seen.Add(asset)) return;
                var name = asset.name.ToLowerInvariant();
                if (!index.TryGetValue(name, out var named)) index[name] = named = new List<(UnityEngine.Object, AssetType)>();
                named.Add((asset, type));
            }
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    foreach (var material in renderer.sharedMaterials)
                    {
                        if (material == null) continue;
                        Add(material, AssetType.Material);
                        foreach (var slot in material.GetTexturePropertyNameIDs())
                        {
                            if (material.GetTexture(slot) is Texture2D texture) Add(texture, AssetType.Texture2D);
                        }
                    }
                }
                foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true)) Add(filter.sharedMesh, AssetType.Mesh);
                foreach (var skinned in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) Add(skinned.sharedMesh, AssetType.Mesh);
                foreach (var source in root.GetComponentsInChildren<AudioSource>(true)) Add(source.clip, AssetType.AudioClip);
            }
            worldIndex = index;
            worldIndexScene = scene.handle;
            return index;
        }

        /// <summary>Everything the world's scene uses, found by walking it.</summary>
        internal IEnumerable<(UnityEngine.Object asset, AssetType type)> WorldAssets() =>
            WorldIndex()?.Values.SelectMany(named => named) ?? Enumerable.Empty<(UnityEngine.Object, AssetType)>();

        BSAssetManifest WorldManifest()
        {
            var scene = WorldScene();
            if (!scene.IsValid() || !scene.isLoaded) return null;
            if (worldManifestScene != scene.handle)
            {
                worldManifest = ReadManifest(scene);
                worldManifestScene = scene.handle;
            }
            return worldManifest;
        }

        /// <summary>The manifest the Builder left in a world's scene; null for a world built before it did.</summary>
        internal static BSAssetManifest ReadManifest(Scene scene)
        {
            var holder = scene.GetRootGameObjects().FirstOrDefault(root => root.name == BSAssetManifest.HolderName);
            if (holder == null) return null;
            var chunks = new List<string>(holder.transform.childCount);
            foreach (Transform chunk in holder.transform) chunks.Add(chunk.name);
            return BSAssetManifest.TryParse(BSAssetManifest.FromChunks(chunks), out var manifest) ? manifest : null;
        }

        BSLoadedAsset LoadWorld(BSAssetKey key, AssetType? wanted)
        {
            var index = WorldIndex() ?? throw new InvalidOperationException("this world's scene isn't loaded");
            var item = WorldManifest()?.Assets.FirstOrDefault(a => string.Equals(a.Path, key.Path, StringComparison.OrdinalIgnoreCase));
            wanted ??= item?.Type;
            foreach (var name in new[] { item?.Name, key.Path, BSAssetManifest.NameOf(key.Path) })
            {
                if (name == null || !index.TryGetValue(name.ToLowerInvariant(), out var named)) continue;
                foreach (var (asset, type) in named)
                {
                    if (wanted == null || wanted == type)
                        return new BSLoadedAsset { Asset = asset, Type = type, Size = BSAssetRegistry.EstimateMemorySize(asset) };
                }
            }
            throw new InvalidOperationException("this world's scene doesn't use " + key.Text);
        }

        // ---------------------------------------------------------------------------------------------- kits and lib

        static AssetBundle KitBundleOf(BSAssetKey key)
        {
            var settings = BSScene.Instance()?.settings;
            if (settings == null) return null;
            var named = key.KitBundle;
            if (named == null) return settings.KitPaths.TryGetValue(key.KitPath, out var kit) ? kit.assetBundle : FindHolder(settings, key.KitPath);
            return settings.KitBundles.Select(b => b.assetBundle).FirstOrDefault(b => b != null && string.Equals(b.name, named, StringComparison.OrdinalIgnoreCase));
        }

        // KitPaths only lists prefabs.
        static AssetBundle FindHolder(BSSceneSettings settings, string path) =>
            settings.KitBundles.Select(b => b.assetBundle).FirstOrDefault(b => b != null && b.Contains(path));

        static async Task<BSLoadedAsset> LoadKit(BSAssetKey key)
        {
            var bundle = KitBundleOf(key) ?? throw new InvalidOperationException("no kit bundle holds " + key.Text);
            var request = bundle.LoadAssetAsync(key.KitPath);
            await request;
            var asset = request.asset;
            if (asset == null) throw new InvalidOperationException("no kit bundle holds " + key.Text);
            // Judged as it loads, as BSKitItem does; throws when it fails.
            if (asset is GameObject prefab) BSUnityEventGuard.SanitizePrefab(prefab, key.KitPath);
            return new BSLoadedAsset { Asset = asset, Type = TypeOf(asset), Size = BSAssetRegistry.EstimateMemorySize(asset) };
        }

        static BSLoadedAsset LoadLibrary(BSAssetKey key)
        {
            var dot = key.Path.LastIndexOf('.');
            var prefab = Resources.Load<GameObject>(LibraryRoot + (dot > 0 ? key.Path.Substring(0, dot) : key.Path));
            if (prefab == null) throw new InvalidOperationException("the library has no " + key.Text);
            return new BSLoadedAsset { Asset = prefab, Type = AssetType.GameObject };
        }

        static AssetType TypeOf(UnityEngine.Object asset)
        {
            switch (asset)
            {
                case Texture2D _: return AssetType.Texture2D;
                case AudioClip _: return AssetType.AudioClip;
                case Material _: return AssetType.Material;
                case Mesh _: return AssetType.Mesh;
                case GameObject _: return AssetType.GameObject;
                case Sprite _: return AssetType.Sprite;
                case Font _: return AssetType.Font;
                case Shader _: return AssetType.Shader;
                case AnimationClip _: return AssetType.AnimationClip;
                case RenderTexture _: return AssetType.RenderTexture;
                case Cubemap _: return AssetType.Cubemap;
                default: throw new InvalidOperationException("a " + asset.GetType().Name + " can't be loaded from a script");
            }
        }

        // ------------------------------------------------------------------------------------------------------ urls

        /// <summary>
        /// Through <see cref="Get"/>'s cache, as every other download. Its copy may be shared with a material, so an entry
        /// lets go of it rather than destroying it.
        /// </summary>
        static async Task<BSLoadedAsset> LoadUrl(BSAssetKey key, AssetType? wanted)
        {
            var url = key.Path;
            if (!BSAssetKey.IsWebAddress(url)) throw new InvalidOperationException(BSAssetKey.WebAddressesOnly);
            var type = wanted ?? BSAssetManifest.TypeOf(new Uri(url).AbsolutePath);
            UnityEngine.Object asset;
            switch (type)
            {
                case AssetType.Texture2D:
                    asset = await Get.Texture(url);
                    break;
                case AssetType.AudioClip:
                    asset = await Get.Audio(url);
                    break;
                default:
                    throw new InvalidOperationException("a url loads as a Texture2D or an AudioClip: name the type if its extension doesn't say");
            }
            if (asset == null) throw new InvalidOperationException("nothing came from " + url);
            return new BSLoadedAsset
            {
                Asset = asset,
                Type = type.Value,
                Url = url,
                Size = BSAssetRegistry.EstimateMemorySize(asset),
                Dispose = () => Get.Forget(url, asset),
            };
        }

        // --------------------------------------------------------------------------------------------------- listing

        /// <summary>What this world holds: its scene, its kits, and whatever is loaded besides. <c>lib:</c> isn't listed.</summary>
        internal List<Listing> List(BSAssetDatabase database)
        {
            var listings = new Dictionary<string, Listing>();
            void Add(string key, string name, AssetType? type, long size)
            {
                if (BSAssetKey.TryParse(key, out var parsed)) listings[parsed.Text] = new Listing { Key = parsed.Text, Name = name, Type = type, Size = size };
            }

            var manifest = WorldManifest();
            if (manifest != null)
            {
                foreach (var item in manifest.Assets) Add("world:" + (item.Path ?? item.Name), item.Name, item.Type, item.Size);
            }
            else
            {
                foreach (var (asset, type) in WorldAssets()) Add("world:" + asset.name, asset.name, type, BSAssetRegistry.EstimateMemorySize(asset));
            }

            var settings = BSScene.Instance()?.settings;
            if (settings != null)
            {
                foreach (var kit in settings.KitBundles)
                {
                    if (kit == null || kit.assetBundle == null) continue;
                    foreach (var item in KitManifest(kit.assetBundle).Assets) Add("kit:" + item.Path, item.Name, item.Type, item.Size);
                }
            }

            foreach (var entry in database.Entries)
            {
                listings.TryGetValue(entry.Key, out var listing);
                listing.Key = entry.Key;
                listing.Name = listing.Name ?? entry.Meta.id;
                listing.Type = entry.Meta.type;
                listing.Size = entry.Meta.memorySize;
                listing.Loaded = entry.Meta.loaded;
                listings[entry.Key] = listing;
            }
            return listings.Values.ToList();
        }

        BSAssetManifest KitManifest(AssetBundle bundle)
        {
            if (kitManifests.TryGetValue(bundle, out var manifest)) return manifest;
            var names = bundle.GetAllAssetNames();
            var file = names.FirstOrDefault(n => n.EndsWith(BSAssetManifest.FileName, StringComparison.OrdinalIgnoreCase));
            if (file == null || !BSAssetManifest.TryParse(bundle.LoadAsset<TextAsset>(file)?.text, out manifest))
                manifest = BSAssetManifest.FromAssetNames(names);
            return kitManifests[bundle] = manifest;
        }
    }
}
