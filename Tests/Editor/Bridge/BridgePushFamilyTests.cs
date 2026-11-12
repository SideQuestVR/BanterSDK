using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using NUnit.Framework;
using SideQuest.Ora;
using UnityEngine;

namespace BS.Bridge.Tests
{
    /// <summary>
    /// The M3 families where they meet BSScene and BSLink: with them off, Unity sends the strings it always has; offered
    /// and accepted, "!u!" updates and watched transforms become DELTA (stamped with the page's applied count), presence
    /// markers become ANNOUNCE, and OnDestroy plus the sweep become DESTROYED. Same fake view as BridgeSessionLaneTests.
    /// </summary>
    public class BridgePushFamilyTests
    {
        const uint All = BridgeFamily.Fence | BridgeFamily.Pushed;

        GameObject go;
        BSLink link;
        OraView view;
        BSScene scene;
        readonly ConcurrentQueue<string> toPage = new ConcurrentQueue<string>();
        readonly List<GameObject> made = new List<GameObject>();
        uint savedFamilies;
        double savedDrainBudgetMs, savedLoadingDrainBudgetMs;

        static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [SetUp]
        public void SaveStatics()
        {
            savedFamilies = BSLink.BridgeFamilies;
            savedDrainBudgetMs = BSLink.BridgeDrainBudgetMs;
            savedLoadingDrainBudgetMs = BSLink.BridgeDrainLoadingBudgetMs;
            BSLink.BridgeDrainBudgetMs = BSLink.BridgeDrainLoadingBudgetMs = 60_000;
        }

        [TearDown]
        public void TearDown()
        {
            if (link != null && link.bridge != null) BridgePump.Unregister(link);
            if (link != null) typeof(BSLink).GetMethod("ShutDown", Private)?.Invoke(link, null);
            if (go != null) UnityEngine.Object.DestroyImmediate(go);
            foreach (var m in made) if (m != null) UnityEngine.Object.DestroyImmediate(m);
            made.Clear();
            while (toPage.TryDequeue(out _)) { }
            BSLink.BridgeFamilies = savedFamilies;
            BSLink.BridgeDrainBudgetMs = savedDrainBudgetMs;
            BSLink.BridgeDrainLoadingBudgetMs = savedLoadingDrainBudgetMs;
        }

        /// <summary>A link offering families, its scene with empty tables, and a page that has said HELLO (and ACCEPT).</summary>
        Page Open(uint families, uint accept)
        {
            BSLink.BridgeFamilies = families;
            go = new GameObject("BridgePushFamilyTests");
            go.SetActive(false); // no Awake/Start: no Ora window, no scheduler
            view = go.AddComponent<OraView>();
            link = go.AddComponent<BSLink>();
            scene = (BSScene)FormatterServices.GetUninitializedObject(typeof(BSScene));
            scene.link = link;
            scene.events = new BSSceneEvents();
            scene.events.GetPlatform = () => "editmode";
            scene.events.IsVR = () => false;
            scene.state = SceneState.SCENE_START;
            Set("objects", new ConcurrentDictionary<int, UnityAndBanterObject>());
            Set("banterComponents", new ConcurrentDictionary<int, BSComponent>());
            Set("stampGate", new object());
            link.scene = scene;
            link.ConnectPipe(view, null);
            link.pipe.SendOverride = toPage.Enqueue;
            link.bridge.NotifyLoadStarted("https://world.example/a");
            link.bridge.NotifyOnLoad();
            var page = new Page("doc");
            FromPage(page.Hello("https://world.example/a"));
            page.Receive(Pump());
            if (accept != 0) FromPage(page.Accept(accept));
            page.Receive(Pump());
            return page;
        }

        void Set(string field, object value) => typeof(BSScene).GetField(field, Private).SetValue(scene, value);
        T Get<T>(string field) => (T)typeof(BSScene).GetField(field, Private).GetValue(scene);

        void FromPage(string data) => view.browserMessage.Invoke("1", "custom-message", data);

        List<string> Pump()
        {
            link.BridgeDrain();
            link.BridgeFlush();
            var sent = new List<string>();
            while (toPage.TryDequeue(out var s)) sent.Add(s);
            return sent;
        }

        /// <summary>A registered object: a scene table entry for a real GameObject, as AddBanterObject makes one.</summary>
        int Register(GameObject g)
        {
            int oid = BSScene.UnityId(g);
            var obj = (BSObject)FormatterServices.GetUninitializedObject(typeof(BSObject));
            obj.oid = oid;
            obj.scene = scene;
            obj.banterComponents = new ConcurrentDictionary<int, BSComponent>();
            var entry = new UnityAndBanterObject { gameObject = g, banterObject = obj };
            obj.unityAndBanterObject = entry;
            Get<ConcurrentDictionary<int, UnityAndBanterObject>>("objects").TryAdd(oid, entry);
            return oid;
        }

        GameObject Make(string name)
        {
            var g = new GameObject(name);
            made.Add(g);
            return g;
        }

        static string Part(PropertyName p, params string[] values) => (int)p + MessageDelimiters.TERTIARY + string.Join(MessageDelimiters.TERTIARY, values);

        static BSComponentPropertyUpdate Position(int oid, Vector3 v) => new BSComponentPropertyUpdate
        {
            name = PropertyName.position, type = PropertyType.Vector3, value = v, componentType = ComponentType.Transform, oid = oid,
        };

        sealed class Page
        {
            public readonly BridgeLink Link = new BridgeLink();
            public readonly string Token;
            public uint Epoch, Families;
            public readonly List<string> Texts = new List<string>();
            public readonly List<(uint opcode, byte[] payload)> Pushes = new List<(uint, byte[])>();
            public Page(string token) { Token = token; }

            public string Hello(string href)
            {
                var hello = new HelloPayload { Features = BridgeFeature.TextForm, SdkVersion = "editmode", DocToken = Token, Href = href };
                Link.EnqueueOp(BridgeOps.Encode(BridgeOp.Hello, hello.Encode()));
                return TakeOne();
            }

            public string Accept(uint families)
            {
                Link.EnqueueOp(BridgeOps.Encode(BridgeOp.Accept, BridgePush.EncodeAccept(families)));
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

            public void Receive(IEnumerable<string> fromUnity)
            {
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
                        Families = w.Families;
                        Link.Epoch = Epoch;
                    }
                    if (f.Header.Epoch != Epoch) continue;
                    var msgs = new List<BridgeMessage>();
                    var e = f.IsText ? Link.ReceiveData(f.Header, f.Body, null, 0, 0, msgs)
                                     : Link.ReceiveData(f.Header, null, f.Frame, BridgeWire.HeaderBytes, f.Header.Length, msgs);
                    Assert.AreEqual(BridgeError.None, e);
                    foreach (var m in msgs)
                        foreach (var i in m.Items)
                        {
                            if (i.IsText) Texts.Add(i.Text);
                            else if (i.Opcode == BridgeOp.Delta || i.Opcode == BridgeOp.Announce || i.Opcode == BridgeOp.Destroyed)
                            {
                                Pushes.Add((i.Opcode, i.Payload.ToArray()));
                                Texts.Add("#" + i.Opcode);
                            }
                        }
                }
            }

            public List<DeltaEntry> Delta(int index, out uint applied)
            {
                Assert.AreEqual((uint)BridgeOp.Delta, Pushes[index].opcode);
                Assert.True(BridgePush.TryDecodeDelta(Pushes[index].payload, out applied, out var entries));
                return entries;
            }
        }

        // ------------------------------------------------------------------------------------------------- tests

        [Test]
        public void TheFamiliesFlag_ReadsNamesAndAll()
        {
            Assert.AreEqual(0u, BSLink.ParseBridgeFamilies(null));
            Assert.AreEqual(0u, BSLink.ParseBridgeFamilies(""));
            Assert.AreEqual(BridgeFamily.Fence | BridgeFamily.Delta, BSLink.ParseBridgeFamilies("fence,DELTA"));
            Assert.AreEqual(BridgeFamily.Announce | BridgeFamily.Destroyed, BSLink.ParseBridgeFamilies(" announce ; destroyed nonsense"));
            Assert.AreEqual(All, BSLink.ParseBridgeFamilies("all"));
        }

        [Test]
        public void ChangeStrings_OnlyComponentPropertiesParse()
        {
            Assert.True(BSScene.TryParseChange("5¶6¶7¶8§3§1.5", out int oid, out int cid, out int ct, out int prop, out string value));
            Assert.AreEqual((5, 6, 7, 8, "8§3§1.5"), (oid, cid, ct, prop, value));
            Assert.False(BSScene.TryParseChange("5¶0¶0¶0§0§1", out _, out _, out _, out _, out _), "an object's marker (cid 0)");
            Assert.False(BSScene.TryParseChange("5¶6¶7¶", out _, out _, out _, out _, out _), "no value");
            Assert.False(BSScene.TryParseChange("5¶x¶7¶8§3§1", out _, out _, out _, out _, out _), "not a number");
            Assert.False(BSScene.TryParseChange(null, out _, out _, out _, out _, out _));
        }

        [Test]
        public void TransformDeltaText_IsTheEventsPartExactly()
        {
            var q = new Quaternion(0.1f, -0.2f, 0.3f, 0.9f);
            Assert.AreEqual((int)PropertyName.position + MessageDelimiters.TERTIARY + "1.5" + MessageDelimiters.TERTIARY + "-2" + MessageDelimiters.TERTIARY + "3",
                BSLink.TransformDeltaText(Position(1, new Vector3(1.5f, -2, 3))));
            var rot = new BSComponentPropertyUpdate { name = PropertyName.rotation, value = q };
            Assert.AreEqual((int)PropertyName.rotation + MessageDelimiters.TERTIARY + NumberFormat.Format(q.x) + MessageDelimiters.TERTIARY
                + NumberFormat.Format(q.y) + MessageDelimiters.TERTIARY + NumberFormat.Format(q.z) + MessageDelimiters.TERTIARY + NumberFormat.Format(q.w),
                BSLink.TransformDeltaText(rot));
            Assert.IsNull(BSLink.TransformDeltaText(new BSComponentPropertyUpdate { name = PropertyName.mass, value = 1f }));
        }

        [Test]
        public void FamiliesOff_UnitySendsTheStringsItAlwaysHas_EvenToAPageThatAccepts()
        {
            var page = Open(0, All);
            Assert.AreEqual(0u, page.Families & All, "WELCOME offers none of them");
            Assert.AreEqual(0u, link.bridge.Current.Accepted, "nothing accepted");
            var g = Make("Marker");
            int oid = Register(g);
            scene.EnqueueChange("5¶6¶7¶8§3§1.5");
            scene.FlushObjectToChanges(oid);
            scene.FlushObjectToChanges(5, 6, (ComponentType)7);
            link.OnTransformUpdate(5, new List<BSComponentPropertyUpdate> { Position(5, new Vector3(1, 2, 3)) });
            scene.DestroyBanterObject(oid);
            page.Receive(Pump());
            CollectionAssert.AreEqual(new[]
            {
                APICommands.UPDATE + "5¶6¶7¶8§3§1.5",
                APICommands.UPDATE + oid + "¶0¶0¶0§0§1",
                APICommands.UPDATE + "5¶6¶7¶0§0§1",
                APICommands.EVENT + APICommands.TRANSFORM_UPDATED + MessageDelimiters.PRIMARY + "5" + MessageDelimiters.SECONDARY + Part(PropertyName.position, "1", "2", "3"),
            }, page.Texts);
            Assert.AreEqual(0, page.Pushes.Count);
        }

        [Test]
        public void Delta_ReplacesUpdatesAndWatchedTransforms_NewestPerProperty()
        {
            var page = Open(All, BridgeFamily.Delta);
            Assert.AreEqual(All, page.Families & All, "offered");
            scene.EnqueueChange("5¶6¶7¶8§3§1.5");
            scene.EnqueueChange("5¶6¶7¶9§3§7");
            scene.EnqueueChange("5¶6¶7¶8§3§2.5");
            link.OnTransformUpdate(5, new List<BSComponentPropertyUpdate> { Position(5, new Vector3(1, 2, 3)) });
            link.OnTransformUpdate(5, new List<BSComponentPropertyUpdate> { Position(5, new Vector3(4, 5, 6)) });
            page.Receive(Pump());
            CollectionAssert.AreEqual(new[] { "#" + BridgeOp.Delta }, page.Texts, "one DELTA, no strings");
            var entries = page.Delta(0, out uint applied);
            CollectionAssert.AreEqual(new[] { "5/6/8 8§3§2.5", "5/6/9 9§3§7", "5/0/" + (int)PropertyName.position + " " + Part(PropertyName.position, "4", "5", "6") },
                entries.Select(e => e.Oid + "/" + e.Cid + "/" + e.Prop + " " + e.Text).ToArray());
            Assert.AreEqual(0u, applied, "no page items yet");
        }

        [Test]
        public void Delta_ThePropertyWorkerStampsWhatItReadsWithTheCountFromWhenItWasQueued()
        {
            var page = Open(All, BridgeFamily.Delta);
            // Two page items, dispatched with nothing queued: the applied count is 2.
            FromPage(page.Text(APICommands.KEYBOARD_FOCUS + MessageDelimiters.PRIMARY + "0"));
            FromPage(page.Text(APICommands.KEYBOARD_FOCUS + MessageDelimiters.PRIMARY + "0"));
            page.Receive(Pump());
            Assert.AreEqual(2u, link.bridge.AppliedMark);

            // A component the worker can find, and a queue that hands batches over here instead of to a worker thread.
            var obj = (BSObject)FormatterServices.GetUninitializedObject(typeof(BSObject));
            obj.oid = 5;
            obj.scene = scene;
            obj.banterComponents = new ConcurrentDictionary<int, BSComponent>();
            var comp = new BSComponent { cid = 6, type = (ComponentType)7, banterObject = obj };
            Get<ConcurrentDictionary<int, BSComponent>>("banterComponents").TryAdd(6, comp);
            var batches = new List<List<BSComponentPropertyUpdate>>();
            Set("propertyUpdates", new DrainQueue<BSComponentPropertyUpdate>(b => batches.Add(b), run => run(), e => throw e));

            scene.SetFromUnityProperties(new List<BSComponentPropertyUpdate>
            {
                new BSComponentPropertyUpdate { name = (PropertyName)8, type = PropertyType.Float, value = 1.5f, componentType = (ComponentType)7, oid = 5, cid = 6 },
            });
            Assert.AreEqual(1, batches.Count);
            Assert.AreEqual("DeltaStamp", batches[0][0].GetType().Name, "the batch carries the count it was queued under");
            Assert.AreEqual(2u, (uint)batches[0][0].GetType().GetField("Stamp").GetValue(batches[0][0]));

            // Meanwhile the page sends more: the count moves on, but the worker's value was read before.
            FromPage(page.Text(APICommands.KEYBOARD_FOCUS + MessageDelimiters.PRIMARY + "0"));
            page.Receive(Pump());
            Assert.AreEqual(3u, link.bridge.AppliedMark);
            typeof(BSScene).GetMethod("DoPropertyUpdateQueue", Private).Invoke(scene, new object[] { batches[0] });
            page.Receive(Pump());
            var entries = page.Delta(0, out uint applied);
            Assert.AreEqual(2u, applied, "stamped with the queued count, not the current one");
            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual((5, 6, 7, 8), (entries[0].Oid, entries[0].Cid, entries[0].ComponentType, entries[0].Prop));
            StringAssert.StartsWith("8§", entries[0].Text);

            // Off the worker, a value is stamped with the count as it stands.
            scene.EnqueueChange("5¶6¶7¶9§3§1");
            page.Receive(Pump());
            page.Delta(1, out uint now);
            Assert.AreEqual(3u, now);
        }

        [Test]
        public void Announce_ObjectsWithWhatThePageWouldAskFor_AndComponents()
        {
            var page = Open(All, BridgeFamily.Announce);
            var parent = Make("Parent");
            var g = Make("Child 名前");
            g.transform.SetParent(parent.transform);
            g.layer = 5;
            g.SetActive(false);
            int oid = Register(g);
            scene.FlushObjectToChanges(oid);
            scene.FlushObjectToChanges(oid, 77, (ComponentType)37);
            scene.FlushObjectToChanges(123456789); // not registered: the string, as before
            page.Receive(Pump());
            CollectionAssert.AreEqual(new[] { "#" + BridgeOp.Announce, APICommands.UPDATE + "123456789¶0¶0¶0§0§1" }, page.Texts);
            Assert.True(BridgePush.TryDecodeAnnounce(page.Pushes[0].payload, out var a));
            Assert.AreEqual(2, a.Count);
            Assert.AreEqual((BridgePush.AnnounceObject, oid, false, "Child 名前", 5, g.tag, BSScene.UnityId(parent)),
                (a[0].Kind, a[0].Oid, a[0].Active, a[0].Name, a[0].Layer, a[0].Tag, a[0].Parent));
            Assert.AreEqual((BridgePush.AnnounceComponent, oid, 77, 37), (a[1].Kind, a[1].Oid, a[1].Cid, a[1].ComponentType));
        }

        [Test]
        public void Destroyed_FromOnDestroyAndFromTheSweep()
        {
            var page = Open(All, BridgeFamily.Destroyed);
            int alive = Register(Make("Destroyed by OnDestroy"));
            var never = Make("Destroyed without OnDestroy");
            int swept = Register(never);
            scene.DestroyBanterObject(alive);
            UnityEngine.Object.DestroyImmediate(never); // inactive-style: nothing calls DestroyBanterObject
            page.Receive(Pump()); // BridgeDrain runs the sweep
            CollectionAssert.AreEqual(new[] { "#" + BridgeOp.Destroyed }, page.Texts);
            Assert.True(BridgePush.TryDecodeDestroyed(page.Pushes[0].payload, out var d));
            CollectionAssert.AreEqual(new[] { alive + ":1", swept + ":2" }, d.Select(x => x.handle + ":" + x.reason).ToArray());
            Assert.False(Get<ConcurrentDictionary<int, UnityAndBanterObject>>("objects").ContainsKey(swept), "the swept entry is let go");
            Assert.AreEqual(0, scene.SweepDestroyed(64), "nothing left to find");
        }
    }
}
