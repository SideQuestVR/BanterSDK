using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace BS
{
    /// <summary>
    /// Instantiates a prefab from the processed low-poly library by its manifest path.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="BSKitItem"/> rather than a mode on it, because the two resolve
    /// through completely different mechanisms and only one of them is about kits in the Banter
    /// sense. BSKitItem loads out of an AssetBundle registered against the space
    /// (<c>scene.settings.KitPaths</c>), which is right for content a creator uploaded with their
    /// space. This one addresses prefabs that ship inside the build, so there is no bundle to wait
    /// for, nothing to download, and no per-space registration — and folding that into BSKitItem
    /// would mean a component whose `path` means two different things depending on state.
    ///
    /// The path is the kit manifest's own `path` field, relative to the package's Assets root:
    /// <c>CartoonCubeWorld/Prefabs/Props/Apple.prefab</c>.
    ///
    /// When the app ships the kit as downloadable bundles it sets <see cref="BundleResolver"/>; the prefab then comes
    /// from the app's own content downloader (not from a space's bundles), and Resources stays the fallback.
    /// </remarks>
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BSObjectId))]
    [WatchComponent]
    public partial class BSKitAsset : BSComponentBase
    {
        [Tooltip("Manifest path of the prefab, relative to the kit package's Assets root, e.g. CartoonCubeWorld/Prefabs/Props/Apple.prefab")]
        [See(initial = "")][SerializeField] internal string path = "";

        /// <summary>
        /// Resources-relative root the prefabs sit under.
        /// </summary>
        /// <remarks>
        /// "Assets/" rather than anything kit-specific because the pipeline re-rooted by moving its
        /// existing <c>Assets</c> tree wholesale inside <c>Resources</c>, so a manifest path of
        /// <c>CartoonCubeWorld/…/Apple.prefab</c> is reachable as <c>Assets/CartoonCubeWorld/…/Apple</c>.
        /// Keeping the tree shape identical is what lets the manifest's own path field stay valid
        /// across the move — it is relative to the pack root either way.
        /// </remarks>
        const string ResourcePrefix = "Assets/";

        /// <summary>Where the prefabs live in the project, for the editor-only fallback: the kit tree, or its older home under Resources.</summary>
        static readonly string[] PackageAssetRoots =
        {
            "Packages/com.sidequest.low-poly-assets-processed/Kit/Assets/",
            "Packages/com.sidequest.low-poly-assets-processed/Resources/Assets/",
        };

        /// <summary>
        /// Set by the app when the kit ships as downloadable bundles: loads the prefab for a manifest path, downloading
        /// it first if needed. A null result (or an exception) falls back to Resources and the editor's AssetDatabase.
        /// Null (the default) keeps the synchronous Resources path.
        /// </summary>
        public static Func<string, Task<GameObject>> BundleResolver;

        GameObject item;

        /// <summary>Bumped by every setup and by destroy: a bundle load that finishes for an older one is dropped.</summary>
        int setupVersion;

        void SetupKitAsset()
        {
            setupVersion++;
            if (item != null)
            {
                Destroy(item);
                item = null;
            }

            /*
             * Failures are logged HERE as well as handed to SetLoadedIfNot, and that is not
             * belt-and-braces — it is the only way most of them are ever seen.
             *
             * SetLoadedIfNot latches on `_loaded` and reports exactly once per component, ever. The
             * first call wins even when it is the successful one, so a kit asset that loads and is
             * then pointed at a path that does not resolve says nothing at all: no Unity log, no
             * `loaded` event, and an object that silently stays empty. Every later failure was
             * invisible, which is precisely the state "kit items do not load and there is nothing
             * in the console" describes.
             */
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogWarning($"[BSKitAsset] '{name}' has no path — nothing to instantiate.");
                SetLoadedIfNot(false, "KitAsset has no path.");
                return;
            }

            if (BundleResolver != null)
            {
                _ = SetupFromBundleAsync(path, setupVersion);
                return;
            }
            SpawnFrom(Resolve(path), path);
        }

        async Task SetupFromBundleAsync(string manifestPath, int version)
        {
            GameObject prefab = null;
            try { prefab = await BundleResolver(manifestPath); }
            catch (Exception e)
            {
                if (this != null && version == setupVersion)
                    Debug.LogWarning($"[BSKitAsset] '{name}': the kit bundle for '{manifestPath}' did not load ({e.Message}); trying Resources.");
            }
            // Destroyed, or pointed at another path, while the bundle loaded: that newer setup owns the object.
            if (this == null || version != setupVersion) return;
            SpawnFrom(prefab != null ? prefab : Resolve(manifestPath), manifestPath);
        }

        void SpawnFrom(GameObject prefab, string path)
        {
            if (prefab == null)
            {
                // The resolved Resources key is in the message on purpose: the manifest path and the
                // key are different strings (the extension is stripped and "Assets/" prepended), and
                // which of the two is wrong is the whole question when one of these fails.
                Debug.LogWarning($"[BSKitAsset] '{name}' could not resolve '{path}' — tried "
                               + $"Resources.Load(\"{ResourcePrefix + StripExtension(path)}\")"
                               + " and the editor AssetDatabase fallback. Is "
                               + "com.sidequest.low-poly-assets-processed present and processed?");
                SetLoadedIfNot(false, $"KitAsset not found at path: {path}");
                return;
            }

            try
            {
                // Parented with worldPositionStays: false, so the spawned prefab sits exactly on the
                // object the editor created for it and the gizmo moves the two as one.
                item = Instantiate(prefab, transform, false);
                item.transform.localPosition = Vector3.zero;
                item.transform.localRotation = Quaternion.identity;

                scene.kitItems.Add(item);
                // Onto the SQ-lit diffuse family, so the piece receives the fake sun
                // and baked AO like every editor primitive. Here rather than on the
                // `loaded` event, which is latched once-per-component and never fires
                // again for the re-instantiate a path change causes.
                KitMaterialLighting.Convert(item);
                SetLoadedIfNot();
            }
            catch (Exception e)
            {
                Debug.LogError($"[BSKitAsset] '{name}' failed to instantiate '{path}': {e.Message}");
                SetLoadedIfNot(false, e.Message);
            }
        }

        /// <summary>
        /// Find the prefab, preferring the path that also works in a build.
        /// </summary>
        /// <remarks>
        /// Resources first, AssetDatabase second, and the order is the point: Resources is the only
        /// branch that exists in a player build, so it has to be the one that normally hits. The
        /// editor fallback is now just a safety net for a working tree mid-move — putting it first
        /// would mean everything worked in the editor right up until the first build, where nothing
        /// would, and nothing about that failure would point back to this method.
        /// </remarks>
        static GameObject Resolve(string manifestPath)
        {
            var resourcePath = ResourcePrefix + StripExtension(manifestPath);
            var prefab = Resources.Load<GameObject>(resourcePath);
            if (prefab != null) return prefab;

#if UNITY_EDITOR
            foreach (var root in PackageAssetRoots)
            {
                prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(root + manifestPath);
                if (prefab != null) return prefab;
            }
#endif

            return null;
        }

        static string StripExtension(string value)
        {
            var dot = value.LastIndexOf('.');
            var slash = value.LastIndexOf('/');
            return dot > slash ? value.Substring(0, dot) : value;
        }

        internal override void DestroyStuff()
        {
            setupVersion++; // a bundle load still in flight must not spawn into a destroyed component
            if (item != null)
            {
                scene.kitItems.Remove(item);
                // The instantiated prefab is this component's artefact, exactly as the generated
                // mesh is BSGeometry's — de-registering without destroying left it orphaned in the
                // scene, and a remove→undo cycle then instantiated a second copy beside it.
                Destroy(item);
                item = null;
            }
        }

        internal override void UpdateStuff() { }
        internal override void StartStuff()
        {
            if (!valuesApplied)
            {
                // Placed in the Inspector, so nothing has instantiated the path set there yet (see
                // valuesApplied). Without one there is nothing to load, and it counts as loaded
                // rather than failing the space's load.
                if (string.IsNullOrEmpty(path))
                {
                    SetLoadedIfNot();
                }
                else
                {
                    ReSetup();
                }
            }
        }

        internal void UpdateCallback(List<PropertyName> changedProperties)
        {
            valuesApplied = true;
            SetupKitAsset();
        }
    }
}