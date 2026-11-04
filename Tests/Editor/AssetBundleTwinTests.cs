using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// Kit bundles that share a URL, end to end through LoadBundle, with the download and the unload replaced by
    /// test seams (BSAssetBundle.DownloadOverride / UnloadOverride): Unity refuses a second copy of a bundle's files,
    /// and that failure cancels the space, as bundles are critical. So exactly one of them may download a URL, every
    /// other shares its bundle, a destroyed or retargeted twin mustn't make one download again, and a bundle held when
    /// its holder goes is released, once. EditMode [UnityTest]s: the loads' awaits resume on the editor's frames.
    /// "Bundles" are uninitialised AssetBundle objects: a reference to share, with no native bundle behind it.
    /// Each test has a URL of its own: unloads are registered by URL in a static table, and a test that fails part way
    /// can leave one there, which held every later test's load (all of them failed, with nothing downloaded).
    /// </summary>
    public class AssetBundleTwinTests
    {
        string Url;

        sealed class Download
        {
            internal string url;
            internal TaskCompletionSource<AssetBundle> done;
            internal CancellationToken token;
        }

        readonly List<Download> _downloads = new List<Download>();
        readonly List<AssetBundle> _released = new List<AssetBundle>();
        readonly List<GameObject> _objects = new List<GameObject>();
        readonly List<(string url, Task task)> _registered = new List<(string, Task)>();
        BSSceneSettings _settings;
        float _cap;

        static readonly PropertyName[] EveryUrl =
        {
            PropertyName.windowsUrl, PropertyName.osxUrl, PropertyName.linuxUrl,
            PropertyName.androidUrl, PropertyName.iosUrl, PropertyName.vosUrl,
        };

        [SetUp]
        public void SetUp()
        {
            Url = "https://example.invalid/" + TestContext.CurrentContext.Test.Name + "/kit.banter";
            var ours = Url;
            _cap = BSAssetBundle.PendingUnloadCapSeconds;
            BSAssetBundle.DownloadOverride = (url, token) =>
            {
                // A load left over from an earlier test (its kit destroyed) isn't this test's download.
                if (url == null || !url.StartsWith(ours, StringComparison.Ordinal))
                {
                    return Task.FromCanceled<AssetBundle>(new CancellationToken(true));
                }
                var download = new Download
                {
                    url = url, token = token,
                    // As Get's loops do, a cancel lands later, never inside Cancel() itself.
                    done = new TaskCompletionSource<AssetBundle>(TaskCreationOptions.RunContinuationsAsynchronously),
                };
                token.Register(() => download.done.TrySetCanceled());
                _downloads.Add(download);
                return download.done.Task;
            };
            BSAssetBundle.UnloadOverride = bundle =>
            {
                _released.Add(bundle);
                return Task.CompletedTask;
            };
            _settings = (BSSceneSettings)FormatterServices.GetUninitializedObject(typeof(BSSceneSettings));
            _settings.KitBundles = new List<BSAssetBundle>();
            _settings.KitPaths = new Dictionary<string, BSAssetBundle>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var (url, task) in _registered) BSAssetBundle.UnregisterUnloading(url, task);
            foreach (var go in _objects) if (go != null) Object.DestroyImmediate(go);
            foreach (var download in _downloads) download.done.TrySetCanceled(); // what still waits resumes destroyed
            BSAssetBundle.DownloadOverride = null;
            BSAssetBundle.UnloadOverride = null;
            BSAssetBundle.PendingUnloadCapSeconds = _cap;
            _objects.Clear();
            _downloads.Clear();
            _released.Clear();
            _registered.Clear();
        }

        static AssetBundle Fake() => (AssetBundle)FormatterServices.GetUninitializedObject(typeof(AssetBundle));

        static void Set(BSAssetBundle bundle, string field, object value) =>
            typeof(BSAssetBundle).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(bundle, value);

        static T Get<T>(BSAssetBundle bundle, string field) =>
            (T)typeof(BSAssetBundle).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(bundle);

        BSAssetBundle Kit(string name, string url)
        {
            var go = new GameObject("AssetBundleTwinTests " + name);
            _objects.Add(go);
            var kit = go.AddComponent<BSAssetBundle>();
            SetUrl(kit, url);
            var scene = (BSScene)FormatterServices.GetUninitializedObject(typeof(BSScene)); // its constructor starts the page side
            scene.settings = _settings;
            Set(kit, "_scene", scene);
            return kit;
        }

        static void SetUrl(BSAssetBundle kit, string url)
        {
            kit.windowsUrl = kit.osxUrl = kit.linuxUrl = kit.androidUrl = kit.iosUrl = kit.vosUrl = url;
        }

        /// <summary>What the page's property update does: this platform's URL among the changes starts the load.</summary>
        static void Load(BSAssetBundle kit) => kit.UpdateCallback(EveryUrl.ToList());

        /// <summary>A kit already holding <paramref name="bundle"/> from <paramref name="url"/>.</summary>
        void Holding(BSAssetBundle kit, AssetBundle bundle, string url, bool owns = true)
        {
            kit.assetBundle = bundle;
            Set(kit, "ownsBundle", owns);
            Set(kit, "currentUrl", url);
            kit.valuesApplied = true;
        }

        void BlockUnload(string url, Task until)
        {
            BSAssetBundle.RegisterUnloading(url, until);
            _registered.Add((url, until));
        }

        /// <summary>Lets the editor run frames (the awaits resume there) until <paramref name="done"/>, or 3 s.</summary>
        static IEnumerator Until(Func<bool> done)
        {
            var deadline = Time.realtimeSinceStartup + 3f;
            while (!done() && Time.realtimeSinceStartup < deadline) yield return null;
        }

        static IEnumerator Frames(int count = 5)
        {
            for (int i = 0; i < count; i++) yield return null;
        }

        [UnityTest]
        public IEnumerator ThreeTwins_AWaiterDestroyed_TheOthersStillShareOneDownload()
        {
            var x = Fake();
            var k1 = Kit("1", Url);
            var k2 = Kit("2", Url);
            var k3 = Kit("3", Url);
            Load(k1);
            Assert.AreEqual(1, _downloads.Count, "the first downloads");
            Load(k2);
            Load(k3);
            Assert.AreEqual(1, _downloads.Count, "the others wait for it");

            _ = k2.Unload(); // the page destroys the second while it waits
            Object.DestroyImmediate(k2.gameObject);

            _downloads[0].done.SetResult(x);
            yield return Until(() => ReferenceEquals(k3.assetBundle, x));

            Assert.AreEqual(1, _downloads.Count, "a second copy of the same files would fail, and cancel the space");
            Assert.IsTrue(ReferenceEquals(k1.assetBundle, x) && Get<bool>(k1, "ownsBundle"));
            Assert.IsTrue(ReferenceEquals(k3.assetBundle, x), "the third shares the first's");
            Assert.IsFalse(Get<bool>(k3, "ownsBundle"));
            CollectionAssert.IsEmpty(_released);
        }

        [UnityTest]
        public IEnumerator TwoWaitingForTheSameUnload_OneDownloads_TheOtherShares()
        {
            var x = Fake();
            var earlier = new TaskCompletionSource<bool>(); // the world just left, still unloading this URL
            BlockUnload(Url, earlier.Task);
            var k1 = Kit("1", Url);
            var k2 = Kit("2", Url);
            Load(k1);
            Load(k2);
            Assert.AreEqual(0, _downloads.Count, "both wait for the unload first");

            earlier.SetResult(true);
            yield return Until(() => _downloads.Count > 0);
            yield return Frames();
            Assert.AreEqual(1, _downloads.Count, "neither waits for the other in a circle, and only one downloads");

            _downloads[0].done.SetResult(x);
            yield return Until(() => ReferenceEquals(k1.assetBundle, x) && ReferenceEquals(k2.assetBundle, x));
            Assert.IsTrue(ReferenceEquals(k1.assetBundle, x) && ReferenceEquals(k2.assetBundle, x));
            Assert.AreEqual(1, new[] { k1, k2 }.Count(k => Get<bool>(k, "ownsBundle")), "exactly one owns it");
        }

        [UnityTest]
        public IEnumerator ATwin_SharesWhatItsHolderHolds_EvenWhileTheHolderLoadsAnotherUrl()
        {
            var x = Fake();
            var x2 = Fake();
            var a = Kit("holder", Url);
            Holding(a, x, Url);
            SetUrl(a, Url + "?new"); // the page points it at a slower, newer kit
            Load(a);
            Assert.AreEqual(1, _downloads.Count);

            var b = Kit("twin", Url);
            Load(b);
            Assert.IsTrue(ReferenceEquals(b.assetBundle, x), "shared at once, not after the holder's other load");

            _downloads[0].done.SetResult(x2);
            yield return Until(() => ReferenceEquals(a.assetBundle, x2));
            Assert.IsTrue(ReferenceEquals(a.assetBundle, x2));
            Assert.AreEqual(Url + "?new", Get<string>(a, "currentUrl"));
            Assert.IsTrue(ReferenceEquals(b.assetBundle, x) && Get<bool>(b, "ownsBundle"),
                "the twin takes the old bundle over; it used to adopt the new one as the old URL");
            Assert.AreEqual(Url, Get<string>(b, "currentUrl"));
            CollectionAssert.IsEmpty(_released, "not unloaded under the twin");
        }

        [UnityTest]
        public IEnumerator DestroyedWhileWaitingForAnUnload_StillReleasesWhatItHolds()
        {
            var x = Fake();
            var earlier = new TaskCompletionSource<bool>();
            BlockUnload(Url + "?new", earlier.Task);
            var a = Kit("holder", Url);
            Holding(a, x, Url);
            SetUrl(a, Url + "?new");
            Load(a); // waits for the earlier unload of the new URL
            _ = a.Unload(); // then the page destroys it
            Object.DestroyImmediate(a.gameObject);

            earlier.SetResult(true);
            yield return Until(() => _released.Count > 0);
            Assert.AreEqual(1, _released.Count, "the bundle it held is released; it used to be dropped, and its URL dead");
            Assert.IsTrue(ReferenceEquals(_released[0], x));
            Assert.AreEqual(0, _downloads.Count, "and nothing downloaded for a destroyed bundle");
        }

        [UnityTest]
        public IEnumerator ADownloadThatFails_WithTwoWaiting_OneOfThemDownloads_AndBothShareIt()
        {
            var x = Fake();
            var k1 = Kit("1", Url);
            var k2 = Kit("2", Url);
            var k3 = Kit("3", Url);
            Load(k1);
            Load(k2);
            Load(k3);
            Assert.AreEqual(1, _downloads.Count);

            _downloads[0].done.SetException(new Exception("404"));
            // The two waiters used to pick each other, and wait for good.
            yield return Until(() => _downloads.Count > 1);
            yield return Frames();
            Assert.AreEqual(2, _downloads.Count, "the first waiter to look again downloads; the other waits on it");

            _downloads[1].done.SetResult(x);
            yield return Until(() => ReferenceEquals(k2.assetBundle, x) && ReferenceEquals(k3.assetBundle, x));
            Assert.IsTrue(ReferenceEquals(k2.assetBundle, x) && ReferenceEquals(k3.assetBundle, x), "both get it, within the frames allowed");
            Assert.AreEqual(1, new[] { k2, k3 }.Count(k => Get<bool>(k, "ownsBundle")));
            Assert.AreEqual(2, _downloads.Count);
        }

        [UnityTest]
        public IEnumerator ATwinUnloadedWhileItWaits_StopsWaitingAtOnce()
        {
            var k1 = Kit("1", Url);
            var k2 = Kit("2", Url);
            Load(k1);
            Load(k2);
            var unloading = k2.Unload();
            yield return Until(() => unloading.IsCompleted);
            Assert.IsTrue(unloading.IsCompleted, "it doesn't wait for the first's download to end");
            Assert.IsFalse(_downloads[0].done.Task.IsCompleted, "which is still running");
            Assert.IsFalse(BSAssetBundle.IsUnloading(Url), "so a new load of the URL isn't held by it");
        }

        [UnityTest]
        public IEnumerator ACancelledLoad_IsNotAFailure_EvenIfTheBundleIsSetUpAgainAtOnce()
        {
            var k = Kit("again", Url);
            var reports = new List<bool>();
            k.loaded.AddListener((success, message) => reports.Add(success));
            Load(k);
            _ = k.Unload(); // cancels the download...
            Load(k);        // ...and a property set at once clears unloadRequested before the cancel lands
            yield return Until(() => _downloads.Count > 1);
            Assert.AreEqual(2, _downloads.Count, "the second setup loads once the first has ended");
            CollectionAssert.DoesNotContain(reports, false, "a failure would cancel the whole space");
            Assert.IsTrue(k.IsLoaded);
        }

        [UnityTest]
        public IEnumerator ABundleChangingUrl_HoldsTheNewOneBeforeItReleasesTheOld()
        {
            var x = Fake();
            var y = Fake();
            var xReleased = new TaskCompletionSource<bool>();
            BSAssetBundle.UnloadOverride = bundle =>
            {
                _released.Add(bundle);
                return ReferenceEquals(bundle, x) ? xReleased.Task : Task.CompletedTask; // the old one takes a while
            };
            try
            {
                var a = Kit("changing", Url);
                Holding(a, x, Url);
                var h = Kit("holder", Url + "?other");
                Holding(h, y, Url + "?other");
                SetUrl(a, Url + "?other");
                Load(a); // shares h's bundle, and releases its own old one
                Assert.IsTrue(ReferenceEquals(a.assetBundle, y), "the new one is held while the old one is still being released");
                Assert.IsTrue(BSAssetBundle.IsUnloading(Url), "the old URL is held while its bundle goes");

                _ = h.Unload(); // h leaves meanwhile
                Assert.IsTrue(Get<bool>(a, "ownsBundle"), "a was found holding it, and took it over");
                // By reference: fake bundles have no native object, so Unity's == calls any two of them equal (and null).
                Assert.IsFalse(_released.Any(b => ReferenceEquals(b, y)), "not unloaded from under a");
                Assert.AreEqual(1, _released.Count);
                Assert.IsTrue(ReferenceEquals(_released[0], x));
            }
            finally
            {
                xReleased.TrySetResult(true); // even when an assertion fails: the old URL's unload must end
            }
            yield return Until(() => !BSAssetBundle.IsUnloading(Url));
            Assert.IsFalse(BSAssetBundle.IsUnloading(Url), "the old URL is free once its bundle has gone");
        }

        [UnityTest]
        public IEnumerator AnUnloadThenASetupAtOnce_WhileWaitingOnATwin_LoadsAgain_WithoutSpinning()
        {
            // A world graph's Unload then Refresh: the setup clears unloadRequested before the cancelled wait resumes.
            // That wait used to loop for good without yielding (its waits completed at once from then on), freezing
            // the main thread. Every pass after a wait now starts a frame later, so a regression fails this test by
            // timing out instead of hanging the run.
            var x = Fake();
            var k1 = Kit("1", Url);
            var k2 = Kit("2", Url);
            Load(k1);
            Load(k2);
            _ = k2.Unload();
            Load(k2);
            yield return Frames(10);
            Assert.AreEqual(1, _downloads.Count, "the second setup waits on the first's download too");

            _downloads[0].done.SetResult(x);
            yield return Until(() => ReferenceEquals(k2.assetBundle, x));
            Assert.IsTrue(ReferenceEquals(k2.assetBundle, x), "set up again, it shares the download within the frames allowed");
            Assert.AreEqual(1, _downloads.Count);
        }

        [UnityTest]
        public IEnumerator TheDownloaderDestroyedMidDownload_OneOfItsWaitersDownloadsInstead()
        {
            var x = Fake();
            var k1 = Kit("1", Url);
            var k2 = Kit("2", Url);
            var k3 = Kit("3", Url);
            Load(k1);
            Load(k2);
            Load(k3);
            _ = k1.Unload(); // the page destroys the one the others wait on
            Object.DestroyImmediate(k1.gameObject);
            Assert.IsTrue(_downloads[0].token.IsCancellationRequested);

            yield return Until(() => _downloads.Count > 1);
            yield return Frames();
            Assert.AreEqual(2, _downloads.Count, "one waiter takes over; the other waits on it");
            _downloads[1].done.SetResult(x);
            yield return Until(() => ReferenceEquals(k2.assetBundle, x) && ReferenceEquals(k3.assetBundle, x));
            Assert.IsTrue(ReferenceEquals(k2.assetBundle, x) && ReferenceEquals(k3.assetBundle, x));
            Assert.AreEqual(1, new[] { k2, k3 }.Count(k => Get<bool>(k, "ownsBundle")));
        }

        [UnityTest]
        public IEnumerator ALoadedListenerThatThrows_StillEndsTheLoad_SoTwinsDontWaitForever()
        {
            LogAssert.ignoreFailingMessages = true; // the listener's exception is logged on its way out
            var x = Fake();
            var k1 = Kit("1", Url);
            var k2 = Kit("2", Url);
            k1.loaded.AddListener((success, message) => throw new InvalidOperationException("a listener that throws"));
            Load(k1);
            Load(k2);
            _downloads[0].done.SetException(new Exception("404"));
            yield return Until(() => _downloads.Count > 1);
            Assert.AreEqual(2, _downloads.Count, "the failed load ended, and its waiter downloads instead");
            _downloads[1].done.SetResult(x);
            yield return Until(() => ReferenceEquals(k2.assetBundle, x));
            Assert.IsTrue(ReferenceEquals(k2.assetBundle, x));
        }

        [UnityTest]
        public IEnumerator UnloadingWhileItDownloads_CancelsTheDownload_AndDoesntFailTheSpace()
        {
            var k = Kit("leaving", Url);
            Load(k);
            Assert.AreEqual(1, _downloads.Count);
            var unloading = k.Unload();
            Assert.IsTrue(_downloads[0].token.IsCancellationRequested, "a world can take minutes on Quest");
            yield return Until(() => unloading.IsCompleted);
            Assert.IsTrue(unloading.IsCompleted);
            Assert.IsTrue(k.IsLoaded, "reported, as a success: an unloaded bundle's load mustn't cancel the space");
            CollectionAssert.IsEmpty(_released);
            // No NoUnexpectedReceived here: the unload's own progress lines are ordinary Logs. An error or exception
            // logged on the way (a load reported as failed) still fails the test, as Unity's runner does by default.
        }

        [UnityTest]
        public IEnumerator AnUnloadThatNeverEnds_HoldsTheNextLoadOnlyUpToTheCap()
        {
            Assert.GreaterOrEqual(_cap, 60f, "above what an unload waits for itself (a newer world's scene: 60 s)");
            BSAssetBundle.PendingUnloadCapSeconds = 0.2f;
            BlockUnload(Url, new TaskCompletionSource<bool>().Task); // cut short by Play stopping: it never ends
            var k = Kit("next", Url);
            Load(k);
            Assert.AreEqual(0, _downloads.Count);
            yield return Until(() => _downloads.Count > 0);
            Assert.AreEqual(1, _downloads.Count, "it downloads once the cap has passed");
        }
    }
}
