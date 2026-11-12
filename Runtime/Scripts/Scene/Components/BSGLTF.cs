using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using Siccity.GLTFUtility;
using System.Threading.Tasks;

namespace BS
{

    /* 
    #### Banter GLTF
    Add a 3D Model from a glb file to the scene.

    **Properties**
    - `url` - The url of the glb file.
    - `generateMipMaps` - If mipmaps should be generated.
    - `addColliders` - If colliders should be added.
    - `nonConvexColliders` - If colliders should be non convex.
    - `slippery` - If colliders should be slippery.
    - `climbable` - If colliders should be climbable.
    - `legacyRotate` - If the model should be rotated to match the old GLTF forward direction.

    **Code Example**
    ```js
        const url = "https://example.com/model.glb";
        const generateMipMaps = false;
        const addColliders = false;
        const nonConvexColliders = false;
        const slippery = false;
        const climbable = false;
        const legacyRotate = false;
        const gameObject = new BS.GameObject("MyGLTF"); 
        const gltf = await gameObject.AddComponent(new BS.BSGLTF(url, generateMipMaps, addColliders, nonConvexColliders, slippery, climbable, legacyRotate));
    ```
    */
    [DefaultExecutionOrder(-1)]
    [WatchComponent]
    [RequireComponent(typeof(BSObjectId))]

    public partial class BSGLTF : BSComponentBase
    {
        [Tooltip("The URL of the GLB file to be loaded.")]
        [See(initial = "")][SerializeField] internal string url = "";

        [Tooltip("Enable to generate mipmaps for improved texture scaling.")]
        [See(initial = "false")][SerializeField] internal bool generateMipMaps = false;

        [Tooltip("Enable to automatically add colliders to the imported model.")]
        [See(initial = "false")][SerializeField] internal bool addColliders = false;

        [Tooltip("Enable to use non-convex colliders instead of convex ones.")]
        [See(initial = "false")][SerializeField] internal bool nonConvexColliders = false;

        [Tooltip("Enable to make colliders slippery (zero friction).")]
        [See(initial = "false")][SerializeField] internal bool slippery = false;

        [Tooltip("Enable to make the model's colliders climbable.")]
        [See(initial = "false")][SerializeField] internal bool climbable = false;
        // This has been added because unity changed the forward direction of GLTF fast between version 3 and 4. 
        // https://docs.unity3d.com/Packages/com.unity.cloud.gltfast@5.0/manual/UpgradeGuides.html#upgrade-to-4x
        // We decided to leave legacy aframe stuff on the old one by default for compatibility reasons, but 
        // that new stuff will use the forward direction of the new version by default. Aframe shim will set 
        // this to true automaticaly.
        [Tooltip("Enable to rotate the model for compatibility with legacy GLTF forward direction.")]
        [See(initial = "false")][SerializeField] internal bool legacyRotate = false;

        [Tooltip("Set child objects to a specific layer - 0 to disable.")]
        [See(initial = "0")][SerializeField] internal int childrenLayer = 0;
        bool loadStarted;
        // Each load gets a number, so a callback from an older one (an import that outlived its timeout) can't
        // adopt its model over a newer load's, or end the newer load.
        int loadGeneration;
        // The values the load in flight is for. A change while it runs used to be dropped: now it sets
        // reloadPending, and when the load ends the next one loads the latest values (last writer wins). Only the
        // last load reports `loaded`, once.
        string loadingSignature;
        bool reloadPending;

        // The model the importer made for this component, and the meshes, materials, textures and physics materials
        // made for it: all that a reload or teardown may destroy. Serialized so that a copy made with Instantiate (a
        // spawner's) points at its own copy of the model, whose assets are still the original's and so not in its set.
        [SerializeField][HideInInspector] GameObject model;
        // This component adopted model, as opposed to a copy that inherited the reference.
        bool adoptedHere;
        readonly HashSet<UnityEngine.Object> importedAssets = new HashSet<UnityEngine.Object>();

        // Downloads by URL. A failed one isn't kept, so setting the url again tries again.
        private static Dictionary<string, Task<byte[]>> gltfCache = new Dictionary<string, Task<byte[]>>();
        // How far each download has got (0-1), for every component waiting on it: several models often share a URL.
        private static Dictionary<string, float> downloadProgress = new Dictionary<string, float>();
        public static void ClearCache()
        {
            gltfCache.Clear();
            downloadProgress.Clear();
        }

        private Task<byte[]> GetCachedGLTF(string absoluteUrl)
        {
            if (gltfCache.TryGetValue(absoluteUrl, out var cached) && !cached.IsFaulted && !cached.IsCanceled)
            {
                return cached;
            }
            downloadProgress[absoluteUrl] = 0f;
            var gltf = Get.Bytes(absoluteUrl, p => downloadProgress[absoluteUrl] = p);
            gltfCache[absoluteUrl] = gltf;
            return gltf;
        }

        // The space's load gate stops waiting for a component that neither loads nor reports progress for 20 s
        // (BSLoadGate), so the download and the import's start and end are reported. Download 0-0.8, then the import.
        const float DownloadShare = 0.8f, ImportStarted = 0.85f, ImportFinished = 0.95f;
        float reportedProgress = -1f;

        void ReportProgress(int generation, float value)
        {
            if (generation != loadGeneration || this == null || Mathf.Abs(value - reportedProgress) < 0.01f)
            {
                return;
            }
            reportedProgress = value;
            progress?.Invoke(value);
        }

        internal override void StartStuff()
        {
            LogLine.Do("Warning: Using BSGLTF is not recommended for production use. It is slow and not optimized.");
            SetupGLTF();
        }

        // Every mesh, material and texture under an imported model, once each: glTF primitives share a material, and
        // destroying it a second time was the teardown NRE. Only for the importer's own hierarchy, and before anything
        // else can have put assets there (a page's BSMaterial, whose materials are shared scene-wide).
        internal static void CollectImportedAssets(GameObject root, HashSet<UnityEngine.Object> assets)
        {
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh != null) assets.Add(filter.sharedMesh);
            }
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is SkinnedMeshRenderer skinned && skinned.sharedMesh != null) assets.Add(skinned.sharedMesh);
                foreach (var material in renderer.sharedMaterials)
                {
                    // GLTFMaterial.defaultMaterial (a primitive without a material) is the editor's built-in
                    // Default-Material, and null in a player: not the import's to destroy.
                    if (material == null || material == GLTFMaterial.defaultMaterial || !assets.Add(material)) continue;
                    // GLTFUtility's shaders (and URP/Lit, its fallback) have no default textures, so a texture in a
                    // slot is one the importer loaded.
                    foreach (var id in material.GetTexturePropertyNameIDs())
                    {
                        var texture = material.GetTexture(id);
                        if (texture != null) assets.Add(texture);
                    }
                }
            }
        }

        internal static void DestroyAssets(HashSet<UnityEngine.Object> assets)
        {
            foreach (var asset in assets)
            {
                if (asset != null) DestroyImmediate(asset);
            }
            assets.Clear();
        }

        // A model that was never adopted (its component went first, or a newer load replaced it): nothing but the
        // importer has touched it, so what is on it now is what the importer made.
        static void DestroyUnadoptedModel(GameObject go)
        {
            var assets = new HashSet<UnityEngine.Object>();
            CollectImportedAssets(go, assets);
            DestroyAssets(assets);
            Destroy(go);
        }

        // The model goes if this component adopted it, or if it is this object's own copy of one (Instantiate points
        // the reference at the copy). A copy whose original had moved its model elsewhere still points at the
        // original's, which isn't this one's to destroy.
        void DestroyModel(bool hideNow)
        {
            if (model != null && (adoptedHere || model.transform.IsChildOf(transform)))
            {
                if (hideNow) model.SetActive(false);
                Destroy(model);
            }
            model = null;
            adoptedHere = false;
        }

        // UnityWebRequest's own reading of a URL without a scheme that starts with a host name and a path
        // ("cdn.example.com/a.glb" loads https://cdn.example.com/a.glb), kept so that what loaded before still does.
        static readonly Regex HostWithoutScheme = new Regex(@"^\w+(\.\w+)+/");
        static readonly Regex SchemePrefix = new Regex(@"^[A-Za-z][A-Za-z0-9+.\-]*:");

        /// <summary>
        /// The URL to download: an absolute http(s) URL as given, anything else relative to the page, as a browser
        /// resolves it (A-Frame passes asset src attributes through as written). A local file (file:, a drive path)
        /// only from a file: page or when <paramref name="allowLocalFiles"/> (the editor). False when there is nothing
        /// usable: empty, another scheme (javascript:, data:, ftp:), or a path with no page to resolve it on.
        /// The URL is kept as written wherever it can be: Uri.AbsoluteUri rewrites escapes (%7E, %2e), which breaks
        /// signed URLs, and UnityWebRequest passes a URL with escapes through untouched.
        /// </summary>
        internal static bool TryResolveUrl(string url, string pageUrl, bool allowLocalFiles, out string absolute)
        {
            absolute = null;
            url = url?.Trim();
            if (string.IsNullOrEmpty(url)) return false;
            Uri.TryCreate(pageUrl?.Trim(), UriKind.Absolute, out var page);
            string candidate;
            if (HostWithoutScheme.IsMatch(url))
            {
                candidate = "https://" + url;
            }
            else if (url.StartsWith("//"))
            {
                // The page's scheme, as a browser would; https without an http page, as UnityWebRequest did.
                candidate = (page != null && page.Scheme == Uri.UriSchemeHttp ? "http:" : "https:") + url;
            }
            else if (SchemePrefix.IsMatch(url))
            {
                candidate = url;
            }
            else if (page == null)
            {
                return false;
            }
            else if (url.StartsWith("/"))
            {
                // Spelled out: Mono's Uri (Android) can read a leading "/" as a file path.
                candidate = page.GetLeftPart(UriPartial.Authority) + url;
            }
            else
            {
                if (!Uri.TryCreate(page, url, out var combined)) return false;
                candidate = combined.AbsoluteUri;
            }
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)) return false;
            if (uri.Scheme == Uri.UriSchemeFile)
            {
                // A page on the web doesn't get to read the player's files.
                if (!allowLocalFiles && (page == null || page.Scheme != Uri.UriSchemeFile)) return false;
            }
            else if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            {
                return false;
            }
            absolute = candidate;
            return true;
        }

        // The model object's name: the file name in the URL's path. Path.GetFileNameWithoutExtension on the whole URL
        // threw on Mono for | " < > and control characters, after the model had been adopted.
        internal static string ModelName(string absoluteUrl)
        {
            try
            {
                var path = new Uri(absoluteUrl).AbsolutePath;
                var file = Uri.UnescapeDataString(path.Substring(path.LastIndexOf('/') + 1));
                var dot = file.LastIndexOf('.');
                var name = dot > 0 ? file.Substring(0, dot) : file;
                return name.Length > 0 ? name : "GLTF";
            }
            catch (Exception)
            {
                return "GLTF";
            }
        }

        async void SetupGLTF()
        {
            if (loadStarted)
            {
                reloadPending = true;
                return;
            }
            reloadPending = false;
            var generation = ++loadGeneration;
            loadingSignature = GetSignature();
            var requestedUrl = url;
            _loaded = false;
            loadStarted = true;
            string absoluteUrl = null;
            try
            {
                if (string.IsNullOrWhiteSpace(requestedUrl))
                {
                    // Nothing to load (yet): the JS constructor's url defaults to "", and setting url later loads it.
                    // UnityWebRequest said "Malformed URL" for "", an error with no URL in it.
                    FinishLoad(generation);
                    return;
                }
                // CurrentUrl is the URL the space was asked for: the page (the missing-world fallback keeps the
                // original URL there, but its page has no models). Not the URL after any redirect.
                if (!TryResolveUrl(requestedUrl, scene.CurrentUrl, Application.isEditor, out absoluteUrl))
                {
                    LoadFailed(generation, requestedUrl, "it isn't an http(s) URL, or a path on the page");
                    return;
                }
                // `loaded` fires once the model is in the scene (or couldn't be loaded), not before the download:
                // it used to fire here, so `loaded` meant nothing and the space could open without the model.
                var download = GetCachedGLTF(absoluteUrl);
                while (!download.IsCompleted && this != null)
                {
                    ReportProgress(generation, DownloadShare * (downloadProgress.TryGetValue(absoluteUrl, out var got) ? got : 0f));
                    await Task.WhenAny(download, Task.Delay(250));
                }
                var bytes = await download;
                if (this == null)
                {
                    return;
                }
                if (!IsGlb(bytes))
                {
                    LoadFailed(generation, requestedUrl, "it isn't a binary glTF 2.0 (.glb) file");
                    return;
                }
                var importFinished = false;
                ReportProgress(generation, ImportStarted);
                Importer.ImportGLBAsync(bytes, new ImportSettings(), (go, animations) =>
                {
                    importFinished = true;
                    ReportProgress(generation, ImportFinished);
                    try
                    {
                        foreach (var anim in animations)
                        {
                            DestroyImmediate(anim);
                        }
                        var comp = this;
                        if (comp == null || gameObject == null)
                        {
                            LogLine.Do("GameObject/Component was destroyed before GLTF was loaded, killing the GLTF.");
                            DestroyUnadoptedModel(go);
                            return;
                        }
                        if (generation != loadGeneration)
                        {
                            // This import outlived its timeout and a newer load has started since: that one wins.
                            DestroyUnadoptedModel(go);
                            return;
                        }
                        // Replace the previous model, and only that: this used to destroy child 0, whatever it was.
                        DestroyModel(hideNow: true);
                        DestroyAssets(importedAssets);
                        model = go;
                        adoptedHere = true;
                        CollectImportedAssets(go, importedAssets);
                        go.transform.SetParent(transform, false);
                        go.transform.name = ModelName(absoluteUrl);

                        Traverse.Do(go.transform, t =>
                        {
                            var collider = t.name.Contains("sq-collider");
                            var nonconvexcollider = t.name.Contains("sq-nonconvexcollider");
                            t.gameObject.layer = childrenLayer;
                            if (collider || nonconvexcollider && (t.gameObject.GetComponent<Renderer>() != null))
                            {
                                t.gameObject.AddComponent<BSObjectId>();
                                try
                                {
                                    var col = t.gameObject.AddComponent<BSMeshCollider>();
                                    if (nonconvexcollider)
                                    {
                                        col.convex = false;
                                    }
                                }
                                catch (Exception)
                                {
                                    // Debug.LogError(e);
                                }
                            }
                            var climbable = t.name.Contains("sq-climbable");
                            if (climbable)
                            {
                                t.gameObject.layer = 20;
                            }
                        });


                        if (legacyRotate)
                        {
                            var child = go.transform;
                            child.RotateAround(child.position, child.up, 180f);
                        }
                        if (addColliders || nonConvexColliders || slippery)
                        {
                            // The model's meshes only: this used to take every MeshFilter under the object, so each
                            // load added another collider to the creator's own children.
                            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
                            {
                                if (mf.sharedMesh == null)
                                {
                                    continue;
                                }
                                var collider = mf.gameObject.AddComponent<MeshCollider>();
                                collider.convex = !nonConvexColliders;
                                collider.sharedMesh = mf.sharedMesh;
                                if (climbable)
                                {
                                    mf.gameObject.layer = 20;
                                }
                                if (slippery)
                                {
                                    var physicsMaterial = new PhysicsMaterial()
                                    {
                                        dynamicFriction = 0,
                                        staticFriction = 0
                                    };
                                    collider.material = physicsMaterial;
                                    importedAssets.Add(physicsMaterial);
                                }
                                // need to add support fo sq-overridecolor default setting maybe?
                            }
                        }
                        FinishLoad(generation);
                    }
                    catch (Exception e)
                    {
                        if (model == go)
                        {
                            // Adopted already, so importedAssets is go's (the previous model's went before).
                            DestroyAssets(importedAssets);
                            DestroyModel(hideNow: false);
                        }
                        else
                        {
                            DestroyUnadoptedModel(go);
                        }
                        LoadFailed(generation, requestedUrl, e.Message);
                    }
                });
                // The importer is a coroutine that never calls back if it throws part way through (a damaged
                // file), so don't let it hold up the space's load for ever.
                for (var waited = 0; !importFinished && this != null && waited < ImportTimeoutSeconds; waited++)
                {
                    await Task.Delay(1000);
                }
                if (!importFinished && this != null)
                {
                    LoadFailed(generation, requestedUrl, $"the import didn't finish within {ImportTimeoutSeconds} s");
                }
            }
            catch (Exception e)
            {
                // A failed download (404, timeout, no network) is the page's model, not an SDK bug: a warning, like
                // every other LoadFailed, instead of an error with a stack.
                LoadFailed(generation, requestedUrl,
                    absoluteUrl != null && absoluteUrl != requestedUrl ? $"{e.Message} (from {absoluteUrl})" : e.Message);
            }
        }

        const int ImportTimeoutSeconds = 60;

        // What GLTFUtility checks before it parses: given anything else (an HTML error page, a .gltf) its import
        // coroutine throws and never calls back.
        static bool IsGlb(byte[] bytes) =>
            bytes != null && bytes.Length >= 20 &&
            bytes[0] == (byte)'g' && bytes[1] == (byte)'l' && bytes[2] == (byte)'T' && bytes[3] == (byte)'F' &&
            BitConverter.ToUInt32(bytes, 4) == 2;

        // Ends load `generation`; the end of an older one changes nothing. If the values changed while it ran, the
        // next load starts instead of this one reporting, so `loaded` fires once, for the latest values.
        void FinishLoad(int generation, string failure = null)
        {
            if (generation != loadGeneration)
            {
                return;
            }
            loadStarted = false;
            if (reloadPending && this != null && GetSignature() != loadingSignature)
            {
                SetupGLTF();
                return;
            }
            reloadPending = false;
            if (failure == null)
            {
                SetLoadedIfNot();
            }
            else
            {
                SetLoadedIfNot(true, failure);
            }
        }

        // A model that couldn't be loaded still reports loaded, as a success: a failure would cancel the whole
        // space's load (BSScene's loaded listener), and one missing model shouldn't stop the space opening.
        void LoadFailed(int generation, string requestedUrl, string reason)
        {
            if (generation != loadGeneration)
            {
                return;
            }
            Debug.LogWarning($"[BSGLTF] Couldn't load '{requestedUrl}': {reason}");
            FinishLoad(generation, reason);
        }
        internal override void DestroyStuff()
        {
            // Only what the importer made. This used to strip the meshes and materials off every child, creator
            // objects included, whose BSMaterial and BSGeometry assets are shared scene-wide.
            DestroyAssets(importedAssets);
            DestroyModel(hideNow: false);
        }

        internal override void UpdateStuff()
        {

        }
        internal void UpdateCallback(List<PropertyName> changedProperties)
        {
            SetupGLTF();
        }
    }
}