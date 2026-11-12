using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// BSAssetBundle's lifecycle without a download (EditMode: no Awake, so no scene is touched, and the coroutine
    /// awaiters don't run; the URL path needs Play, as it goes to the network).
    ///
    /// A bundle placed in a world's scene or a kit prefab never loaded and never reported loaded, and asset bundles are
    /// what the space's load always waits for: such a space never opened. Now a placed one loads its URL, reports loaded
    /// when it has none (next frame, even if deactivated or destroyed by then), and a scene bundle is ignored with a
    /// warning. A copy made with Instantiate has nothing of its own to load. The same kit placed twice shares one bundle,
    /// and the owner leaving hands it to a twin. Leaving a world never loads Void over a newer one, and an unload is
    /// registered from its start, so a quick rejoin waits for it.
    /// "Bundles" here are uninitialised AssetBundle objects: the same reference, with no native bundle to unload.
    /// </summary>
    public class PlacedAssetBundleTests
    {
        const string KitUrl = "https://example.invalid/kit.banter"; // .invalid never resolves; nothing here requests it

        readonly List<GameObject> _objects = new List<GameObject>();
        BSAssetBundle _bundle;

        [SetUp]
        public void SetUp()
        {
            _bundle = NewBundle("PlacedAssetBundleTests");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _objects) if (go != null) Object.DestroyImmediate(go);
            _objects.Clear();
        }

        BSAssetBundle NewBundle(string name)
        {
            var go = new GameObject(name);
            _objects.Add(go);
            return go.AddComponent<BSAssetBundle>();
        }

        static void SetEveryUrl(BSAssetBundle bundle, string url)
        {
            bundle.windowsUrl = bundle.osxUrl = bundle.linuxUrl = bundle.androidUrl = bundle.iosUrl = bundle.vosUrl = url;
        }

        static AssetBundle FakeBundle() => (AssetBundle)FormatterServices.GetUninitializedObject(typeof(AssetBundle));

        static void Set(BSAssetBundle bundle, string field, object value) =>
            typeof(BSAssetBundle).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(bundle, value);

        static T Get<T>(BSAssetBundle bundle, string field) =>
            (T)typeof(BSAssetBundle).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(bundle);

        /// <summary>A kit bundle holding <paramref name="shared"/>, its scene a bare one with these settings.</summary>
        static void Holding(BSAssetBundle bundle, AssetBundle shared, bool owns, string url, BSSceneSettings settings)
        {
            bundle.assetBundle = shared;
            Set(bundle, "ownsBundle", owns);
            Set(bundle, "currentUrl", url);
            var scene = (BSScene)FormatterServices.GetUninitializedObject(typeof(BSScene));
            scene.settings = settings;
            Set(bundle, "_scene", scene);
        }

        static BSSceneSettings Settings()
        {
            var settings = (BSSceneSettings)FormatterServices.GetUninitializedObject(typeof(BSSceneSettings)); // its constructor makes objects
            settings.KitBundles = new List<BSAssetBundle>();
            settings.KitPaths = new Dictionary<string, BSAssetBundle>();
            return settings;
        }

        [Test]
        public void APlacedBundleWithNoUrlForThisPlatform_ReportsLoaded()
        {
            _bundle.StartStuff(); // in Play, next frame
            Assert.IsTrue(_bundle.IsLoaded, "nothing to load, so it mustn't hold the space's load");
        }

        [Test]
        public void TheNoUrlReport_ComesEvenIfTheBundleWasDeactivatedOrDestroyedMeanwhile()
        {
            // It used to be a coroutine on the bundle itself: deactivating it that frame ended it, and the critical
            // bundle never reported. It runs on the shared async runner now, and touches nothing of the bundle's.
            _bundle.gameObject.SetActive(false);
            _bundle.ReportNothingToLoad();
            Assert.IsTrue(_bundle.IsLoaded);

            var destroyed = NewBundle("destroyed");
            Object.DestroyImmediate(destroyed.gameObject);
            destroyed.ReportNothingToLoad();
            Assert.IsTrue(destroyed.IsLoaded, "the space's load still counts its record");
        }

        [Test]
        public void NothingToLoad_IsNotReported_WhenAScriptSetTheBundleUpThatFrame()
        {
            SetEveryUrl(_bundle, KitUrl);
            _bundle.UpdateCallback(new List<PropertyName>()); // a Visual Scripting On Start, after this bundle's Start
            _bundle.ReportNothingToLoad(); // next frame
            Assert.IsFalse(_bundle.IsLoaded, "its own load reports");
        }

        [Test]
        public void APlacedSceneBundle_IsIgnoredWithAWarning()
        {
            SetEveryUrl(_bundle, "https://example.invalid/world.asset");
            _bundle.isScene = true;
            LogAssert.Expect(LogType.Warning, new Regex(@"^\[Banter\] Placed scene AssetBundle 'PlacedAssetBundleTests' ignored"));
            _bundle.StartStuff();
            Assert.IsTrue(_bundle.IsLoaded);
            Assert.IsNull(_bundle.assetBundle, "not loaded: it would replace the world it sits in");
        }

        [Test]
        public void ABundleGivenItsValuesByAScript_IsNotSetUpAgainWhenItStarts()
        {
            SetEveryUrl(_bundle, KitUrl);
            _bundle.UpdateCallback(new List<PropertyName>()); // what Init -> Deserialise does for the page's bundle
            _bundle.StartStuff();
            Assert.IsTrue(_bundle.valuesApplied);
            Assert.IsFalse(_bundle.IsLoaded, "its own load reports, not StartStuff");
        }

        [Test]
        public void AScriptThatSetsNoUrlForThisPlatform_GetsItReportedLoaded()
        {
            // Only legacyShaderFix, say: it used to load nothing and report nothing, and held the space's load for good.
            _bundle.UpdateCallback(new List<PropertyName> { PropertyName.legacyShaderFix });
            Assert.IsTrue(_bundle.IsLoaded);
        }

        [Test]
        public void ACopyMadeWithInstantiate_NeverOwnsItsOriginalsBundle_AndReportsLoaded()
        {
            // Instantiate copies what Unity serializes. assetBundle is a public field (the SDK's surface), so a copy comes
            // holding its original's bundle; ownsBundle mustn't come with it, or the copy unloads that bundle when it goes.
            var owns = typeof(BSAssetBundle).GetField("ownsBundle", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsFalse(owns.IsPublic || owns.IsDefined(typeof(SerializeField), true), "ownsBundle mustn't be serialized");
            SetEveryUrl(_bundle, KitUrl);
            _bundle.UpdateCallback(new List<PropertyName>());
            Set(_bundle, "ownsBundle", true);
            var copy = Object.Instantiate(_bundle.gameObject);
            _objects.Add(copy);
            var copied = copy.GetComponent<BSAssetBundle>();
            Assert.IsTrue(copied.valuesApplied, "valuesApplied is serialized, so it comes with the copy");
            Assert.IsFalse(Get<bool>(copied, "ownsBundle"));
            copied.StartStuff();
            Assert.IsTrue(copied.IsLoaded, "it used to hold the space's load for good");
            Assert.IsFalse(_bundle.IsLoaded);
        }

        [Test]
        public void ACopyHoldingItsOriginalsBundle_ReleasesNothingWhenItGoes()
        {
            var released = new List<AssetBundle>();
            BSAssetBundle.UnloadOverride = b => { released.Add(b); return Task.CompletedTask; };
            try
            {
                var shared = FakeBundle();
                var settings = Settings();
                Holding(_bundle, shared, owns: true, KitUrl + "?copy", settings);
                var copied = NewBundle("copy");
                // What Instantiate leaves (a fake bundle has no native object, so set by hand): the reference, but neither
                // its ownership nor its URL.
                Holding(copied, shared, owns: false, null, settings);
                Assert.IsTrue(copied.Unload().IsCompleted);
                CollectionAssert.IsEmpty(released, "the original's bundle is the original's to unload");
                Assert.IsTrue(ReferenceEquals(_bundle.assetBundle, shared) && Get<bool>(_bundle, "ownsBundle"));
            }
            finally
            {
                BSAssetBundle.UnloadOverride = null;
            }
        }

        [Test]
        public void AUrlThatArrivesAfterThePlacedBundleReportedNothingToLoad_IsLogged()
        {
            _bundle.StartStuff();
            SetEveryUrl(_bundle, KitUrl);
            LogAssert.Expect(LogType.Warning, new Regex(@"got its URL after it had reported that it had nothing to load"));
            _bundle.UpdateCallback(new List<PropertyName>()); // no URL among the changes, so nothing downloads here
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void UnloadingTwice_ReleasesTheBundleOnce()
        {
            var released = new List<AssetBundle>();
            BSAssetBundle.UnloadOverride = b => { released.Add(b); return Task.CompletedTask; };
            try
            {
                var shared = FakeBundle();
                Holding(_bundle, shared, owns: true, KitUrl + "?twice", Settings());
                var first = _bundle.Unload();
                var second = _bundle.Unload();
                Assert.IsTrue(first.IsCompleted && second.IsCompleted);
                Assert.AreEqual(1, released.Count, "taken by the first, before anything waits: the second finds nothing to do");
                Assert.IsTrue(ReferenceEquals(released[0], shared));
                Assert.IsFalse(_bundle.MayStillRegisterKitPrefabs, "unloaded: a download that lands now registers nothing");
            }
            finally
            {
                BSAssetBundle.UnloadOverride = null;
            }
        }

        [Test]
        public void AnUnload_IsRegisteredFromItsStart_WhileItWaitsForALoadInFlight()
        {
            const string url = KitUrl + "?registered";
            SetEveryUrl(_bundle, url);
            _bundle.BeginLoading(); // a download in flight: a rejoin, or F5, must wait for the load and the unload
            var unloading = _bundle.Unload();
            Assert.IsFalse(unloading.IsCompleted);
            Assert.IsTrue(BSAssetBundle.IsUnloading(url), "so the next load of this URL waits for all of it");
            _bundle.EndLoading();
        }

        [Test]
        public void TheSameKitTwice_SharesOneBundle_NotWithOneThatIsUnloading()
        {
            const string url = KitUrl + "?share";
            var loading = NewBundle("loading");
            loading.BeginLoading();
            Set(loading, "loadingUrl", url);
            Assert.AreSame(loading, BSAssetBundle.LoaderOf(_bundle, url, new[] { _bundle, loading }), "the one loading it");
            Assert.IsNull(BSAssetBundle.LoaderOf(loading, url, new[] { loading }), "never itself");
            Assert.IsNull(BSAssetBundle.HolderOf(_bundle, url, new[] { _bundle, loading }), "loading isn't holding");

            var waiting = NewBundle("waiting");
            waiting.BeginLoading();
            Set(waiting, "loadingUrl", url);
            Set(waiting, "waitingForTwin", true);
            Assert.AreSame(loading, BSAssetBundle.LoaderOf(_bundle, url, new[] { _bundle, waiting, loading }),
                "the one downloading it");
            Assert.IsNull(BSAssetBundle.LoaderOf(_bundle, url, new[] { _bundle, waiting }),
                "never one only waiting for a twin: two of those could each pick the other, and wait for good");

            var holding = NewBundle("holding");
            Holding(holding, FakeBundle(), owns: true, url, Settings());
            Assert.AreSame(holding, BSAssetBundle.HolderOf(_bundle, url, new[] { _bundle, holding }), "the one holding it");
            Set(holding, "loadingUrl", url + "?another");
            Assert.AreSame(holding, BSAssetBundle.HolderOf(_bundle, url, new[] { _bundle, holding }),
                "even while it loads another URL: what it holds is still that bundle, now");
            _ = holding.Unload();
            Assert.IsNull(BSAssetBundle.HolderOf(_bundle, url, new[] { _bundle, holding }),
                "not one being unloaded: an old instance, when the world is rejoined while it still loads");

            var world = NewBundle("world");
            world.isScene = true;
            world.BeginLoading();
            Set(world, "loadingUrl", url);
            Assert.IsNull(BSAssetBundle.LoaderOf(_bundle, url, new[] { _bundle, world }), "scene bundles are never shared");
            loading.EndLoading();
            waiting.EndLoading();
            world.EndLoading();
        }

        [Test]
        public void TheOwnerLeaving_HandsTheBundleToATwin_AndOnlyTheLastStopsTheCameras()
        {
            const string url = KitUrl + "?handoff";
            var shared = FakeBundle();
            var settings = Settings();
            var owner = _bundle;
            var twin = NewBundle("twin");
            var heir = NewBundle("heir");
            Holding(owner, shared, owns: true, url, settings);
            Holding(twin, shared, owns: false, url, settings);
            Holding(heir, shared, owns: false, url, settings);
            settings.KitBundles.AddRange(new[] { owner, twin, heir });
            settings.KitPaths["assets/kit/lamp.prefab"] = owner;

            // A kit item from that bundle, with a camera rendering into a texture.
            var item = new GameObject("kit item");
            _objects.Add(item);
            item.AddComponent<BSKitItem>().KitBundle = shared;
            var camera = new GameObject("camera").AddComponent<Camera>();
            camera.transform.SetParent(item.transform);
            var target = new RenderTexture(16, 16, 0);
            camera.targetTexture = target;
            try
            {
                _ = twin.Unload();
                Assert.IsTrue(camera.enabled && camera.targetTexture == target, "a twin leaving stops nothing of its owner's");

                _ = owner.Unload();
                Assert.IsTrue(Get<bool>(heir, "ownsBundle"), "a twin still using the bundle takes it over");
                Assert.AreEqual(url, Get<string>(heir, "currentUrl"));
                Assert.IsTrue(ReferenceEquals(heir.assetBundle, shared), "not unloaded under it");
                Assert.AreSame(heir, settings.KitPaths["assets/kit/lamp.prefab"], "kit items now find the heir");
                Assert.IsTrue(camera.enabled && camera.targetTexture == target, "the kit is still in use");

                _ = heir.Unload();
                Assert.IsFalse(camera.enabled, "the last one leaving stops the kit's cameras before the bundle goes");
                Assert.IsNull(camera.targetTexture);
                CollectionAssert.IsEmpty(settings.KitPaths);
            }
            finally
            {
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void RepointingKitPaths_MovesOrDropsOnlyTheLeaversEntries()
        {
            var leaver = _bundle;
            var other = NewBundle("other");
            var heir = NewBundle("heir");
            var paths = new Dictionary<string, BSAssetBundle> { ["a"] = leaver, ["b"] = other, ["c"] = leaver };
            BSAssetBundle.RepointKitPaths(paths, leaver, heir);
            Assert.AreSame(heir, paths["a"]);
            Assert.AreSame(other, paths["b"]);
            Assert.AreSame(heir, paths["c"]);
            BSAssetBundle.RepointKitPaths(paths, heir, null);
            CollectionAssert.AreEquivalent(new[] { "b" }, paths.Keys);
        }

        [Test]
        public void AnUnloadingWorld_LoadsVoid_OnlyOverItsOwnScene()
        {
            Assert.AreEqual(BSAssetBundle.LeaveWorld.Nothing, BSAssetBundle.HowToLeave(false, 3, 3),
                "its scene never loaded, or a newer world has replaced it");
            Assert.AreEqual(BSAssetBundle.LeaveWorld.LoadVoid, BSAssetBundle.HowToLeave(true, 3, 3));
            Assert.AreEqual(BSAssetBundle.LeaveWorld.WaitForNewerWorld, BSAssetBundle.HowToLeave(true, 3, 4),
                "a newer world's Single load is coming: Void would queue after it and replace it");
        }

        [Test]
        public void Void_IsFoundByBuildIndex_NotByName()
        {
            var index = BSAssetBundle.VoidBuildIndex();
            if (index < 0)
            {
                Assert.AreEqual(-1, index, "no Void scene in this project's build (the Creator SDK's own projects)");
                return;
            }
            StringAssert.EndsWith("/Void.unity", SceneUtility.GetScenePathByBuildIndex(index));
        }
    }
}
