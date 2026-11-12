using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BS.Utilities.Async;
using NUnit.Framework;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.TestTools;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// The string bus as the page sees it: script assets (asset.ts sends CREATE_ASSET and DESTROY_ASSET without a request
    /// id, and waits on !al! and !af!), the free text fields, and the order the page and graphs hear an event in. The
    /// literal messages here are the ones Injection~/test/wire/page-wire.test.mjs feeds or expects on the page, so the
    /// two suites hold the same contract from each end. Get's cache stands in for a download. The link has no pipe here,
    /// so Send buffers into its private `messages` list.
    /// </summary>
    public class PageWireTests
    {
        const string P = MessageDelimiters.PRIMARY;
        const string S = MessageDelimiters.SECONDARY;
        const string Id = "asset_Texture2D_0001";
        const string Url = "https://cdn.example/a.png";

        static readonly FieldInfo SceneInstance = typeof(BSScene).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic);
        static readonly FieldInfo RegistryInstance = typeof(BSAssetRegistry).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic);

        static ConcurrentDictionary<string, UnityEngine.Object> Cache =>
            (ConcurrentDictionary<string, UnityEngine.Object>)typeof(Get).GetField("objectCache", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);

        static Dictionary<string, Task<Texture2D>> Downloads =>
            (Dictionary<string, Task<Texture2D>>)typeof(Get).GetField("textureDownloads", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);

        GameObject _go;
        GameObject _registryGo;
        BSLink _link;
        BSAssetRegistry _registry;
        List<string> _sent;
        object _previousScene, _previousRegistry;
        readonly List<UnityEngine.Object> _created = new List<UnityEngine.Object>();
        readonly List<string> _seeded = new List<string>();

        // UnityMainThreadTaskScheduler.Default is shared by every test: parked here so RunQueued runs this test's work only.
        List<object> _parked;

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

        static void RunQueued()
        {
            for (int pass = 0; pass < 100 && UnityMainThreadTaskScheduler.Default.QueuedCount > 0; pass++)
                UnityMainThreadTaskScheduler.Default.RunPass();
        }

        [SetUp]
        public void SetUp()
        {
            _parked = TakeAll(SchedulerQueue());

            _go = new GameObject("PageWireTests");
            _go.SetActive(false); // no Awake/Start
            _link = _go.AddComponent<BSLink>();
            _link.scene = (BSScene)FormatterServices.GetUninitializedObject(typeof(BSScene));
            _link.scene.link = _link;
            _link.scene.events = new BSSceneEvents();
            _link.scene.data = new DataBridge();
            _link.scene.state = SceneState.SCENE_START;
            typeof(BSScene).GetField("banterComponents", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(_link.scene, new ConcurrentDictionary<int, BSComponent>());
            _sent = (List<string>)typeof(BSLink).GetField("messages", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_link);
            _previousScene = SceneInstance.GetValue(null);
            SceneInstance.SetValue(null, _link.scene);

            // Instance would make its own, which DontDestroyOnLoad refuses in Edit Mode.
            _registryGo = new GameObject("BSAssetRegistry");
            _registryGo.SetActive(false);
            _registry = _registryGo.AddComponent<BSAssetRegistry>();
            _previousRegistry = RegistryInstance.GetValue(null);
            RegistryInstance.SetValue(null, _registry);
        }

        [TearDown]
        public void TearDown()
        {
            SceneInstance.SetValue(null, _previousScene);
            RegistryInstance.SetValue(null, _previousRegistry);
            foreach (var url in _seeded) Cache.TryRemove(url, out _);
            _seeded.Clear();
            foreach (var created in _created) UnityEngine.Object.DestroyImmediate(created);
            _created.Clear();
            UnityEngine.Object.DestroyImmediate(_registryGo);
            UnityEngine.Object.DestroyImmediate(_go);

            var queue = SchedulerQueue();
            TakeAll(queue);
            var enqueue = queue.GetType().GetMethod("Enqueue");
            foreach (var item in _parked) enqueue.Invoke(queue, new[] { item });
        }

        T Made<T>(T created) where T : UnityEngine.Object
        {
            _created.Add(created);
            return created;
        }

        Texture2D Cached(string url)
        {
            var texture = Made(new Texture2D(2, 2));
            Cache[url] = texture;
            _seeded.Add(url);
            return texture;
        }

        void Handle(string message) =>
            typeof(BSLink).GetMethod("HandleMessage", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_link, new object[] { message });

        /// <summary>A command as the page sends it, with no request id, and the work it queued.</summary>
        void FromPage(string command, string data)
        {
            Handle(command + P + data);
            RunQueued();
        }

        static string Request(int id, string command, string data) =>
            APICommands.REQUEST_ID + MessageDelimiters.REQUEST_ID + id + P + command + P + data;

        static string CreateMessage(string id, int type, string url, string tag = "") => id + S + type + S + url + S + tag;

        // ---------------------------------------------------------------------------------------------- script assets

        [Test]
        public void ATexture_IsStoredUnderThePagesId_AndThePageHearsItLoaded()
        {
            var texture = Cached(Url);
            string registered = null;
            _registry.OnAssetRegistered += (id, type) => registered = id;

            FromPage(APICommands.CREATE_ASSET, CreateMessage(Id, (int)AssetType.Texture2D, Url, "hero"));

            Assert.AreSame(texture, _registry.GetAsset<Texture2D>(Id));
            Assert.AreEqual(Id, registered, "BSMaterial follows the id through this event");
            var metadata = _registry.GetMetadata(Id).Value;
            Assert.IsTrue(metadata.loaded);
            Assert.AreEqual(Url, metadata.url);
            Assert.AreEqual("hero", metadata.tag);
            Assert.AreSame(texture, _registry.FindAssetByUrl(Url));
            CollectionAssert.AreEqual(new[]
            {
                "!ar!" + P + Id + S + "0" + S + Url + S + "0" + S + "hero",
                "!al!" + P + Id + S + "16",
            }, _sent);
        }

        [Test]
        public void AnAudioClip_IsStoredUnderThePagesId()
        {
            const string id = "asset_Texture2D_0002", url = "https://cdn.example/a.ogg";
            var clip = Made(AudioClip.Create("clip", 100, 1, 1000, false));
            Cache[url] = clip;
            _seeded.Add(url);

            FromPage(APICommands.CREATE_ASSET, CreateMessage(id, (int)AssetType.AudioClip, url));

            Assert.AreSame(clip, _registry.GetAsset<AudioClip>(id));
            Assert.IsTrue(_registry.GetMetadata(id).Value.loaded);
        }

        [Test]
        public void AMaterial_IsStoredUnderThePagesId()
        {
            const string id = "asset_Material_0001";
            FromPage(APICommands.CREATE_ASSET, CreateMessage(id, (int)AssetType.Material, ""));
            var material = _registry.GetAsset<Material>(id);
            Assert.IsNotNull(material);
            Made(material);
            Assert.IsTrue(_registry.GetMetadata(id).Value.loaded);
        }

        [Test]
        public void AUrlAndTagWithADelimiter_AreDecoded()
        {
            var url = "https://cdn.example/a" + S + "b.png";
            var texture = Cached(url);
            FromPage(APICommands.CREATE_ASSET, Id + S + "0" + S + "\"https://cdn.example/a\\u00a7b.png\"" + S + "\"x\\u00a7y\"");
            Assert.AreSame(texture, _registry.GetAsset<Texture2D>(Id));
            Assert.AreEqual(url, _registry.GetMetadata(Id).Value.url);
            Assert.AreEqual("x" + S + "y", _registry.GetMetadata(Id).Value.tag);
        }

        [Test]
        public void AMesh_IsRefusedInWordsThePageCanShow()
        {
            const string id = "asset_Texture2D_0003";
            FromPage(APICommands.CREATE_ASSET, CreateMessage(id, (int)AssetType.Mesh, "https://cdn.example/a.glb"));
            CollectionAssert.AreEqual(new[] { "!af!" + P + id + S + "Mesh assets can't be created from a script" }, _sent);
            Assert.IsFalse(_registry.GetMetadata(id).HasValue);
        }

        [Test]
        public void AFailureWithADelimiter_StaysOneField()
        {
            _registry.MarkAssetFailed(Id, "bad " + S + " url");
            CollectionAssert.AreEqual(new[] { "!af!" + P + Id + S + "\"bad \\u00a7 url\"" }, _sent);
        }

        [Test]
        public void ATextureWithNoUrl_Fails()
        {
            FromPage(APICommands.CREATE_ASSET, CreateMessage(Id, (int)AssetType.Texture2D, ""));
            CollectionAssert.AreEqual(new[] { "!af!" + P + Id + S + "No URL provided" }, _sent);
        }

        [Test]
        public void AMalformedCreate_FailsForItsId()
        {
            FromPage(APICommands.CREATE_ASSET, Id);
            CollectionAssert.AreEqual(new[] { "!af!" + P + Id + S + "Invalid CREATE_ASSET message" }, _sent);

            _sent.Clear();
            FromPage(APICommands.CREATE_ASSET, Id + S + "not a number");
            Assert.AreEqual(1, _sent.Count);
            StringAssert.StartsWith("!af!" + P + Id + S, _sent[0]);
        }

        [Test]
        public void AsARequest_ItIsStillAnswered()
        {
            Handle(Request(5, APICommands.CREATE_ASSET, CreateMessage(Id, (int)AssetType.Mesh, "https://cdn.example/a.glb")));
            CollectionAssert.Contains(_sent, APICommands.REQUEST_ID + MessageDelimiters.REQUEST_ID + 5);

            _sent.Clear();
            Handle(Request(6, APICommands.CREATE_ASSET, Id + S + "not a number"));
            Assert.AreEqual(2, _sent.Count, "the failure, and the reply");
            StringAssert.StartsWith(APICommands.REQUEST_ID + MessageDelimiters.REQUEST_ID + 6 + P + APICommands.REQUEST_ERROR, _sent[1]);

            _sent.Clear();
            Handle(Request(7, APICommands.DESTROY_ASSET, Id));
            CollectionAssert.AreEqual(new[] { APICommands.REQUEST_ID + MessageDelimiters.REQUEST_ID + 7 }, _sent);
        }

        [Test]
        public void Destroy_UnregistersAndLeavesTheSharedObjectAlone()
        {
            var texture = Cached(Url);
            FromPage(APICommands.CREATE_ASSET, CreateMessage(Id, (int)AssetType.Texture2D, Url));
            _sent.Clear();

            FromPage(APICommands.DESTROY_ASSET, Id);

            Assert.IsFalse(_registry.GetMetadata(Id).HasValue);
            Assert.IsTrue(texture != null, "Get's cache owns it, and other assets may share it");
            CollectionAssert.AreEqual(new[] { "!ad!" + P + Id }, _sent);
        }

        [Test]
        public void Destroy_OnlyReachesThePagesOwnAssets()
        {
            var texture = Made(new Texture2D(2, 2));
            _registry.SetAsset(BSLink.WORLD_BROWSER_TEXTURE, texture, AssetType.Texture2D, "browser:world");

            FromPage(APICommands.DESTROY_ASSET, BSLink.WORLD_BROWSER_TEXTURE);
            Assert.AreSame(texture, _registry.GetAsset<Texture2D>(BSLink.WORLD_BROWSER_TEXTURE));

            FromPage(APICommands.CREATE_ASSET, CreateMessage(BSLink.WORLD_BROWSER_TEXTURE, 999, ""));
            FromPage(APICommands.DESTROY_ASSET, BSLink.WORLD_BROWSER_TEXTURE);
            Assert.AreSame(texture, _registry.GetAsset<Texture2D>(BSLink.WORLD_BROWSER_TEXTURE), "creating under an id doesn't make it the page's");
        }

        // Without a SynchronizationContext the load's continuation runs inside SetResult, so this needs no frames.
        void WhileTheTextureLoads(Action meanwhile, Texture2D result)
        {
            var context = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);
            var download = new TaskCompletionSource<Texture2D>();
            Downloads[Url] = download.Task;
            try
            {
                FromPage(APICommands.CREATE_ASSET, CreateMessage(Id, (int)AssetType.Texture2D, Url));
                meanwhile();
                download.SetResult(result);
            }
            finally
            {
                Downloads.Remove(Url);
                SynchronizationContext.SetSynchronizationContext(context);
            }
        }

        [Test]
        public void ALoadThatFinishes_IsStored()
        {
            var texture = Made(new Texture2D(2, 2));
            WhileTheTextureLoads(() => Assert.IsEmpty(_sent), texture);
            Assert.AreSame(texture, _registry.GetAsset<Texture2D>(Id));
        }

        [Test]
        public void AnAssetDestroyedWhileLoading_IsNotStored()
        {
            WhileTheTextureLoads(() => FromPage(APICommands.DESTROY_ASSET, Id), Made(new Texture2D(2, 2)));
            Assert.IsFalse(_registry.GetMetadata(Id).HasValue);
            Assert.IsEmpty(_sent);
        }

        void WireStart() => typeof(BSLink).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_link, null);

        [Test]
        public void AnAssetWhosePageWentWhileLoading_IsNotStored()
        {
            WireStart();
            WhileTheTextureLoads(() => _link.scene.events.OnLoad.Invoke(), Made(new Texture2D(2, 2)));
            Assert.IsFalse(_registry.GetMetadata(Id).HasValue);
        }

        [Test]
        public void APagesAssets_LeaveWithIt_AndTheNextPagesStay()
        {
            const string nextId = "asset_Texture2D_0004";
            WireStart();
            Cached(Url);
            FromPage(APICommands.CREATE_ASSET, CreateMessage(Id, (int)AssetType.Texture2D, Url));

            // BSScene.OnLoad queues its reset, and the next document's ops come behind it.
            UnityMainThreadTaskScheduler.Default.Enqueue(() => _link.scene.events.OnLoad.Invoke());
            Handle(APICommands.CREATE_ASSET + P + CreateMessage(nextId, (int)AssetType.Texture2D, Url));
            RunQueued();

            Assert.IsFalse(_registry.GetMetadata(Id).HasValue);
            Assert.IsTrue(_registry.GetMetadata(nextId).HasValue);
        }

        // --------------------------------------------------------------------------------------- page to Unity fields

        [Test]
        public void SpaceProps_DecodeKeysAndValues()
        {
            var got = new List<string>();
            _link.scene.events.OnPublicSpaceStateChanged.AddListener((key, value) => got.Add(key + "=" + value));
            _link.scene.events.OnProtectedSpaceStateChanged.AddListener((key, value) => got.Add("protected " + key + "=" + value));

            Handle(APICommands.SET_PUBLIC_SPACE_PROPS + P + "a|b" + S + "with space|{\"json\":1}");
            Handle(APICommands.SET_PUBLIC_SPACE_PROPS + P + "\"k\\u007c1\"|\"a\\u007cb\\u00a7c\"" + S + "quoted|\"\\\"x\\\"\"");
            Handle(APICommands.SET_PROTECTED_SPACE_PROPS + P + "a|\"b\\u00b6\"");

            CollectionAssert.AreEqual(new[]
            {
                "a=b", "with space={\"json\":1}",
                "k|1=a|b" + S + "c", "quoted=\"x\"",
                "protected a=b" + P,
            }, got);
        }

        [Test]
        public void ARawPropWithAStrayPipe_IsStillRefused()
        {
            LogAssert.Expect(LogType.Error, new Regex("Invalid prop"));
            Handle(APICommands.SET_PUBLIC_SPACE_PROPS + P + "a|b|c");
        }

        [Test]
        public void DeepLink_DecodesTheAppIdAndLeavesTheMessageAlone()
        {
            var got = new List<string>();
            _link.scene.events.OnDeepLink.AddListener((app, message) => got.Add(app + "=" + message));

            Handle(Request(BSLink.NoReplyRequestId, APICommands.DEEP_LINK, "1234" + P + "hello"));
            Handle(Request(BSLink.NoReplyRequestId, APICommands.DEEP_LINK, "\"12\\u00b634\"" + P + "hi|" + S + P + "there"));

            CollectionAssert.AreEqual(new[] { "1234=hello", "12" + P + "34=hi|" + S + P + "there" }, got);
        }

        [Test]
        public void AiImageAndBase64ToCdn_DecodeTheirTextField()
        {
            _link.scene.data.UnsupportedRequests.Clear();
            var got = new List<string>();
            _link.scene.events.OnAiImage.AddListener((prompt, ratio) => got.Add(prompt + "=" + (int)ratio));
            _link.scene.events.OnBase64ToCDN.AddListener((name, base64) => got.Add(name + "=" + base64));

            Handle(Request(1, APICommands.AI_IMAGE, "a red cube" + P + "2"));
            Handle(Request(2, APICommands.AI_IMAGE, "\"a\\u00b6b\"" + P + "1"));
            Handle(Request(3, APICommands.BASE_64_TO_CDN, "file.png" + P + "QUJD"));
            Handle(Request(4, APICommands.BASE_64_TO_CDN, "\"a\\u00b6b.png\"" + P + "QUJD"));
            RunQueued();

            CollectionAssert.AreEqual(new[] { "a red cube=2", "a" + P + "b=1", "file.png=QUJD", "a" + P + "b.png=QUJD" }, got);
        }

        static string Keys((string eventName, KeyValuePair<string, string>[] keyValues) telemetry)
        {
            var text = telemetry.eventName + ":";
            foreach (var pair in telemetry.keyValues) text += pair.Key + "=" + pair.Value + ";";
            return text;
        }

        [TestCase("event" + P + "k|v" + S + "k2|v2", "event:k=v;k2=v2;")]
        [TestCase("\"e\\u00b61\"" + P + "\"k\\u007c\"|\"v\\u00a7\"", "e" + P + "1:k|=v" + S + ";")]
        [TestCase("event" + P, "event:")]
        [TestCase("event", "event:")]
        [TestCase("event" + P + "k", "event:k=;")]
        [TestCase("event" + P + "k|a|b", "event:k=a|b;")]
        public void Telemetry_ReadsEveryField(string data, string expected)
        {
            Assert.AreEqual(expected, Keys(BSLink.ParseTelemetry(data)));
        }

        // --------------------------------------------------------------------------------------- Unity to page fields

        [Test]
        public void TriggerNames_AreEncodedOnlyWhenTheyNeedIt()
        {
            var obj = Made(new GameObject("Trigger"));
            var plain = Made(new GameObject("Door")).AddComponent<BoxCollider>();
            var marked = Made(new GameObject("Door" + S + "1")).AddComponent<BoxCollider>();
            int oid = BSScene.UnityId(obj), plainId = BSScene.UnityId(plain.gameObject), markedId = BSScene.UnityId(marked.gameObject);

            _link._OnTriggerEnter(obj, plain);
            _link._OnTriggerStay(obj, plain);
            _link._OnTriggerExit(obj, plain);
            _link._OnTriggerEnter(obj, marked);
            _link._OnTriggerStay(obj, marked);
            _link._OnTriggerExit(obj, marked);

            const string encoded = "\"Door\\u00a71\"";
            CollectionAssert.AreEqual(new[]
            {
                "!e!te!" + P + oid + S + plainId + S + "Untagged" + S + "Door" + S + "-1",
                "!e!ts!" + P + oid + S + plainId + S + "Untagged" + S + "Door",
                "!e!tx!" + P + oid + S + plainId + S + "Untagged" + S + "Door" + S + "-1",
                "!e!te!" + P + oid + S + markedId + S + "Untagged" + S + encoded + S + "-1",
                "!e!ts!" + P + oid + S + markedId + S + "Untagged" + S + encoded,
                "!e!tx!" + P + oid + S + markedId + S + "Untagged" + S + encoded + S + "-1",
            }, _sent);
        }

        // ------------------------------------------------------------------------------------------------ event order

        [Test]
        public void AGraphThatThrows_DoesNotCostThePageItsOneShot()
        {
            var hook = new EventHook("OnOneShot");
            Action<CustomEventArgs> throwing = _ => throw new InvalidOperationException("graph");
            EventBus.Register(hook, throwing);
            try
            {
                Assert.Throws<InvalidOperationException>(() => _link.OnOneShot("hello", "u1", false));
            }
            finally
            {
                EventBus.Unregister(hook, throwing);
            }
            CollectionAssert.AreEqual(new[] { "!e!on!" + P + "u1" + S + "0" + S + "hello" }, _sent);
        }
    }
}
