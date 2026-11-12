using NUnit.Framework;
using UnityEngine;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// A kit item and a kit bundle placed in the same world: the item's Start can come first, when its path isn't
    /// registered yet and scene.bundlesLoaded (recomputed only by the load poll) still says yes, so it failed with
    /// "Kititem not found" and cancelled the space. Now a kit item that misses its path waits while a kit bundle may
    /// still register it, then looks once more (KitPathWait, BSAssetBundle.MayStillRegisterKitPrefabs). The wait
    /// itself is a coroutine (Play only); its decisions are checked here.
    /// </summary>
    public class KitPathWaitTests
    {
        static KitPathWait Wait(System.Func<bool> registered, System.Func<bool> kitLoading, System.Func<bool> queued = null,
            System.Func<bool> failed = null, float now = 0f) =>
            new KitPathWait(registered, kitLoading, queued ?? (() => false), failed ?? (() => false), now);

        [Test]
        public void TheWaitEnds_AsSoonAsThePathIsRegistered()
        {
            var registered = false;
            var wait = Wait(() => registered, () => true);
            Assert.IsFalse(wait.Done(0f));
            Assert.IsFalse(wait.Done(5f), "a kit bundle is still loading");
            registered = true;
            Assert.IsTrue(wait.Done(5.01f), "without waiting for the other bundles");
        }

        [Test]
        public void TheWaitEnds_WhenNoKitBundleMayStillRegisterIt_CheckedFourTimesASecond()
        {
            var loading = true;
            var scans = 0;
            var wait = Wait(() => false, () => { scans++; return loading; });
            for (var t = 0f; t < 1f; t += 0.01f) Assert.IsFalse(wait.Done(t));
            Assert.LessOrEqual(scans, 4, "the scan walks every BSAssetBundle: not every frame");
            loading = false;
            Assert.IsTrue(wait.Done(1.25f), "then the item looks once more, and fails if the path still isn't there");

            Assert.IsTrue(Wait(() => false, () => false).Done(0f), "no bundle loading: no wait at all");
        }

        [Test]
        public void AKitBundleStillLoading_IsWaitedForPastTheLimit_UnlessTheLoadFails()
        {
            var failed = false;
            var wait = Wait(() => false, () => true, failed: () => failed, now: 100f);
            Assert.IsFalse(wait.Done(100f + KitPathWait.LimitSeconds + 600f),
                "the space's load waits for that bundle too, and it bounds itself (a stalled download fails)");
            failed = true;
            Assert.IsTrue(wait.Done(100f + KitPathWait.LimitSeconds + 600.01f), "a failed load ends it");
        }

        [Test]
        public void QueuedCreationsAlone_AreWaitedForUpToTheLimit()
        {
            // A page can keep creating objects: those are waited for up to the backstop, and no longer.
            var wait = Wait(() => false, () => false, queued: () => true, now: 100f);
            Assert.IsFalse(wait.Done(100f));
            Assert.IsFalse(wait.Done(100f + KitPathWait.LimitSeconds - 1f));
            Assert.IsTrue(wait.Done(100f + KitPathWait.LimitSeconds));
        }

        [Test]
        public void WhichBundlesMayStillRegisterKitPrefabs()
        {
            var root = new GameObject("KitPathWaitTests");
            try
            {
                // EditMode: no Awake or Start, so placed bundles sit as they do before their Start.
                var kit = new GameObject("kit").AddComponent<BSAssetBundle>();
                kit.transform.SetParent(root.transform);
                Assert.IsTrue(kit.MayStillRegisterKitPrefabs, "placed and active: its Start will load it");
                Assert.IsTrue(BSAssetBundle.AnyKitStillLoading());
                kit.StartStuff(); // no URL here, so it reports loaded with nothing to load
                Assert.IsFalse(kit.MayStillRegisterKitPrefabs, "it has reported");

                var world = new GameObject("world").AddComponent<BSAssetBundle>();
                world.transform.SetParent(root.transform);
                world.isScene = true;
                Assert.IsFalse(world.MayStillRegisterKitPrefabs, "a scene bundle registers no kit prefabs");

                var hidden = new GameObject("hidden").AddComponent<BSAssetBundle>();
                hidden.transform.SetParent(root.transform);
                hidden.gameObject.SetActive(false);
                Assert.IsFalse(hidden.MayStillRegisterKitPrefabs, "inactive and not loading: it won't start");

                var leaving = new GameObject("leaving").AddComponent<BSAssetBundle>();
                leaving.transform.SetParent(root.transform);
                _ = leaving.Unload();
                Assert.IsFalse(leaving.MayStillRegisterKitPrefabs, "unloaded: it registers nothing");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
