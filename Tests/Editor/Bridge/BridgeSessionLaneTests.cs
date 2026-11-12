using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BS.Utilities.Async;
using NUnit.Framework;
using SideQuest.Ora;
using UnityEngine;

namespace BS.Bridge.Tests
{
    /// <summary>
    /// BSLink and BSPipe on a Bridge 2 session, through a fake Ora view: an inactive OraView (no Awake, so no
    /// window) whose browserMessage event the test raises, and BSPipe.SendOverride catching what would go to the page.
    /// The pump is driven by hand (BSLink.BridgeDrain / BridgeFlush) instead of the PlayerLoop.
    /// Navigation goes through the bridge's LoadStarted/OnLoad notifications rather than view.loadStarted, whose
    /// handler runs BSScene.OnLoad (not EditMode material).
    /// </summary>
    public class BridgeSessionLaneTests
    {
        GameObject go;
        BSLink link;
        OraView view;
        readonly ConcurrentQueue<string> toPage = new ConcurrentQueue<string>();

        void Setup()
        {
            go = new GameObject("BridgeSessionLaneTests");
            go.SetActive(false); // no Awake/Start: no Ora window, no scheduler
            view = go.AddComponent<OraView>();
            link = go.AddComponent<BSLink>();
            var scene = (BSScene)FormatterServices.GetUninitializedObject(typeof(BSScene));
            scene.link = link;
            scene.events = new BSSceneEvents();
            scene.events.GetPlatform = () => "editmode";
            scene.events.IsVR = () => false;
            scene.state = SceneState.SCENE_START;
            typeof(BSScene).GetField("banterComponents", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(scene, new ConcurrentDictionary<int, BSComponent>());
            link.scene = scene;
            link.ConnectPipe(view, null);
            link.pipe.SendOverride = toPage.Enqueue;
        }

        // UnityMainThreadTaskScheduler.Default is shared by every test. This suite runs it by hand (RunPass), which must
        // not run what other tests left queued (a leftover coroutine has no host here and logs an error), nor leave
        // this test's work for theirs. So its queue is parked for the test and put back after, in order, untouched.
        List<object> parkedSchedulerItems;

        static object SchedulerQueue() => typeof(UnityMainThreadTaskScheduler)
            .GetField("tasks", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(UnityMainThreadTaskScheduler.Default);

        static List<object> TakeAll(object queue)
        {
            var tryDequeue = queue.GetType().GetMethod("TryDequeue");
            var items = new List<object>();
            var args = new object[1];
            while ((bool)tryDequeue.Invoke(queue, args)) { items.Add(args[0]); args[0] = null; }
            return items;
        }

        // The pump is driven by hand inside one editor frame. BSLink's drain budget (4 ms, shared by every drain in a
        // frame, keyed on Time.frameCount) never resets in a test, and first-use JIT alone can spend it during the
        // handshake, after which no drain runs. Each Pump() stands for a frame here, so the budget is out of the way.
        double savedDrainBudgetMs, savedLoadingDrainBudgetMs;

        [SetUp]
        public void ParkSchedulerQueue()
        {
            parkedSchedulerItems = TakeAll(SchedulerQueue());
            savedDrainBudgetMs = BSLink.BridgeDrainBudgetMs;
            savedLoadingDrainBudgetMs = BSLink.BridgeDrainLoadingBudgetMs;
            BSLink.BridgeDrainBudgetMs = BSLink.BridgeDrainLoadingBudgetMs = 60_000;
        }

        /// <summary>Runs what this test queued on the main-thread scheduler (passes have a time budget too).</summary>
        static void RunQueued()
        {
            for (int pass = 0; pass < 100 && UnityMainThreadTaskScheduler.Default.QueuedCount > 0; pass++)
                UnityMainThreadTaskScheduler.Default.RunPass();
        }

        [TearDown]
        public void TearDown()
        {
            if (link != null && link.bridge != null) BridgePump.Unregister(link);
            // Inactive objects get no OnDestroy.
            if (link != null) typeof(BSLink).GetMethod("ShutDown", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(link, null);
            if (go != null) UnityEngine.Object.DestroyImmediate(go);
            while (toPage.TryDequeue(out _)) { }
            // Drop what this test left queued (its link is gone), then give the other tests theirs back.
            var queue = SchedulerQueue();
            foreach (var mine in TakeAll(queue))
                (mine.GetType().GetProperty("Counter")?.GetValue(mine) as UnityMainThreadTaskScheduler.PendingCounter)?.Release();
            var enqueue = queue.GetType().GetMethod("Enqueue");
            if (parkedSchedulerItems != null) foreach (var item in parkedSchedulerItems) enqueue.Invoke(queue, new[] { item });
            parkedSchedulerItems = null;
            BSLink.BridgeDrainBudgetMs = savedDrainBudgetMs;
            BSLink.BridgeDrainLoadingBudgetMs = savedLoadingDrainBudgetMs;
        }

        // ---------------------------------------------------------------------------------------------- page side

        sealed class Page
        {
            public readonly BridgeLink Link = new BridgeLink();
            public readonly string Token;
            public uint Epoch;
            public Page(string token) { Token = token; }

            public string Hello(string href)
            {
                var hello = new HelloPayload { Features = BridgeFeature.TextForm, SdkVersion = "editmode", DocToken = Token, Href = href };
                Link.EnqueueOp(BridgeOps.Encode(BridgeOp.Hello, hello.Encode()));
                return TakeOne();
            }

            public string Text(string text)
            {
                Link.EnqueueText(text);
                return TakeOne();
            }

            string TakeOne()
            {
                var frames = new List<OutFrame>();
                Link.TakeFrames(frames);
                Assert.AreEqual(1, frames.Count);
                return frames[0].ToCarrier();
            }

            /// <summary>Consumes what Unity sent; returns the strings it carried (WELCOME adopts the epoch).</summary>
            public List<string> Receive(IEnumerable<string> fromUnity)
            {
                var texts = new List<string>();
                foreach (var s in fromUnity)
                {
                    Assert.AreEqual(BridgeError.None, BridgeCarrier.TryDecode(s, out var f), "Unity sent a bad carrier: " + s);
                    if (f.Header.Kind != FrameKind.Data) continue;
                    if (Epoch == 0)
                    {
                        var ops = new List<RawOp>();
                        BridgeOps.Decode(f.Payload, ops);
                        var w = WelcomePayload.TryDecode(f.Payload.Slice(ops[0].Offset, ops[0].Length));
                        Assert.NotNull(w, "the first frame is WELCOME");
                        Epoch = w.Epoch;
                        Link.Epoch = Epoch;
                    }
                    if (f.Header.Epoch != Epoch) continue;
                    var msgs = new List<BridgeMessage>();
                    var e = f.IsText ? Link.ReceiveData(f.Header, f.Body, null, 0, 0, msgs)
                                     : Link.ReceiveData(f.Header, null, f.Frame, BridgeWire.HeaderBytes, f.Header.Length, msgs);
                    Assert.AreEqual(BridgeError.None, e);
                    foreach (var m in msgs) texts.AddRange(m.Items.Where(i => i.IsText).Select(i => i.Text));
                }
                return texts;
            }
        }

        void FromPage(string data) => view.browserMessage.Invoke("1", "custom-message", data);

        List<string> Pump()
        {
            link.BridgeDrain();
            link.BridgeFlush();
            var sent = new List<string>();
            while (toPage.TryDequeue(out var s)) sent.Add(s);
            return sent;
        }

        static string Request(int id, string command, string data = "") =>
            APICommands.REQUEST_ID + MessageDelimiters.REQUEST_ID + id + MessageDelimiters.PRIMARY + command + MessageDelimiters.PRIMARY + data;

        static string Reply(int id, string payload) =>
            APICommands.REQUEST_ID + MessageDelimiters.REQUEST_ID + id + MessageDelimiters.PRIMARY + payload;

        // ------------------------------------------------------------------------------------------------- tests

        [Test]
        public void SessionLane_Handshake_Request_Reply_Navigation_LateReplyDropped()
        {
            Setup();
            Assert.NotNull(link.bridge, "the link has its bridge");
            Assert.True(BridgePump.Installed, "pump in the PlayerLoop");
            link.bridge.NotifyLoadStarted("https://world.example/a");
            link.bridge.NotifyOnLoad();

            // Handshake. The page's HELLO arrives before the pipe's start-up gate opened: frames bypass it.
            var page1 = new Page("doc-1");
            FromPage(page1.Hello("https://world.example/a"));
            page1.Receive(Pump());
            Assert.AreEqual(1u, page1.Epoch);

            // A request and its reply (GET_PLATFORM answers synchronously through BSLink.Send -> the bridge).
            FromPage(page1.Text(Request(1, APICommands.GET_PLATFORM)));
            CollectionAssert.AreEqual(new[] { Reply(1, "editmode") }, page1.Receive(Pump()));

            // Navigation mid-request: GET_DEVICE_INFO answers from queued main-thread work (the real path: the scheduler
            // keeps the queuing document's epoch), and that work runs only after the page has gone.
            FromPage(page1.Text(Request(2, APICommands.GET_DEVICE_INFO)));
            Assert.AreEqual(0, page1.Receive(Pump()).Count, "queued, not answered yet");
            link.bridge.NotifyLoadStarted("https://world.example/b");
            var close = toPage.ToArray();
            Assert.AreEqual(1, close.Length, "CLOSE to the replaced document");
            while (toPage.TryDequeue(out _)) { }
            link.bridge.NotifyOnLoad();
            RunQueued(); // the queued GetDeviceInfo replies now, for document 1
            Assert.AreEqual(0, Pump().Count, "the late reply is dropped");
            Assert.AreEqual(1L, link.bridge.Stats.LateReplies);

            // The next document reuses id 2 and gets exactly its own answer.
            var page2 = new Page("doc-2");
            FromPage(page2.Hello("https://world.example/b"));
            page2.Receive(Pump());
            Assert.AreEqual(2u, page2.Epoch);
            FromPage(page2.Text(Request(2, APICommands.GET_DEVICE_INFO)));
            Pump();
            RunQueued();
            CollectionAssert.AreEqual(new[] { Reply(2, BSScene.FormatDeviceInfo(Application.platform == RuntimePlatform.Android,
                Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor, false)) }, page2.Receive(Pump()));
        }

        [Test]
        public void TheSpaceView_GetsItsLinkProfile_OnBothOfTheStartersPaths()
        {
            // Before the view's Awake, which opens its channel: a view BSStarterUpper builds, and one that came with the
            // prefab (Greenfield's Creator SDK prefab).
            var starter = System.IO.File.ReadAllText("Packages/com.sidequest.creator-sdk/Runtime/Scripts/BSStarterUpper.cs");
            Assert.AreEqual(2, System.Text.RegularExpressions.Regex.Matches(starter, @"oraView\.linkProfile = OraChannelProfile\.SdkView;").Count);
        }

        [Test]
        public void InTheEditor_LoadStartedDoesNotTrail()
        {
            // Only an Android player delivers a page's messages ahead of its LoadStarted.
            Setup();
            Assert.False(link.bridge.Options.LoadStartedMayTrail);
        }
    }
}
