using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BS.Utilities.Async;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// Issue 1 of the 2026-11-03 SDK report. A space's load waited, with no limit, for every registered component to
    /// report loaded, and some never did (BSMonoBehaviour, BSScriptGraph, placed asset bundles), so players sat in the
    /// cage until they gave up; the 25 s log named nothing. Asset bundles (the world and its kits) are still waited
    /// for, and still fail the load when they fail. Any other component that neither loads nor reports progress for
    /// 20 s stops holding it, and the log names each one with where it is and why it is stuck.
    /// </summary>
    public class SceneLoadGateTests
    {
        const float Stalled = BSLoadGate.StalledSeconds;
        const float Step = 0.5f; // the poll checks twice a second; a longer gap restarts the clocks (a pause)

        static BSLoadGate.Pending Waiting(int cid, ComponentType type, float progress = 0f) =>
            new BSLoadGate.Pending(cid, type, progress);

        static List<BSLoadGate.Pending> Pending(params BSLoadGate.Pending[] waiting) => waiting.ToList();

        /// <summary>Ticks every half second from <paramref name="from"/> to <paramref name="to"/>; what was released, and when.</summary>
        static List<(int cid, float at)> Run(BSLoadGate gate, List<BSLoadGate.Pending> pending, float from, float to)
        {
            var released = new List<(int, float)>();
            for (var t = from; t <= to; t += Step)
            {
                foreach (var cid in gate.Tick(pending, t)) released.Add((cid, t));
            }
            return released;
        }

        [Test]
        public void AComponentThatNeitherLoadsNorReportsProgress_IsReleasedAfter20Seconds_Once()
        {
            var gate = new BSLoadGate();
            var stuck = Pending(Waiting(1, ComponentType.MonoBehaviour), Waiting(2, ComponentType.KitItem));

            CollectionAssert.IsEmpty(Run(gate, stuck, 100f, 100f + Stalled - Step));
            CollectionAssert.AreEquivalent(new[] { 1, 2 }, gate.Tick(stuck, 100f + Stalled));
            Assert.IsTrue(gate.WasReleased(1) && gate.WasReleased(2));
            CollectionAssert.IsEmpty(Run(gate, stuck, 100f + Stalled + Step, 100f + 3 * Stalled), "released once per load, even if still reported waiting");

            gate.Reset();
            Assert.IsFalse(gate.WasReleased(1), "a new load starts with nothing released");
        }

        [Test]
        public void ProgressRestartsTheClock()
        {
            // As a GLTF reports it: its download (0-0.8), then the import's start.
            var gate = new BSLoadGate();
            CollectionAssert.IsEmpty(Run(gate, Pending(Waiting(1, ComponentType.GLTF, .1f)), 0f, 15f - Step));
            CollectionAssert.IsEmpty(Run(gate, Pending(Waiting(1, ComponentType.GLTF, .85f)), 15f, 15f + Stalled - Step),
                "an import that is still within 20 s of starting is waited for");
            Assert.AreEqual(new[] { (1, 15f + Stalled) }, Run(gate, Pending(Waiting(1, ComponentType.GLTF, .85f)), 15f + Stalled, 15f + Stalled));
        }

        [Test]
        public void AssetBundlesAreAlwaysWaitedFor_AndNothingIsReleasedWhileOneIsPending()
        {
            var gate = new BSLoadGate();
            var world = Waiting(1, ComponentType.AssetBundle, .3f);
            var script = Waiting(2, ComponentType.MonoBehaviour);
            CollectionAssert.IsEmpty(Run(gate, Pending(world, script), 0f, 600f),
                "a world.asset can take minutes on Quest, and kit items wait for their bundles");
            Assert.IsFalse(gate.WasReleased(1));

            // The bundle loaded after the 600 s check: the other clocks start from there, not from SCENE_READY.
            CollectionAssert.IsEmpty(Run(gate, Pending(script), 600f + Step, 600f + Stalled - Step));
            CollectionAssert.AreEqual(new[] { 2 }, gate.Tick(Pending(script), 600f + Stalled));
        }

        [Test]
        public void APause_StopsTheClocks_InsteadOfReleasingEverythingAtOnce()
        {
            var gate = new BSLoadGate();
            var script = Pending(Waiting(1, ComponentType.MonoBehaviour));
            CollectionAssert.IsEmpty(Run(gate, script, 0f, 10f));
            // The headset was taken off for five minutes; real time ran on.
            CollectionAssert.IsEmpty(gate.Tick(script, 310f), "the first check after a pause");
            CollectionAssert.IsEmpty(Run(gate, script, 310f + Step, 320f - Step));
            CollectionAssert.AreEqual(new[] { 1 }, gate.Tick(script, 320f), "the 10 s before the pause count; the pause doesn't");
        }

        [Test]
        public void AHeldClock_KeepsWhatItHadCounted()
        {
            var gate = new BSLoadGate();
            var script = Waiting(1, ComponentType.MonoBehaviour);
            var kit = Waiting(2, ComponentType.AssetBundle);
            CollectionAssert.IsEmpty(Run(gate, Pending(script), 0f, 15f), "15 s without loading");
            CollectionAssert.IsEmpty(Run(gate, Pending(script, kit), 15f + Step, 200f), "a kit bundle loads: the clock stands still");
            CollectionAssert.IsEmpty(Run(gate, Pending(script), 200f + Step, 205f - Step));
            CollectionAssert.AreEqual(new[] { 1 }, gate.Tick(Pending(script), 205f), "the last 5 s, after the bundle");
        }

        [Test]
        public void OnlyAFailureTheLoadIsStillWaitingFor_FailsTheLoad()
        {
            var gate = new BSLoadGate();
            var kitBundle = Waiting(1, ComponentType.AssetBundle);
            var script = Waiting(2, ComponentType.MonoBehaviour);
            Run(gate, Pending(kitBundle, script), 0f, 1000f);
            Assert.IsTrue(BSLoadGate.FailureCancelsLoad(spaceLoaded: false, releasedByGate: gate.WasReleased(1), ofCurrentLoad: true),
                "a world or kit bundle is never released, so its failure still fails the load");

            Assert.AreEqual(new[] { (2, 1000f + Stalled) }, Run(gate, Pending(script), 1000f + Step, 1000f + Stalled));
            Assert.IsFalse(BSLoadGate.FailureCancelsLoad(false, gate.WasReleased(2), true),
                "a released component that fails later must not fail a load that stopped waiting for it");
            Assert.IsFalse(BSLoadGate.FailureCancelsLoad(spaceLoaded: true, releasedByGate: false, ofCurrentLoad: true),
                "nor may any failure once the space is open: LOAD_FAILED would make the page's requests fail");
            Assert.IsFalse(BSLoadGate.FailureCancelsLoad(false, false, ofCurrentLoad: false), "nor one left over from the last load");
        }

        [Test]
        public void ABSMonoBehaviour_ReportsLoadedWhenItStarts()
        {
            var go = new GameObject("SceneLoadGateTests script");
            try
            {
                var script = go.AddComponent<BSMonoBehaviour>(); // EditMode: no Awake, so no scene is touched
                Assert.IsFalse(script.IsLoaded);
                script.StartStuff();
                Assert.IsTrue(script.IsLoaded, "it has nothing to load, and used to hold its space's load for good");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ABSScriptGraph_ReportsLoadedOnceItsMachinesAreSummarised()
        {
            var go = new GameObject("SceneLoadGateTests graph");
            try
            {
                var graph = go.AddComponent<BSScriptGraph>();
                // Its summary goes to the scene; a bare one that buffers it, so nothing else runs.
                var scene = (BSScene)FormatterServices.GetUninitializedObject(typeof(BSScene));
                typeof(BSScene).GetField("_tickBuffer", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(scene, new List<BSComponentPropertyUpdate>());
                typeof(BSScriptGraph).GetField("_scene", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(graph, scene);
                graph.machineCount = -1;
                int? countWhenLoaded = null;
                graph.loaded.AddListener((ok, message) => countWhenLoaded = graph.MachineCount);

                graph.StartStuff();

                Assert.IsTrue(graph.IsLoaded, "it never reported, and held its space's load for good");
                Assert.AreEqual(0, countWhenLoaded, "the page hears 'loaded' with the machine count already refreshed");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void TheLogSaysWhereAWaitingComponentIsAndWhyItIsStuck()
        {
            var root = new GameObject("World");
            try
            {
                var clock = new GameObject("Clock");
                clock.transform.SetParent(root.transform);
                var script = clock.AddComponent<BSMonoBehaviour>();

                string Describe(BSComponentBase component, GameObject go, float progress = 0f) =>
                    BSLoadGate.Describe(BSLoadGate.Diagnose(7, 3, ComponentType.MonoBehaviour, component, go, progress));

                Assert.AreEqual("MonoBehaviour 7 at 'World/Clock' (oid 3): it has not started " +
                                "[activeInHierarchy=True enabled=True started=False _loaded=False progress=0%]", Describe(script, clock));
                script.enabled = false;
                StringAssert.Contains("): it is disabled, so it never started [activeInHierarchy=True enabled=False", Describe(script, clock));
                script.enabled = true;
                root.SetActive(false);
                StringAssert.Contains("): its object is inactive, so it never started [activeInHierarchy=False enabled=True", Describe(script, clock));
                root.SetActive(true);
                script.StartStuff();
                StringAssert.Contains("): it reported loaded, but the load never heard [", Describe(script, clock));

                // The component, then its whole object, gone while the load still counts it.
                Assert.AreEqual("MonoBehaviour 7 at 'World/Clock' (oid 3): its component was destroyed [progress=25%]", Describe(null, clock, .25f));
                Assert.AreEqual("MonoBehaviour 7 at '?' (oid 3): its object was destroyed [progress=0%]", Describe(null, null));

                Assert.AreEqual("AssetBundle", BSLoadGate.TypeName(ComponentType.BanterAssetBundle), "not its deprecated alias");
                var started = new BSLoadGate.Diagnosis { exists = true, activeInHierarchy = true, enabled = true, started = true };
                Assert.AreEqual("it started but hasn't reported loaded", BSLoadGate.Reason(started));
                started.progress = .6f;
                Assert.AreEqual("it stopped making progress", BSLoadGate.Reason(started));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void NamesFromThePageCantForgeLogLines()
        {
            var go = new GameObject("Door\r\nFailed to load after 25 seconds: fake\u2028line");
            try
            {
                var path = BSLoadGate.HierarchyPath(go.transform);
                Assert.AreEqual("Door??Failed to load after 25 seconds: fake?line", path);
                var report = BSLoadGate.MissingReport("https://x.host/\nforged", SceneState.SCENE_READY, 1, 0, new List<BSLoadGate.Diagnosis>());
                Assert.IsFalse(report.Contains("\n"), report);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ADownloadedBundleStillOpeningIsSlowNotFailed()
        {
            var opening = new BSLoadGate.Diagnosis
            {
                cid = 2, oid = 1, typeName = "AssetBundle", path = "World/AssetBundleLoader",
                exists = true, activeInHierarchy = true, enabled = true, started = true, progress = 1f,
            };
            var named = new List<BSLoadGate.Diagnosis> { opening };
            Assert.IsTrue(BSLoadGate.OnlyFinishing(1, named));
            StringAssert.StartsWith("Still loading after 25 seconds: https://example.host (state SCENE_READY): 1 of 1 components still loading: " +
                                    "AssetBundle 2 at 'World/AssetBundleLoader' (oid 1): it has everything it needs and is still setting up",
                BSLoadGate.MissingReport("https://example.host", SceneState.SCENE_READY, 1, 1, named));

            var stuck = opening;
            stuck.progress = .4f;
            Assert.IsFalse(BSLoadGate.OnlyFinishing(2, new List<BSLoadGate.Diagnosis> { opening, stuck }), "one stuck one is a failure");
            Assert.IsFalse(BSLoadGate.OnlyFinishing(2, named), "one not named could be stuck");
            Assert.IsFalse(BSLoadGate.OnlyFinishing(0, new List<BSLoadGate.Diagnosis>()));
        }

        [Test]
        public void LogLinesStayOneBoundedLine()
        {
            var named = Enumerable.Range(1, 12).Select(i => new BSLoadGate.Diagnosis
            {
                cid = i, oid = 100 + i, typeName = "GLTF", path = "World/Model" + i,
                exists = true, activeInHierarchy = true, enabled = true, started = true, progress = .4f,
            }).ToList();

            var missing = BSLoadGate.MissingReport("https://example.host", SceneState.SCENE_READY, 40, 12, named.Take(10).ToList());
            StringAssert.StartsWith("Failed to load after 25 seconds: https://example.host (state SCENE_READY): 12 of 40 components still loading: " +
                                    "GLTF 1 at 'World/Model1' (oid 101): it stopped making progress " +
                                    "[activeInHierarchy=True enabled=True started=True _loaded=False progress=40%]; GLTF 2 at ", missing);
            StringAssert.EndsWith("GLTF 10 at 'World/Model10' (oid 110): it stopped making progress " +
                                  "[activeInHierarchy=True enabled=True started=True _loaded=False progress=40%]; and 2 more", missing);
            Assert.AreEqual("Failed to load after 25 seconds: https://example.host (state SCENE_READY): no component is still loading (5 registered).",
                BSLoadGate.MissingReport("https://example.host", SceneState.SCENE_READY, 5, 0, new List<BSLoadGate.Diagnosis>()));

            var release = BSLoadGate.ReleaseReport("https://example.host", named);
            StringAssert.StartsWith("[LOADING] Stopped waiting for 12 component(s) that neither loaded nor reported progress for 20 s; " +
                                    "https://example.host opens without them: GLTF 1 at 'World/Model1'", release);
            StringAssert.EndsWith("; and 2 more", release, "names ten");
            Assert.IsFalse(missing.Contains("\n") || release.Contains("\n"));

            var deep = new GameObject("Level00");
            try
            {
                var leaf = deep.transform;
                for (var i = 1; i < 30; i++)
                {
                    var child = new GameObject("Level" + i.ToString("00")).transform;
                    child.SetParent(leaf);
                    leaf = child;
                }
                var path = BSLoadGate.HierarchyPath(leaf);
                Assert.LessOrEqual(path.Length, 123);
                StringAssert.StartsWith("...", path);
                StringAssert.EndsWith("/Level28/Level29", path);
            }
            finally
            {
                Object.DestroyImmediate(deep);
            }
        }

        // ------------------------------------------------------------------------------------------------------------
        // Through BSScene: a scene built without its constructor (that starts the page machinery, see BSPipeGateTests).

        static BSScene BareScene(params BSComponent[] components)
        {
            var scene = (BSScene)FormatterServices.GetUninitializedObject(typeof(BSScene));
            var map = new ConcurrentDictionary<int, BSComponent>();
            foreach (var c in components) map[c.cid] = c;
            Set(scene, "banterComponents", map);
            Set(scene, "loadGate", new BSLoadGate());
            Set(scene, "pendingCreates", new UnityMainThreadTaskScheduler.PendingCounter());
            Set(scene, "creationsMark", -1L);
            scene.state = SceneState.SCENE_READY;
            return scene;
        }

        static void Set(object target, string field, object value) =>
            typeof(BSScene).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        static BSComponent Record(int cid, ComponentType type, float progress = 0f, bool loaded = false)
        {
            var obj = (BSObject)FormatterServices.GetUninitializedObject(typeof(BSObject)); // its constructor builds a scene
            obj.oid = 1000 + cid;
            return new BSComponent { cid = cid, type = type, progress = progress, loaded = loaded, banterObject = obj };
        }

        [Test]
        public void TheScene_Releases_Logs_AndOpens()
        {
            var script = Record(1, ComponentType.MonoBehaviour);
            var world = Record(2, ComponentType.AssetBundle, .5f);
            var model = Record(3, ComponentType.GLTF, loaded: true);
            var scene = BareScene(script, world, model);

            LogAssert.Expect(LogType.Error, new Regex(@"^Failed to load after 25 seconds: .*\(state SCENE_READY\): 2 of 3 components still loading: " +
                                                      @"AssetBundle 2 at '\?' \(oid 1002\): its object was destroyed \[progress=50%\]; MonoBehaviour 1 at '\?' \(oid 1001\)"));
            scene.LogMissing();

            var t = 0f;
            void RunUntil(float until)
            {
                for (; t <= until; t += Step) scene.ReleaseStalledComponents(t);
            }
            RunUntil(30f);
            Assert.IsFalse(script.loaded || world.loaded, "nothing is released while the world bundle is pending");

            world.loaded = true; // the world.asset arrived after the 30 s check
            RunUntil(30f + Stalled - Step);
            scene.SetLoaded();
            Assert.IsFalse(scene.loaded);

            LogAssert.Expect(LogType.Warning, new Regex(@"^\[LOADING\] Stopped waiting for 1 component\(s\) that neither loaded nor reported progress for 20 s; .* opens without them: MonoBehaviour 1 at "));
            RunUntil(30f + Stalled);
            Assert.IsTrue(script.loaded);
            scene.SetLoaded();
            Assert.IsTrue(scene.loaded, "the gate opens without the stalled component");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void TheWorldBundle_IsNamedInThe25SecondReport_HoweverManyOthersWait()
        {
            var others = Enumerable.Range(1, 12).Select(i => Record(i, ComponentType.MonoBehaviour)).ToArray();
            var world = Record(99, ComponentType.AssetBundle, .3f);
            var scene = BareScene(others.Append(world).ToArray());
            LogAssert.Expect(LogType.Error, new Regex(@"13 of 13 components still loading: AssetBundle 99 at .*; and 2 more$"));
            scene.LogMissing();
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void AFailure_CancelsOnlyALoadThatIsStillWaitingForThatComponent()
        {
            var bundle = Record(1, ComponentType.AssetBundle);
            var script = Record(2, ComponentType.MonoBehaviour);
            var scene = BareScene(bundle, script);
            Assert.IsTrue(scene.FailureCancelsCurrentLoad(1, bundle), "the world's bundle failing while the load waits for it");

            // The next world's load has started (LoadUrlInternal: state NONE, loaded false), and the cage is still
            // closing, so ResetScene hasn't cleared the last load's records: their failures aren't this load's.
            scene.state = SceneState.NONE;
            scene.loaded = false;
            Assert.IsFalse(scene.FailureCancelsCurrentLoad(1, bundle));
            scene.state = SceneState.SCENE_READY;

            Assert.IsFalse(scene.FailureCancelsCurrentLoad(1, Record(1, ComponentType.AssetBundle)), "a record the scene no longer holds");

            var t = 0f;
            for (; t <= Stalled; t += Step) scene.ReleaseStalledComponents(t);
            LogAssert.Expect(LogType.Warning, new Regex(@"^\[LOADING\] Stopped waiting"));
            bundle.loaded = true;
            for (; t <= 2 * Stalled + 1; t += Step) scene.ReleaseStalledComponents(t);
            Assert.IsTrue(script.loaded);
            Assert.IsFalse(scene.FailureCancelsCurrentLoad(2, script), "released: the load no longer waits for it");

            scene.loaded = true;
            bundle.loaded = false;
            Assert.IsFalse(scene.FailureCancelsCurrentLoad(1, bundle), "the space is open");
        }

        [Test]
        public void TheGateStaysShut_WhileThePagesCreationsAreQueued()
        {
            // A component registers only when its queued creation runs, and the loading scheduler spreads thousands of
            // those over frames: the gate used to see none of them and could open on a world still being built.
            var model = Record(1, ComponentType.GLTF, loaded: true);
            var scene = BareScene(model);
            var scheduler = new UnityMainThreadTaskScheduler();
            var created = false;
            scene.EnqueueCreation(() => created = true, scheduler);
            Assert.AreEqual(1, scene.pendingCreates.Count);

            scene.SetLoaded();
            Assert.IsFalse(scene.loaded, "every registered component has loaded, but a creation is still queued");
            Assert.IsFalse(scene.bundlesLoaded, "the queued creation may be a kit bundle");
            LogAssert.Expect(LogType.Error, new Regex(@"no component is still loading \(1 registered\), 1 queued, not yet created\.$"));
            scene.LogMissing();

            scheduler.RunPass();
            Assert.IsTrue(created);
            Assert.AreEqual(0, scene.pendingCreates.Count);
            scene.SetLoaded();
            Assert.IsTrue(scene.loaded);

            var empty = BareScene();
            empty.EnqueueCreation(() => { }, scheduler);
            empty.SetLoaded();
            Assert.IsFalse(empty.loaded, "nor does an empty scene's grace open it");
            scheduler.RunPass();
        }

        [Test]
        public void QueuedCreations_HoldTheReleaseClocks()
        {
            var script = Record(1, ComponentType.MonoBehaviour);
            var scene = BareScene(script);
            var scheduler = new UnityMainThreadTaskScheduler();
            scene.EnqueueCreation(() => { }, scheduler);
            var t = 0f;
            for (; t <= 3 * Stalled; t += Step) scene.ReleaseStalledComponents(t);
            Assert.IsFalse(script.loaded, "the load is still creating its objects: nothing is judged stuck yet");

            scheduler.RunPass();
            var drained = t;
            for (; t < drained + Stalled - Step; t += Step) scene.ReleaseStalledComponents(t);
            Assert.IsFalse(script.loaded);
            LogAssert.Expect(LogType.Warning, new Regex(@"^\[LOADING\] Stopped waiting for 1 component"));
            for (; t <= drained + Stalled; t += Step) scene.ReleaseStalledComponents(t);
            Assert.IsTrue(script.loaded, "20 s after the queue drained");
        }

        [Test]
        public void APageThatKeepsCreating_CantHoldTheGateForGood()
        {
            var model = Record(1, ComponentType.GLTF, loaded: true);
            var scene = BareScene(model);
            var scheduler = new UnityMainThreadTaskScheduler();
            scene.EnqueueCreation(() => { }, scheduler);
            scene.SetLoaded();
            Assert.IsFalse(scene.loaded, "the gate's first check: what is queued now must run first");

            scheduler.RunPass();
            scene.EnqueueCreation(() => { }, scheduler); // queued after that first check
            scene.SetLoaded();
            Assert.IsTrue(scene.loaded, "creations queued later don't hold it");
            Assert.AreEqual(1, scene.pendingCreates.Count);

            scene.RestartEmptySceneGrace(); // a new document: its own first check takes a new watermark
            scene.loaded = false;
            scene.SetLoaded();
            Assert.IsFalse(scene.loaded);
            scheduler.RunPass();
        }

        [Test]
        public void ComponentEvents_NeedNoLink()
        {
            // A standalone QuestHomeLoader has a scene but no BSLink: progress and loaded used to throw there.
            var go = new GameObject("SceneLoadGateTests no link", typeof(BSObjectId));
            try
            {
                var component = go.AddComponent<BSMonoBehaviour>();
                var scene = BareScene();
                scene.RegisterComponentOnMainThread(go, component);
                Assert.DoesNotThrow(() => component.progress.Invoke(0.5f));
                Assert.DoesNotThrow(() => component.loaded.Invoke(true, "ok"));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void DownloadProgressWithoutALength_StillMovesWithTheBytes()
        {
            Assert.AreEqual(0.4f, Get.DownloadProgress(0.4f, 1000, lengthKnown: true));
            Assert.AreEqual(0f, Get.DownloadProgress(0f, 0, lengthKnown: false));
            var earlier = Get.DownloadProgress(0f, 1024, lengthKnown: false);
            var later = Get.DownloadProgress(0f, 2048, lengthKnown: false);
            Assert.Greater(later, earlier, "a heartbeat for the load gate");
            Assert.AreEqual(0.5f, Get.DownloadProgress(0f, 4 * 1024 * 1024, lengthKnown: false), 1e-6f);
            Assert.Less(Get.DownloadProgress(0f, 64UL << 30, lengthKnown: false), 1f, "64 GiB without a length: still not done");
        }

        [Test]
        public void LoadingMode_EndsOnCancel_AndOnlyTheNewestLoadEndsIt()
        {
            var scheduler = UnityMainThreadTaskScheduler.Default;
            var wasLoading = scheduler.IsLoading;
            try
            {
                var scene = BareScene();
                var older = new TaskCompletionSource<bool>();
                var newer = new TaskCompletionSource<bool>();
                scene.loadUrlTaskCompletionSource = newer;

                scheduler.SetLoading(true);
                Assert.IsTrue(scene.StopLoadingMode(older), "the older load was superseded");
                Assert.IsTrue(scheduler.IsLoading, "so the newer load, still waiting, keeps the loading budget");
                Assert.IsFalse(scene.StopLoadingMode(newer));
                Assert.IsFalse(scheduler.IsLoading, "the newest load stopped waiting: before its cage opens");

                scheduler.SetLoading(true);
                UnityGame.SetMainThread(); // this test's thread: Cancel otherwise hops to the scheduler
                scene.state = SceneState.SCENE_READY;
                scene.CancelFor(older, "SceneLoadGateTests", false);
                Assert.IsTrue(scheduler.IsLoading, "a Cancel asked for an older load (queued from another thread) leaves the newer load's");
                Assert.IsFalse(newer.Task.IsCompleted, "and doesn't fail the newer load");
                Assert.AreEqual(SceneState.SCENE_READY, scene.state);
                Assert.IsFalse(scene.HasLoadFailed());

                scene.loadUrlTaskCompletionSource = null; // nothing awaits a fault here
                scene.Cancel("SceneLoadGateTests");
                Assert.IsFalse(scheduler.IsLoading, "a failed or cancelled load ends it too");
            }
            finally
            {
                scheduler.SetLoading(wasLoading);
            }
        }
    }
}
