using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BS
{
    // Created from scripts only (the page's BS.AssetBundle, the snippet runtime), so not offered in Add Component.
    [AddComponentMenu("")]
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BSObjectId))]
    [WatchComponent]
    public partial class BSAssetBundle : BSComponentBase
    {
        [Tooltip("The URL to the Windows asset bundle.")]
        [See(initial = "")][SerializeField] internal string windowsUrl = "";

        [Tooltip("The URL to the OSX asset bundle.")]
        [See(initial = "")][SerializeField] internal string osxUrl = "";

        [Tooltip("The URL to the Linux asset bundle.")]
        [See(initial = "")][SerializeField] internal string linuxUrl = "";

        [Tooltip("The URL to the Android asset bundle.")]
        [See(initial = "")][SerializeField] internal string androidUrl = "";

        [Tooltip("The URL to the iOS asset bundle.")]
        [See(initial = "")][SerializeField] internal string iosUrl = "";

        [Tooltip("The URL to the Vision OS asset bundle.")]
        [See(initial = "")][SerializeField] internal string vosUrl = "";

        [Tooltip("Indicates whether this asset bundle contains a scene or a collection of prefabs.")]
        [See(initial = "false")][SerializeField] internal bool isScene = false;

        [Tooltip("Enables a legacy shader fix for compatibility with older lighting models.")]
        [See(initial = "false")][SerializeField] internal bool legacyShaderFix = false;


        // Serialized, as it always was (part of the SDK's surface): a copy made with Instantiate comes holding its
        // original's bundle, as a twin does. Only an owner unloads it, and ownsBundle isn't serialized.
        [Tooltip("The loaded asset bundle.")]
        public AssetBundle assetBundle;
        List<string> assetPaths;
        bool isLoading = false;
        // UpdateCallback has set this component up. Not serialized (valuesApplied is): a copy made with Instantiate
        // brings valuesApplied but has run no setup, and its original owns the bundle.
        bool setupRan;
        // This component loaded assetBundle, as opposed to sharing a twin's: only the owner unloads it.
        bool ownsBundle;
        // The URL of the bundle this component holds, set with it on success only; and the URL it is loading, visible
        // to twins (LoaderOf) only once it has stopped waiting for earlier unloads of that URL.
        string currentUrl;
        string loadingUrl;
        // Waiting for a twin's load of loadingUrl, rather than downloading it (LoaderOf prefers the one downloading).
        bool waitingForTwin;
        // Completed when the load in flight ends: what Unload and twins wait for, without polling a coroutine.
        TaskCompletionSource<bool> loadEnded;
        // Cancelled by Unload: a download nobody will use stops instead of running on for minutes.
        System.Threading.CancellationTokenSource loadCancel;

#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
        /// <summary>Tests only: replaces the download (URL, cancellation) with a bundle of their own. Not in players.</summary>
        internal static Func<string, System.Threading.CancellationToken, Task<AssetBundle>> DownloadOverride;
        /// <summary>Tests only: replaces AssetBundle.UnloadAsync(true), and counts. Not in players.</summary>
        internal static Func<AssetBundle, Task> UnloadOverride;
#endif
        // Scene loads started by world bundles, ever; worldSceneGeneration is the one that loaded worldScene. A newer one
        // means a newer world's Single load is coming, and a Void load now would queue after it and replace it.
        static int sceneLoadGeneration;
        int worldSceneGeneration;
        // Unloads in progress, by URL, each from the start of its Unload (its wait for a load in flight included): a quick
        // rejoin of the world just left must wait, or Unity refuses the second copy of the same files. Main thread.
        static readonly Dictionary<string, HashSet<Task>> unloadingByUrl = new Dictionary<string, HashSet<Task>>();
        /// <summary>
        /// How long a load waits for earlier unloads of its URL: above what an unload waits for itself (a newer world's
        /// scene, NewerWorldWaitSeconds; the requests before a download), since one cut short by Play stopping never ends.
        /// </summary>
        internal static float PendingUnloadCapSeconds = 2 * NewerWorldWaitSeconds;

        // Statics outlive Play without a domain reload: start each session clean.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            unloadingByUrl.Clear();
            sceneLoadGeneration = 0;
            stillLoadingSeenAt = float.NegativeInfinity;
            voidBuildIndex = -2;
        }
        // Set by Unload, cleared by the next setup: a download that finishes after its bundle was unloaded (the player
        // left the world mid-download) neither loads its scene nor registers its kit prefabs.
        bool unloadRequested;
        // The world scene this bundle loaded. Unload leaves it (for Void) before it releases the bundle's assets.
        Scene worldScene;
        // StartStuff reported a placed bundle with no URL for this platform as loaded with nothing to load.
        bool reportedWithoutUrl;

        // The loaded section's BasisBundleGenerated.RenderingPath; null for raw bundles and for
        // .bee bundles built before the field existed.
        string sceneRenderingPath;

        /// <summary>
        /// Raised for every scene bundle just before its scene loads (Single mode), with the scene
        /// name and the rendering path its bundle recorded (<c>BasisBundleGenerated.RenderingPath</c>
        /// values, or null when unknown: a raw bundle or an older build). The host uses it to match
        /// its renderer to the world before the first frame renders.
        /// </summary>
        public static event Action<string, string> SceneBundleLoading;

        /// <summary>
        /// Raised on the main thread just before Void or a world scene replaces the loaded scenes
        /// (<c>LoadSceneMode.Single</c>). <see cref="SingleSceneLoadFinished"/> follows once that load has finished, with
        /// its operation, or with null when it didn't start. After every Single load Unity runs
        /// <c>Resources.UnloadUnusedAssets</c> by itself, a few frames after the load finishes; its scan of the managed heap
        /// stops every thread that has run managed code, so the host can pause its audio output across it.
        /// </summary>
        public static event Action SingleSceneLoadStarting;

        /// <inheritdoc cref="SingleSceneLoadStarting"/>
        public static event Action<AsyncOperation> SingleSceneLoadFinished;

        /// <summary>Starts a Single scene load between <see cref="SingleSceneLoadStarting"/> and <see cref="SingleSceneLoadFinished"/>.</summary>
        static AsyncOperation LoadSingle(Func<AsyncOperation> load)
        {
            try
            {
                SingleSceneLoadStarting?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            AsyncOperation operation = null;
            try
            {
                operation = load();
            }
            finally
            {
                // Called at once if the operation has already finished.
                if (operation != null) operation.completed += RaiseSingleSceneLoadFinished;
                else RaiseSingleSceneLoadFinished(null);
            }
            return operation;
        }

        static void RaiseSingleSceneLoadFinished(AsyncOperation operation)
        {
            try
            {
                SingleSceneLoadFinished?.Invoke(operation);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        internal override void StartStuff()
        {
            // Created from a script (the page, the snippet runtime, C# or Visual Scripting setting its URLs): UpdateCallback
            // has already loaded it. Placed in a scene or prefab (see valuesApplied), nothing had loaded its URLs, and it
            // never reported loaded, so it held the space's load for good.
            if (valuesApplied)
            {
                // A copy made with Instantiate: its original owns the bundle, and it has nothing of its own to load.
                if (!setupRan) SetLoadedIfNot();
                return;
            }
            if (string.IsNullOrEmpty(PlatformUrl()))
            {
                // Nothing to load on this platform, unless a script sets a URL this frame (a Visual Scripting On Start
                // may run after this Start): reported next frame.
                if (Application.isPlaying) ReportNothingToLoadNextFrame();
                else ReportNothingToLoad();
            }
            else if (isScene)
            {
                // Scene bundles load Single: this one would replace the world it sits in.
                Debug.LogWarning($"[Banter] Placed scene AssetBundle '{BSLoadGate.Printable(name)}' ignored: only the page loads a world's scene.");
                SetLoadedIfNot();
            }
            else
            {
                ReSetup();
            }
        }

        // Not a coroutine on this bundle: deactivating or destroying it this frame would end that, and the report it owes
        // would never come (a critical bundle: the space would never open). The async runner is a separate, persistent
        // object. Next frame rather than the end of this one: WaitForEndOfFrame never fires in batch mode.
        async void ReportNothingToLoadNextFrame()
        {
            await new WaitForUpdate();
            ReportNothingToLoad(); // destroyed or not: the space's load still counts it
        }

        /// <summary>
        /// Reports a bundle with nothing to load as loaded, unless a script has set it up meanwhile. Touches no Unity
        /// object, so it reports for a bundle destroyed or deactivated since, too.
        /// </summary>
        internal void ReportNothingToLoad()
        {
            if (setupRan || _loaded) return;
            reportedWithoutUrl = true;
            SetLoadedIfNot();
        }

        /// <summary>The URL LoadBundle loads on this platform (the same #if chain).</summary>
        string PlatformUrl()
        {
#if UNITY_STANDALONE_WIN
            return windowsUrl;
#elif UNITY_STANDALONE_OSX
            return osxUrl;
#elif UNITY_STANDALONE_LINUX
            return linuxUrl;
#elif UNITY_ANDROID
            return androidUrl;
#elif UNITY_VISIONOS
            return vosUrl;
#elif UNITY_IOS
            return iosUrl;
#else
            return null;
#endif
        }

        internal override void UpdateStuff()
        {

        }
        private async Task SetupBundle(List<PropertyName> changedProperties)
        {
            unloadRequested = false;
            if (isScene)
            {
                // Visual Scripting or C# can get here at Play start, before the page's first load created the settings.
                if (!await scene.WhenSettingsReady("Scene bundle '" + name + "'") || this == null) return;
                if (scene.settings.SceneAssetBundle)
                {
                    SetLoadedIfNot(false, "Scene bundle already registered!");
                    return;
                }
                scene.settings.SceneAssetBundle = this;
            }
#if !GREENFIELD_PROJECT
            if (isScene)
            {
                SetLoadedIfNot();
                return;
            }
#endif
            try
            {
                await LoadBundle(changedProperties);
            }
            catch (Exception e)
            {
                SetLoadedIfNot(false, e.Message);
                LogLine.Do(Color.red, LogTag.Banter, e.Message);
            }
        }

        internal async Task AfterBundleLoad()
        {
            // A download started from Visual Scripting or C# at Play start can finish before the page's first load
            // created the settings (both branches use them): register once they exist, unless this bundle was
            // destroyed or unloaded meanwhile.
            if (this == null || assetBundle == null || unloadRequested) return;
            if (!await scene.WhenSettingsReady("Asset bundle '" + name + "'") || this == null || assetBundle == null || unloadRequested) return;
            if (isScene)
            {
                await SetupSceneBundle();
            }
            else
            {
                assetPaths = assetBundle.GetAllAssetNames().ToList();
                if (!scene.settings.KitBundles.Contains(this)) scene.settings.KitBundles.Add(this);
                foreach (var path in assetPaths)
                {
                    if (path.EndsWith(".prefab"))
                    {
                        // TryAdd: a twin (the same kit placed twice, sharing one bundle) has registered the path already.
                        scene.settings.KitPaths.TryAdd(path, this);
                    }
                }
            }
        }

        internal async Task LoadBundle(List<PropertyName> changedProperties)
        {
            if (isLoading)
            {
                LogLine.Do("Waiting for bundle to load before loading again...");
                // Again after each: another queued load may have started first.
                while (isLoading && loadEnded != null) await loadEnded.Task;
                // Destroyed or unloaded while queued: don't start a download nobody will keep.
                if (this == null) return;
                if (unloadRequested) { SetLoadedIfNot(); return; }
            }
            try
            {
#if UNITY_STANDALONE_WIN
            if(changedProperties.Contains(PropertyName.windowsUrl))
            {
                BeginLoading();
                await LoadPlatformBundle(windowsUrl, progress: progress =>
                {
                    this.progress?.Invoke(progress);
                });
                    
                await AfterBundleLoad();
                SetLoadedIfNot();
            }
#elif UNITY_STANDALONE_OSX
            if(changedProperties.Contains(PropertyName.osxUrl))
            {
                BeginLoading();
                await LoadPlatformBundle(osxUrl, progress: progress => this.progress?.Invoke(progress));
                await AfterBundleLoad();
                SetLoadedIfNot();
            }
#elif UNITY_STANDALONE_LINUX
            if(changedProperties.Contains(PropertyName.linuxUrl))
            {
                BeginLoading();
                await LoadPlatformBundle(linuxUrl, progress: progress => this.progress?.Invoke(progress));
                await AfterBundleLoad();
                SetLoadedIfNot();
            }
#elif UNITY_ANDROID
            if(changedProperties.Contains(PropertyName.androidUrl))
            {
                BeginLoading();
                await LoadPlatformBundle(androidUrl, progress: progress => this.progress?.Invoke(progress));
                await AfterBundleLoad();
                SetLoadedIfNot();
            }
#elif UNITY_VISIONOS
            if(changedProperties.Contains(PropertyName.vosUrl))
            {
                BeginLoading();
                await LoadPlatformBundle(vosUrl, progress: progress => this.progress?.Invoke(progress));
                await AfterBundleLoad();
                SetLoadedIfNot();
            }
#elif UNITY_IOS
            if(changedProperties.Contains(PropertyName.iosUrl))
            {
                BeginLoading();
                await LoadPlatformBundle(iosUrl, progress: progress => this.progress?.Invoke(progress));
                await AfterBundleLoad();
                SetLoadedIfNot();
            }
#endif      
                if (!isLoading && assetBundle == null && string.IsNullOrEmpty(PlatformUrl()))
                {
                    // No URL for this platform among what was set (a script that set only isScene, say): nothing to
                    // load, and it never reported, so it held the space's load for good.
                    reportedWithoutUrl = true;
                    SetLoadedIfNot();
                }
            }
            catch (Exception e)
            {
                // Cancelled by an Unload, even if a property set since has set the bundle up again (and cleared
                // unloadRequested): Basis reports a cancel as a plain Exception, so the token tells, not the type.
                if (unloadRequested || (loadCancel?.IsCancellationRequested ?? false))
                {
                    // Unloaded meanwhile (its download cancelled, say): it is going, and its load mustn't fail the space.
                    SetLoadedIfNot();
                }
                else
                {
                    SetLoadedIfNot(false, e.Message);
                    LogLine.Do(Color.red, LogTag.Banter, androidUrl + ": " + e.Message);
                }
            }
            finally
            {
                // Once, whatever happened: a loaded listener that throws above (its exception goes on to SetupBundle,
                // which logs it) must not leave this load, and every twin waiting on it, unfinished.
                EndLoading();
            }
        }

        internal void BeginLoading()
        {
            isLoading = true;
            loadEnded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            loadCancel = new System.Threading.CancellationTokenSource();
        }

        internal void EndLoading()
        {
            isLoading = false;
            loadingUrl = null;
            waitingForTwin = false;
            // Not disposed (it holds no timer or wait handle): a token callback can end the load inside Cancel() itself.
            loadCancel = null;
            loadEnded?.TrySetResult(true);
        }


        // Space scene bundles may be Basis-encrypted (.bee content) or a legacy raw AssetBundle — both
        // hosted under the same ".banter" name, because the SideQuest upload API keys off that extension.
        // So we can't branch on the URL; we branch on content. A raw Unity bundle begins with the ASCII
        // "Unity" signature ("UnityFS"/"UnityWeb"/...); a .bee begins with an encrypted length header. Peek
        // the first bytes (a small ranged request) and route accordingly, falling back to raw if unsure.
        // Only scene bundles are ever encrypted — kits and other bundles are always raw, so skip the peek.
        internal async Task<AssetBundle> LoadPlatformBundle(string url, Action<float> progress = null)
        {
            sceneRenderingPath = null;
            if (!string.IsNullOrEmpty(url))
            {
                // Loaded here from this URL already (the page set the same URL again): Unity refuses a second copy.
                if (url == currentUrl && !ReferenceEquals(assetBundle, null)) return assetBundle;
                // This load's own cancellation, captured: an Unload cancels it, and a setup right after (a graph's
                // Unload then Refresh) clears unloadRequested, but must not bring this load back. Once cancelled, it
                // stops at the next check for good: its waits complete at once from then on, and looping again would
                // spin the main thread.
                var cancel = loadCancel;
                var waited = new HashSet<Task>();
                for (var pass = 0; ; pass++)
                {
                    if (pass > 0)
                    {
                        // Belt and braces: every pass after a wait starts on a later frame, so no future change can make
                        // this loop spin without yielding; and it can't go round for ever.
                        await Task.Yield();
                        if (Stopped(cancel)) return assetBundle;
                        if (pass >= MaxLoadPasses)
                        {
                            Debug.LogError($"[Banter] AssetBundle '{BSLoadGate.Printable(name)}' gave up waiting to load " +
                                           $"{BSLoadGate.Printable(url)} after {MaxLoadPasses} rounds of waiting for twins and unloads.");
                            return assetBundle;
                        }
                    }
                    // The same bundle still unloading (a quick rejoin of the world just left, or of one left mid
                    // download): Unity refuses a second copy of its files until it has gone. Others may start meanwhile.
                    var unloads = UnloadsOf(url, waited);
                    if (unloads.Length > 0)
                    {
                        await Task.WhenAny(Task.WhenAll(unloads), Task.Delay(TimeSpan.FromSeconds(PendingUnloadCapSeconds)), WhenCancelled(cancel));
                        // Destroyed, unloaded or cancelled meanwhile: what it holds stays its own to release (Unload).
                        if (Stopped(cancel)) return assetBundle;
                        continue;
                    }
                    if (isScene) break;
                    var all = FindObjects.All<BSAssetBundle>(FindObjectsInactive.Include);
                    // The same kit placed twice: a twin that holds it shares it now, even while that twin loads another
                    // URL (HeirFor takes it over if the twin lets go).
                    var holder = HolderOf(this, url, all);
                    if (holder != null)
                    {
                        var shared = holder.assetBundle;
                        await Adopt(shared, url, owned: false);
                        return shared;
                    }
                    // One is downloading it: wait for that download, then look again. Only downloaders are waited on, and
                    // a downloader waits for no twin, so no wait can come round in a circle. If it fails or goes, the
                    // first waiter to look again downloads (looking and marking itself are one step) and the rest wait
                    // on that one. Unloaded meanwhile, this stops waiting at once.
                    var loader = LoaderOf(this, url, all);
                    if (loader == null) break;
                    loadingUrl = url;
                    waitingForTwin = true;
                    await Task.WhenAny(loader.loadEnded.Task, WhenCancelled(cancel));
                    waitingForTwin = false;
                    loadingUrl = null;
                    if (Stopped(cancel)) return assetBundle;
                }
                // Only now visible to twins: two that waited above for the same unload would otherwise each find the
                // other loading, and each wait for the other.
                loadingUrl = url;
            }
            var cancelAtDownload = loadCancel;
            var bundle = await DownloadPlatformBundle(url, progress);
            if (Stopped(cancelAtDownload))
            {
                // Unloaded while it downloaded (too late to cancel): not taken, and released at once.
                await Release(bundle);
                return assetBundle;
            }
            await Adopt(bundle, url, owned: true);
            return bundle;
        }

        /// <summary>
        /// Takes <paramref name="bundle"/>, from <paramref name="url"/>, once it has loaded: the bundle, who owns it and its
        /// URL change together. A bundle held from an earlier URL is released (or handed to a twin) first; it used to be
        /// overwritten, and leaked.
        /// </summary>
        async Task Adopt(AssetBundle bundle, string url, bool owned)
        {
            var previous = assetBundle;
            var previousOwned = ownsBundle;
            var previousUrl = currentUrl;
            // The new one first: a twin leaving while the old one is released must find this one holding the new one
            // (HeirFor), or it unloads it from under this one.
            assetBundle = bundle;
            ownsBundle = owned;
            currentUrl = url;
            loadingUrl = null;
            if (!ReferenceEquals(previous, null) && !ReferenceEquals(previous, bundle))
            {
                await Registered(previousOwned ? previousUrl : null, ReleaseBundle(previous, previousOwned, previousUrl));
            }
        }

        private async Task<AssetBundle> DownloadPlatformBundle(string url, Action<float> progress)
        {
            var cancellation = loadCancel?.Token ?? System.Threading.CancellationToken.None;
#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
            if (DownloadOverride != null) return await DownloadOverride(url, cancellation);
#endif
            if (PageUrl.Refuse(url, nameof(BSAssetBundle)))
            {
                throw new InvalidOperationException(BSAssetKey.WebAddressesOnly);
            }
            if (isScene && !string.IsNullOrEmpty(url))
            {
                byte[] head = await Get.PeekHeader(url, 8);
                bool looksUnity = head != null && LooksLikeUnityBundle(head);
                Debug.Log($"[Greenfield/BEE] Bundle sniff for {url}: " +
                          (head == null
                              ? "header peek FAILED (null) — falling back to raw AssetBundle load."
                              : $"header=[{DescribeHeader(head)}], looksLikeUnity={looksUnity} → " +
                                (looksUnity ? "raw AssetBundle load." : "Basis encrypted (.bee) decrypt.")));
                if (head != null && !looksUnity)
                {
                    // A world is published to a static url, so the cache cannot tell "same address,
                    // new bytes" from "same address, same bytes" on its own — without a version tag
                    // a re-uploaded world is never re-downloaded. Not harvested from the peek above:
                    // that is a ranged request, and a 206 carries no ETag or Last-Modified.
                    string versionTag = await BasisContentVersion.ResolveRequestedTagAsync(url);
                    var (bundle, section) = await Get.EncryptedAssetBundleWithSection(url, GreenfieldBundleCrypto.Password, progress, versionTag, cancellation, BasisPreLoadCheck.Kind.World);
                    sceneRenderingPath = section?.RenderingPath;
                    return bundle;
                }
            }
            else
            {
                Debug.Log($"[Greenfield/BEE] Skipping .bee sniff (isScene={isScene}, url={(string.IsNullOrEmpty(url) ? "<empty>" : url)}) — raw AssetBundle load.");
            }
            return await Get.AssetBundle(url, progress, cancellation, isScene ? BasisPreLoadCheck.Kind.World : BasisPreLoadCheck.Kind.Kit);
        }

        /// <summary>
        /// Another kit bundle holding <paramref name="url"/>'s bundle; never one being unloaded (an old instance about to
        /// release its bundle, when the world is rejoined while it still loads).
        /// </summary>
        internal static BSAssetBundle HolderOf(BSAssetBundle self, string url, IEnumerable<BSAssetBundle> all)
        {
            foreach (var other in all)
            {
                if (IsTwinCandidate(self, other) && other.currentUrl == url && !ReferenceEquals(other.assetBundle, null)) return other;
            }
            return null;
        }

        /// <summary>
        /// Another kit bundle downloading <paramref name="url"/>; never one only waiting for a twin's download (two of those
        /// could each pick the other, and wait for good), nor one being unloaded.
        /// </summary>
        internal static BSAssetBundle LoaderOf(BSAssetBundle self, string url, IEnumerable<BSAssetBundle> all)
        {
            foreach (var other in all)
            {
                if (IsTwinCandidate(self, other) && other.loadingUrl == url && other.isLoading && other.loadEnded != null
                    && !other.waitingForTwin)
                {
                    return other;
                }
            }
            return null;
        }

        /// <summary>Destroyed, unloaded, or this load (<paramref name="cancel"/>) cancelled.</summary>
        bool Stopped(System.Threading.CancellationTokenSource cancel) =>
            this == null || unloadRequested || (cancel?.IsCancellationRequested ?? false);

        /// <summary>How many rounds of waiting a load may go through before it gives up (each starts a frame later).</summary>
        internal const int MaxLoadPasses = 1000;

        /// <summary>Completes when an Unload cancels the load <paramref name="cancel"/> belongs to; never, when there is none.</summary>
        static Task WhenCancelled(System.Threading.CancellationTokenSource cancel)
        {
            if (cancel == null) return new TaskCompletionSource<bool>().Task;
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            cancel.Token.Register(() => cancelled.TrySetResult(true));
            return cancelled.Task;
        }

        static bool IsTwinCandidate(BSAssetBundle self, BSAssetBundle other) =>
            other != null && other != self && !other.isScene && !other.unloadRequested;

        /// <summary>The kit bundle that takes over <paramref name="bundle"/> from its departing owner: a twin sharing it.</summary>
        internal static BSAssetBundle HeirFor(BSAssetBundle owner, AssetBundle bundle, IEnumerable<BSAssetBundle> all)
        {
            foreach (var other in all)
            {
                if (other == owner || other.isScene || other.unloadRequested) continue;
                if (ReferenceEquals(other.assetBundle, bundle)) return other;
            }
            return null;
        }

        /// <summary>Points <paramref name="from"/>'s kit paths at <paramref name="to"/>, or drops them when there is none.</summary>
        internal static void RepointKitPaths(Dictionary<string, BSAssetBundle> paths, BSAssetBundle from, BSAssetBundle to)
        {
            foreach (var path in paths.Where(p => p.Value == from).Select(p => p.Key).ToList())
            {
                if (to != null) paths[path] = to;
                else paths.Remove(path);
            }
        }

        // Compact "UnityFS | 55 6E 69 74 79 46 53 00" style dump of the sniffed header for logging.
        private static string DescribeHeader(byte[] head)
        {
            var ascii = new System.Text.StringBuilder(head.Length);
            var hex = new System.Text.StringBuilder(head.Length * 3);
            foreach (byte b in head)
            {
                ascii.Append(b >= 0x20 && b < 0x7F ? (char)b : '.');
                hex.Append(b.ToString("X2")).Append(' ');
            }
            return ascii + " | " + hex.ToString().TrimEnd();
        }

        // Unity AssetBundle files begin with an ASCII signature: "UnityFS" (current), also
        // "UnityWeb"/"UnityRaw"/"UnityArchive". A Basis .bee never starts with "Unity".
        /// <summary>
        /// Removes every component on <paramref name="target"/>'s GameObject that [RequireComponent]s it, and in
        /// turn whatever requires those, so <paramref name="target"/> itself can then be destroyed. Without this
        /// a world's EventSystem survives whenever an input module other than StandaloneInputModule rides along
        /// (InputSystemUIInputModule, XR modules: every BaseInputModule requires EventSystem), and Unity logs
        /// "Can't remove EventSystem because ... depends on it". Immediate, because Unity checks dependencies
        /// when Destroy is called, not when the deferred destroy lands.
        /// </summary>
        private static void RemoveDependents(Component target) => RemoveDependents(target, new HashSet<Component> { target });

        // visiting: components already on the removal path, so a RequireComponent cycle can't recurse forever.
        private static void RemoveDependents(Component target, HashSet<Component> visiting)
        {
            foreach (var component in target.GetComponents<Component>())
            {
                if (component == null || visiting.Contains(component) || !Requires(component.GetType(), target.GetType()))
                    continue;
                visiting.Add(component);
                RemoveDependents(component, visiting);
                DestroyImmediate(component);
            }
        }

        private static bool Requires(Type dependent, Type required)
        {
            foreach (RequireComponent attribute in dependent.GetCustomAttributes(typeof(RequireComponent), true))
            {
                if ((attribute.m_Type0 != null && attribute.m_Type0.IsAssignableFrom(required)) ||
                    (attribute.m_Type1 != null && attribute.m_Type1.IsAssignableFrom(required)) ||
                    (attribute.m_Type2 != null && attribute.m_Type2.IsAssignableFrom(required)))
                    return true;
            }
            return false;
        }

        private static bool LooksLikeUnityBundle(byte[] head)
        {
            return head.Length >= 5
                && head[0] == (byte)'U' && head[1] == (byte)'n' && head[2] == (byte)'i'
                && head[3] == (byte)'t' && head[4] == (byte)'y';
        }

        private async Task SetupSceneBundle()
        {
            var scenes = assetBundle.GetAllScenePaths();
            if (scenes.Length > 0)
            {
                string sceneName = System.IO.Path.GetFileNameWithoutExtension(scenes[0]);
                try
                {
                    SceneBundleLoading?.Invoke(sceneName, sceneRenderingPath);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
                // Counted before the load starts: an older world's unload can then tell that this Single load is coming,
                // and doesn't load Void over it (LeaveWorldScene).
                var generation = ++sceneLoadGeneration;
                // Persistent UnityEvent calls, AnimationEvents and host components are judged as the scene loads,
                // before any Start; a world that fails, or that the guard never saw load, is unloading to Void
                // (BSUnityEventGuard).
                using (var guard = BSUnityEventGuard.SanitizeWhenLoaded(scenes[0], sceneName))
                {
                    await LoadSingle(() => SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single));
                    if (!guard.Complete())
                    {
                        SetLoadedIfNot(false, "The world failed its content check.");
                        return;
                    }
                }
                if (legacyShaderFix)
                {
                    RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
                    RenderSettings.ambientSkyColor = Color.white;
                    RenderSettings.ambientEquatorColor = Color.white;
                    RenderSettings.ambientGroundColor = new Color(0.1f, 0.1f, 0.1f, 1f);
                }
                var reference = SceneManager.GetSceneByName(sceneName);
                worldScene = reference;
                worldSceneGeneration = generation;

                // Re-resolve shaders from bundle materials via Shader.Find().
                // Compiled shaders in asset bundles may be incompatible with the
                // current project context. Re-finding by name forces the project's
                // compiled shader version to be used instead.
                // Shader remapper removed — the root cause of bundle shaders not
                // rendering was project configuration (BRG stripping + URP pipeline
                // asset settings), not shader incompatibility.

                foreach (GameObject thisgo in reference.GetRootGameObjects())
                {
                    foreach (Transform transform in thisgo.GetComponentsInChildren<Transform>(true))
                    {
                        var body = transform.gameObject.GetComponent<Rigidbody>();
                        if (body != null)
                        {
                            GuardConcaveColliders(body);
                        }
                        var inputModule = transform.gameObject.GetComponent<StandaloneInputModule>();
                        if (inputModule != null)
                        {
                            Destroy(inputModule);
                        }
                        var eventSystem = transform.gameObject.GetComponent<EventSystem>();
                        if (eventSystem != null)
                        {
                            RemoveDependents(eventSystem);
                            Destroy(eventSystem);
                        }
                        var starterUpper = transform.gameObject.GetComponent<BSStarterUpper>();
                        if (starterUpper != null)
                        {
                            Destroy(starterUpper);
                        }
                        var audioListener = transform.gameObject.GetComponent<AudioListener>();
                        if (audioListener != null)
                        {
                            Destroy(audioListener);
                        }
                        var audioSource = transform.gameObject.GetComponent<AudioSource>();
                        if (audioSource != null)
                        {
                            // App-supplied per-source policy first (e.g. music vs. gameplay), then
                            // the single default group. Only assign if we actually resolved one, so
                            // a scene with no routing configured keeps the bundle's own value.
                            RouteToApp(audioSource, scene.settings.AudioGroupSelector?.Invoke(audioSource)
                                                    ?? scene.settings.SpaceAudioGroup);
                        }
                        var canvas = transform.gameObject.GetComponent<Canvas>();
                        if (canvas != null)
                        {
                            if (canvas.renderMode == RenderMode.WorldSpace)
                            {
                                canvas.worldCamera = Camera.main;
                                // Keep the binding alive: a null/stale worldCamera silently kills
                                // TrackedDeviceRaycaster (Quest races XR camera activation).
                                if (!canvas.GetComponent<CanvasCameraBinder>())
                                    canvas.gameObject.AddComponent<CanvasCameraBinder>();
                                if (!canvas.GetComponent<BoxCollider>())
                                {
                                    var box = canvas.AddComponent<BoxCollider>();
                                    var rt = canvas.GetComponent<RectTransform>();
                                    box.isTrigger = true;
                                    box.size = new Vector3(rt.rect.width, rt.rect.height, 0.01f);
                                    box.center = new Vector3(0f, 0f, 0.015f);

                                    // Founds some spaces that have huge canvases with no rectT scaling, so cap it
                                    if (rt.localScale.x >= 1f && rt.rect.width > 10f)
                                    {
                                        box.enabled = false;
                                    }
                                }
                                // Ensure stock raycasters are present for XRPointerInputModule's
                                // RaycastAll() to find Canvas worldspace hits.
                                if (!canvas.GetComponent<UnityEngine.InputSystem.UI.TrackedDeviceRaycaster>())
                                    canvas.gameObject.AddComponent<UnityEngine.InputSystem.UI.TrackedDeviceRaycaster>();
                                if (!canvas.GetComponent<GraphicRaycaster>())
                                    canvas.gameObject.AddComponent<GraphicRaycaster>();
                            }
                        }

                        BSText.RepairFont(transform.gameObject.GetComponent<TMP_Text>());
                    }
                }
            }
        }

        /// <summary>
        /// Puts a world's AudioSource under the app's volume sliders. A source with no mixer of its own plays through
        /// <paramref name="group"/>. One routed through the creator's mixer stays there and that mixer plays through
        /// <paramref name="group"/>, so its groups and effects still apply. A mixer has one output, so it follows the
        /// first source that asks, and a source that belongs under another of the app's groups skips the mixer.
        /// </summary>
        internal static void RouteToApp(AudioSource source, AudioMixerGroup group)
        {
            if (group == null)
            {
                return;
            }
            var own = source.outputAudioMixerGroup;
            var chained = false;
            try
            {
                chained = own != null && ChainUnder(own.audioMixer, group);
            }
            catch (Exception e)
            {
                // One mixer that won't chain mustn't stop the rest of the world's setup.
                Debug.LogWarning($"[Audio] Couldn't keep {source.name}'s mixer, so it plays through the app's: {e.Message}");
            }
            if (!chained)
            {
                source.outputAudioMixerGroup = group;
            }
        }

        // Follows a mixer's output to the end of the creator's chain and sends that into the app's group. Bounded, as
        // nothing stops a bundle's mixers from naming each other. False when the mixer can't be put under the group,
        // which includes one already feeding a different group of the app's.
        static bool ChainUnder(AudioMixer mixer, AudioMixerGroup group)
        {
            for (var hops = 0; mixer != null && hops < 16; hops++)
            {
                if (mixer == group.audioMixer)
                {
                    return true;
                }
                var next = mixer.outputAudioMixerGroup;
                if (next == null)
                {
                    mixer.outputAudioMixerGroup = group;
                    return mixer.outputAudioMixerGroup == group;
                }
                if (next.audioMixer == group.audioMixer)
                {
                    return next == group;
                }
                mixer = next.audioMixer;
            }
            return false;
        }

        /// <summary>
        /// Unity leaves a concave MeshCollider out of a moving body, so a body with nothing else to collide with falls
        /// through the world. That body gets a collider that works: convex when its meshes can be cooked again here,
        /// otherwise it goes kinematic, which can keep a concave shape. A body that collides through anything else, that
        /// gravity doesn't pull down, that can't fall, or that a joint holds, stays as it was built.
        /// </summary>
        internal static void GuardConcaveColliders(Rigidbody body)
        {
            if (body.isKinematic || !body.useGravity || (body.constraints & RigidbodyConstraints.FreezePositionY) != 0)
            {
                return;
            }
            var concave = new List<MeshCollider>();
            foreach (var collider in body.GetComponentsInChildren<Collider>(true))
            {
                if (!collider.enabled || collider.isTrigger || collider.GetComponentInParent<Rigidbody>(true) != body)
                {
                    continue;
                }
                if (!(collider is MeshCollider mesh) || mesh.convex)
                {
                    return;
                }
                if (mesh.sharedMesh != null)
                {
                    concave.Add(mesh);
                }
            }
            if (concave.Count == 0 || IsJointed(body))
            {
                return;
            }
            var cookable = concave.TrueForAll(mesh => mesh.sharedMesh.isReadable);
            if (!cookable)
            {
                body.isKinematic = true;
            }
            foreach (var mesh in concave)
            {
                if (cookable)
                {
                    mesh.convex = true;
                }
                // The shape was refused when the collider woke, so it has to be asked for again.
                mesh.enabled = false;
                mesh.enabled = true;
            }
            Debug.LogWarning($"[Colliders] '{body.name}' is a moving Rigidbody with only concave MeshColliders, which Unity can't " +
                             $"simulate: {(cookable ? "made them Convex" : "made the Rigidbody kinematic")}. Tick Convex, or make it kinematic, in the world.", body);
        }

        static bool IsJointed(Rigidbody body) =>
            body.GetComponent<Joint>() != null ||
            FindObjects.All<Joint>(FindObjectsInactive.Include).Any(joint => joint.connectedBody == body);

        internal static void RegisterUnloading(string url, Task unloading)
        {
            if (string.IsNullOrEmpty(url) || unloading.IsCompleted) return;
            if (!unloadingByUrl.TryGetValue(url, out var pending)) unloadingByUrl[url] = pending = new HashSet<Task>();
            pending.Add(unloading);
        }

        internal static void UnregisterUnloading(string url, Task unloading)
        {
            if (string.IsNullOrEmpty(url) || !unloadingByUrl.TryGetValue(url, out var pending)) return;
            pending.Remove(unloading);
            if (pending.Count == 0) unloadingByUrl.Remove(url);
        }

        /// <summary>Whether an Unload of <paramref name="url"/>'s bundle is in progress. Main thread.</summary>
        internal static bool IsUnloading(string url) => !string.IsNullOrEmpty(url) && unloadingByUrl.ContainsKey(url);

        /// <summary>The unloads of <paramref name="url"/> in progress that aren't in <paramref name="waited"/> yet; adds them.</summary>
        static Task[] UnloadsOf(string url, HashSet<Task> waited)
        {
            if (!unloadingByUrl.TryGetValue(url, out var pending)) return Array.Empty<Task>();
            var fresh = pending.Where(t => !waited.Contains(t)).ToArray();
            foreach (var t in fresh) waited.Add(t);
            return fresh;
        }


        /// <summary>
        /// Releases the bundle and everything loaded from it. UnloadAsync(true) destroys the bundle's RenderTextures too,
        /// and a camera still rendering into one logs "Releasing render texture that is set as Camera.targetTexture!"
        /// (a fifth of sessions, at a world switch), so the world's scene goes first (for Void), and a kit's cameras are
        /// stopped first. Registered under the URL it releases while it runs (a URL change releases the old one too).
        /// </summary>
        Task UnloadNow()
        {
            // Taken first: a second Unload (the next page's settings.Reset, then OnDestroy, while this one waits for Void)
            // finds nothing left to do.
            var bundle = assetBundle;
            var owned = ownsBundle;
            var url = currentUrl;
            assetBundle = null;
            ownsBundle = false;
            currentUrl = null;
            // Play can stop before any page has loaded, when there are no settings yet; and OnLoad may already have
            // swapped in the next space's settings: only clear the entry if it is this bundle.
            if (isScene && scene.settings != null && scene.settings.SceneAssetBundle == this) scene.settings.SceneAssetBundle = null;
            return Registered(owned ? url : null, ReleaseBundle(bundle, owned, url));
        }

        /// <summary>Registers <paramref name="releasing"/> under the URL it releases while it runs (PendingUnloadOf waits for it).</summary>
        static async Task Registered(string url, Task releasing)
        {
            RegisterUnloading(url, releasing);
            try
            {
                await releasing;
            }
            finally
            {
                UnregisterUnloading(url, releasing);
            }
        }

        /// <summary>Releases <paramref name="bundle"/>, which this component held from <paramref name="url"/> and no longer holds.</summary>
        async Task ReleaseBundle(AssetBundle bundle, bool owned, string url)
        {
            if (isScene)
            {
                await LeaveWorldScene();
            }
            else
            {
                // An owner leaving while twins still use its bundle hands it to one of them: it isn't unloaded, its kit
                // paths now name the heir, and the twins' cameras keep rendering.
                var heir = owned ? HeirFor(this, bundle, FindObjects.All<BSAssetBundle>(FindObjectsInactive.Include)) : null;
                if (heir != null)
                {
                    heir.ownsBundle = true;
                    heir.currentUrl = url;
                }
                else if (owned)
                {
                    StopKitCameras(bundle); // a twin leaving stops nothing of its owner's
                }
                if (scene.settings != null)
                {
                    scene.settings.KitBundles.Remove(this);
                    RepointKitPaths(scene.settings.KitPaths, this, heir);
                }
                if (heir != null) return;
            }
            // A twin's bundle is its twin's to unload.
            if (owned) await Release(bundle);
        }

        /// <summary>UnloadAsync(true): everything loaded from <paramref name="bundle"/> goes with it.</summary>
        static async Task Release(AssetBundle bundle)
        {
            if (ReferenceEquals(bundle, null)) return;
            try
            {
#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
                if (UnloadOverride != null) await UnloadOverride(bundle);
                else
#endif
                if (bundle != null) await bundle.UnloadAsync(true);
            }
            catch (Exception e)
            {
                Debug.Log(e.Message);
            }
        }

        /// <summary>
        /// Replaces this bundle's world scene with the empty Void scene, so the world's cameras, renderers and scripts are
        /// gone before the assets they use are destroyed. Only while that scene is still loaded: Void must not load over
        /// a newer world that has already replaced it, or for a bundle that never loaded its scene.
        /// </summary>
        async Task LeaveWorldScene()
        {
            var world = worldScene;
            worldScene = default;
            switch (HowToLeave(world.IsValid() && world.isLoaded, worldSceneGeneration, sceneLoadGeneration))
            {
                case LeaveWorld.Nothing:
                    return;
                case LeaveWorld.WaitForNewerWorld:
                    // A newer world's Single load has started since this one's: it replaces this scene. Void would queue
                    // after it and replace the newer world, so wait for this scene to go instead (bounded: that load may
                    // fail before it replaces anything).
                    var deadline = Time.realtimeSinceStartup + NewerWorldWaitSeconds;
                    await new WaitUntil(() => !world.isLoaded || Time.realtimeSinceStartup > deadline);
                    return;
            }
            try
            {
                var leaving = LoadVoid();
                if (leaving == null)
                {
                    // Play stopping or the app quitting, or no Void scene in the build.
                    Debug.Log("[Banter] Couldn't load Void to leave the world's scene before unloading its bundle.");
                    return;
                }
                await leaving;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Banter] Couldn't leave the world's scene before unloading its bundle: " + e.Message);
            }
        }

        const float NewerWorldWaitSeconds = 60f;

        internal enum LeaveWorld { Nothing, WaitForNewerWorld, LoadVoid }

        /// <summary>
        /// How an unloading world bundle leaves its scene: not at all if the scene isn't loaded (it never loaded, or a
        /// newer world has replaced it); by waiting, if a newer world's scene load has started since; else by loading Void.
        /// </summary>
        internal static LeaveWorld HowToLeave(bool worldSceneLoaded, int worldGeneration, int latestGeneration)
        {
            if (!worldSceneLoaded) return LeaveWorld.Nothing;
            return worldGeneration == latestGeneration ? LeaveWorld.LoadVoid : LeaveWorld.WaitForNewerWorld;
        }

        /// <summary>World scene loads started so far (see sceneLoadGeneration). BSUnityEventGuard's fail-closed Void can compare it too.</summary>
        internal static int SceneLoadGeneration => sceneLoadGeneration;

        static int voidBuildIndex = -2;

        /// <summary>
        /// The build index of the client's empty Void scene, or -1 when the build has none (the Creator SDK's own
        /// projects). By index: a world's own bundle scene could also be called "Void", and loading by name could pick it.
        /// </summary>
        internal static int VoidBuildIndex()
        {
            if (voidBuildIndex == -2)
            {
                voidBuildIndex = -1;
                for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
                {
                    if (System.IO.Path.GetFileNameWithoutExtension(SceneUtility.GetScenePathByBuildIndex(i)) == "Void")
                    {
                        voidBuildIndex = i;
                        break;
                    }
                }
            }
            return voidBuildIndex;
        }

        /// <summary>Starts loading Void (Single); null when the build has none or it can't load now.</summary>
        internal static AsyncOperation LoadVoid()
        {
            var index = VoidBuildIndex();
            return index < 0 ? null : LoadSingle(() => SceneManager.LoadSceneAsync(index, LoadSceneMode.Single));
        }

        /// <summary>
        /// A kit's instances outlive its unload: they hang under the page's DontDestroyOnLoad parent until the next page
        /// destroys it. Stops those of its cameras that render into a texture, which may be one of the bundle's.
        /// </summary>
        static void StopKitCameras(AssetBundle bundle)
        {
            // By reference: a bundle Unity has already destroyed still has kit items that hold it.
            if (ReferenceEquals(bundle, null)) return;
            foreach (var kitItem in FindObjects.All<BSKitItem>(FindObjectsInactive.Include))
            {
                if (!ReferenceEquals(kitItem.KitBundle, bundle)) continue;
                foreach (var camera in kitItem.GetComponentsInChildren<Camera>(true))
                {
                    if (camera.targetTexture == null) continue;
                    camera.enabled = false; // with no target it would draw over the player's view
                    camera.targetTexture = null;
                }
            }
        }

        /// <summary>
        /// Whether this kit bundle may still register prefabs: it hasn't reported loaded, and it is loading, or it is
        /// active and enabled, so its Start will set it up (a placed one's Start can come after a kit item's, in the same
        /// frame). Main thread.
        /// </summary>
        internal bool MayStillRegisterKitPrefabs =>
            !isScene && !_loaded && !unloadRequested && (isLoading || (enabled && gameObject.activeInHierarchy));

        /// <summary>
        /// Whether any kit bundle may still register prefabs (BSKitItem waits for those). Every waiting kit item asks, and
        /// the answer walks every BSAssetBundle: a "yes" is shared for a quarter of a second. A "no" ends their waits, so
        /// it is never reused: a bundle that has appeared since must count. Main thread.
        /// </summary>
        internal static bool AnyKitStillLoading()
        {
            var now = Time.realtimeSinceStartup;
            if (now - stillLoadingSeenAt < 0.25f) return true;
            foreach (var bundle in FindObjects.All<BSAssetBundle>(FindObjectsInactive.Include))
            {
                if (bundle.MayStillRegisterKitPrefabs)
                {
                    stillLoadingSeenAt = now;
                    return true;
                }
            }
            return false;
        }

        static float stillLoadingSeenAt = float.NegativeInfinity;

        public async Task Unload()
        {
            unloadRequested = true;
            // A download in flight that nobody will use stops (a world can take minutes on Quest).
            loadCancel?.Cancel();
            // Registered from the start, the wait for a load in flight included, under both the URL it holds and the one
            // it loads, so a rejoin of either (or F5 mid download) waits for all of it (LoadPlatformBundle).
            var held = currentUrl;
            var loading = loadingUrl ?? (isLoading ? PlatformUrl() : null);
            var unloading = UnloadWhenLoaded();
            RegisterUnloading(held, unloading);
            RegisterUnloading(loading, unloading);
            try
            {
                await unloading;
            }
            finally
            {
                UnregisterUnloading(held, unloading);
                UnregisterUnloading(loading, unloading);
            }
        }

        async Task UnloadWhenLoaded()
        {
            if (isLoading && loadEnded != null)
            {
                // Its scene may be loading right now: let the load finish first. A download still in flight neither loads
                // its scene nor registers its kit prefabs once it lands (unloadRequested).
                LogLine.Do("Waiting for bundle to load before unloading:" + windowsUrl);
                await loadEnded.Task;
                LogLine.Do("Bundle loaded, unloading:" + windowsUrl);
            }
            // By reference: one Unity has destroyed meanwhile still has its world scene, kit paths and cameras to release.
            if (!ReferenceEquals(assetBundle, null))
            {
                await UnloadNow();
            }
        }

        internal override void DestroyStuff()
        {
            Observe(Unload(), "unload");
        }

        // A dropped task's exception surfaced only as an unobserved-task error, much later and with no bundle named.
        void Observe(Task task, string what)
        {
            var label = BSLoadGate.Printable(name);
            task.ContinueWith(t => Debug.LogWarning($"[Banter] AssetBundle '{label}' {what} failed: {t.Exception?.GetBaseException()}"),
                System.Threading.CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
        internal void UpdateCallback(List<PropertyName> changedProperties)
        {
            if (reportedWithoutUrl && !string.IsNullOrEmpty(PlatformUrl()))
            {
                // Loaded below, but the space's load already counted it as done: kit items that looked for its prefabs
                // meanwhile didn't find them.
                reportedWithoutUrl = false;
                Debug.LogWarning($"[Banter] AssetBundle '{BSLoadGate.Printable(name)}' got its URL after it had reported that it had nothing to load.");
            }
            valuesApplied = true;
            setupRan = true;
            Observe(SetupBundle(changedProperties), "setup");
        }
    }
}