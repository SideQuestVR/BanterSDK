using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BS.Utilities.Async;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// The asset database through the registry and the link: BS.Assets' requests, a world change, and a world's scene as an
    /// asset source. The literal messages are the ones Injection~/test/wire/assets-wire.test.mjs expects on the page. Get's
    /// cache stands in for a download. With no pipe, Send buffers into the link's `messages` list.
    /// </summary>
    public class BSAssetRequestsTests
    {
        const string P = MessageDelimiters.PRIMARY;
        const string S = MessageDelimiters.SECONDARY;
        const string Url = "https://cdn.example/a.png";

        static readonly FieldInfo SceneInstance = typeof(BSScene).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic);
        static readonly FieldInfo RegistryInstance = typeof(BSAssetRegistry).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic);

        static ConcurrentDictionary<string, UnityEngine.Object> Cache =>
            (ConcurrentDictionary<string, UnityEngine.Object>)typeof(Get).GetField("objectCache", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);

        GameObject _go, _registryGo;
        BSLink _link;
        BSAssetRegistry _registry;
        List<string> _sent;
        object _previousScene, _previousRegistry;
        SynchronizationContext _context;
        readonly List<UnityEngine.Object> _created = new List<UnityEngine.Object>();
        readonly List<string> _seeded = new List<string>();
        List<object> _parked;
        Scene _scene;

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
            // Without one a continuation runs where its task completes, so these need no frames.
            _context = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);

            _go = new GameObject("BSAssetRequestsTests");
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
            SynchronizationContext.SetSynchronizationContext(_context);
            SceneInstance.SetValue(null, _previousScene);
            RegistryInstance.SetValue(null, _previousRegistry);
            foreach (var url in _seeded) Cache.TryRemove(url, out _);
            _seeded.Clear();
            foreach (var created in _created) UnityEngine.Object.DestroyImmediate(created);
            _created.Clear();
            if (_scene.IsValid()) EditorSceneManager.ClosePreviewScene(_scene);
            _scene = default;
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

        static string Prefix(int id) => APICommands.REQUEST_ID + MessageDelimiters.REQUEST_ID + id;

        /// <summary>A BS.Assets request as the page sends it; what Unity answered to it, without the id.</summary>
        string Ask(int id, string fields)
        {
            _sent.Clear();
            Handle(Prefix(id) + P + APICommands.ASSET_DATABASE + P + fields);
            RunQueued();
            var reply = _sent.Single(m => m.StartsWith(Prefix(id) + P));
            return reply.Substring(Prefix(id).Length + P.Length);
        }

        [System.Serializable]
        class Reply
        {
            public string id, key, url;
            public int type, size;
        }

        Reply Load(string key, int id = 1) => JsonUtility.FromJson<Reply>(Ask(id, "load" + S + key + S));

        // ---------------------------------------------------------------------------------------------- BS.Assets

        [Test]
        public void Hello_IsAnswered()
        {
            Assert.AreEqual("1", Ask(1, "hello"));
        }

        [Test]
        public void AHostWithoutIt_RefusesAsUnsupported()
        {
            _link.scene.data.UnsupportedRequests.Add(APICommands.ASSET_DATABASE);
            LogAssert.Expect(LogType.Warning, new Regex("doesn't support"));
            StringAssert.Contains("unsupported", Ask(1, "hello"));
        }

        [Test]
        public void AUrl_Loads_UnderAnIdComponentsResolve_AndThePageHearsIt()
        {
            var texture = Cached(Url);
            var reply = Load("url:" + Url);
            var id = reply.id;

            StringAssert.StartsWith("asset_", id);
            Assert.AreEqual("url:" + Url, reply.key);
            Assert.AreEqual((int)AssetType.Texture2D, reply.type);
            Assert.AreEqual(16, reply.size);
            Assert.AreEqual(Url, reply.url);
            Assert.AreSame(texture, _registry.GetAsset<Texture2D>(id));
            Assert.AreSame(texture, _registry.GetAsset<Texture2D>("url:" + Url));
            CollectionAssert.Contains(_sent, "!al!" + P + id + S + "16");
        }

        [Test]
        public void TheSameKey_AnswersWithTheSameId_AndNeedsAReleaseEach()
        {
            Cached(Url);
            var id = Load("url:" + Url).id;
            Assert.AreEqual(id, Load(Url, 2).id);
            Assert.AreEqual(2, _registry.GetReferenceCount(id));

            Assert.AreEqual("{\"id\":\"" + id + "\"}", Ask(3, "release" + S + id));
            Assert.AreEqual("{\"id\":\"" + id + "\"}", Ask(4, "release" + S + "url:" + Url));
            Assert.AreEqual("null", Ask(5, "release" + S + id), "nothing left to release");
        }

        [Test]
        public void AKeyWithADelimiter_IsDecoded()
        {
            var url = "https://cdn.example/a" + S + "b.png";
            var texture = Cached(url);
            var id = JsonUtility.FromJson<Reply>(WireText.Decode(Ask(1, "load" + S + "\"url:https://cdn.example/a\\u00a7b.png\"" + S))).id;
            Assert.AreSame(texture, _registry.GetAsset<Texture2D>(id));
        }

        [TestCase("nonsense")]
        [TestCase("url:file:///c:/windows/a.png")]
        [TestCase("world:logo")]
        [TestCase("kit:assets/a.prefab")]
        [TestCase("browser:999")]
        public void ALoadThatCannotWork_IsRefusedInWords(string key)
        {
            StringAssert.StartsWith(APICommands.REQUEST_ERROR + P + APICommands.ASSET_DATABASE + ": ", Ask(1, "load" + S + key + S));
        }

        [Test]
        public void AUrlOfNoKnownType_NeedsItsTypeNamed()
        {
            Cached("https://cdn.example/image");
            StringAssert.Contains("Texture2D", Ask(1, "load" + S + "https://cdn.example/image" + S));
            Assert.AreEqual((int)AssetType.Texture2D, Load("https://cdn.example/image" + S + "0", 2).type);
        }

        [Test]
        public void WhileMemoryIsLow_ADownloadIsRefusedWithLimit()
        {
            Cached(Url);
            _registry.Memory.LowMemoryReported();
            Assert.AreEqual(APICommands.REQUEST_ERROR + P + "LIMIT: memory is low, try again later", Ask(1, "load" + S + "url:" + Url + S));
            Assert.IsNull(_registry.GetAsset<Texture2D>("url:" + Url));
        }

        [Test]
        public void AnEvictedDownload_LeavesGetsCache_ButIsNotDestroyed()
        {
            var texture = Cached(Url);
            var id = Load("url:" + Url).id;
            Ask(2, "release" + S + id);
            _sent.Clear();

            _registry.Memory.LowMemoryReported();
            _registry.Database.Tick();

            Assert.IsFalse(Cache.ContainsKey(Url));
            Assert.IsTrue(texture != null, "a material may share it");
            CollectionAssert.Contains(_sent, "!ad!" + P + id);
        }

        [Test]
        public void Find_ListsWhatIsLoaded_ByPatternAndType()
        {
            Cached(Url);
            Load("url:" + Url);
            _registry.SetAsset(BSLink.WORLD_BROWSER_TEXTURE, Made(new Texture2D(2, 2)), AssetType.Texture2D, "browser:world");

            var all = Ask(2, "find" + S + "" + S);
            StringAssert.Contains("\"key\":\"url:" + Url + "\"", all);
            StringAssert.Contains("\"key\":\"browser:world\"", all);
            var some = Ask(3, "find" + S + "BROWSER:*" + S);
            StringAssert.Contains("\"key\":\"browser:world\"", some);
            StringAssert.DoesNotContain("url:", some);
            StringAssert.Contains("\"loaded\":true", some);
            Assert.AreEqual("[]", Ask(4, "find" + S + "" + S + (int)AssetType.AudioClip));
        }

        [Test]
        public void AFind_WithAPatternPastTheLimit_IsRefused()
        {
            var answer = Ask(1, "find" + S + new string('*', BSAssetPattern.MaxLength + 1) + "x" + S);
            Assert.AreEqual(APICommands.REQUEST_ERROR + P + "LIMIT: a pattern has at most " + BSAssetPattern.MaxLength + " characters", answer);
        }

        [Test]
        public void ALoad_WithAKeyPastTheLimit_IsRefused()
        {
            StringAssert.StartsWith(APICommands.REQUEST_ERROR + P + "LIMIT: a key", Ask(1, "load" + S + "url:https://cdn.example/" + new string('a', BSAssetKey.MaxLength) + S));
        }

        HashSet<string> PageAssets =>
            (HashSet<string>)typeof(BSLink).GetField("pageAssets", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_link);

        [Test]
        public void APage_HoldsSoManyAssets()
        {
            for (var n = 0; n < BSAssetDatabase.MaxScriptEntries; n++) PageAssets.Add("asset_Material_" + n);

            Handle(APICommands.CREATE_ASSET + P + "asset_Material_new" + S + (int)AssetType.Material + S + S);
            RunQueued();
            CollectionAssert.AreEqual(new[] { "!af!" + P + "asset_Material_new" + S + BSAssetDatabase.WorldFull }, _sent);

            Handle(APICommands.DESTROY_ASSET + P + "asset_Material_0");
            RunQueued();
            Handle(APICommands.CREATE_ASSET + P + "asset_Material_new" + S + (int)AssetType.Material + S + S);
            RunQueued();
            Assert.IsNotNull(Made(_registry.GetAsset<Material>("asset_Material_new")));
        }

        [Test]
        public void AFailedAsset_StopsCountingAgainstThePage()
        {
            Handle(APICommands.CREATE_ASSET + P + "asset_Texture2D_1" + S + "0" + S + "file:///c:/a.png" + S);
            Handle(APICommands.CREATE_ASSET + P + "asset_Texture2D_2" + S + "0" + S + S);
            Handle(APICommands.CREATE_ASSET + P + "asset_Mesh_1" + S + "4" + S + "https://cdn.example/a.glb" + S);
            RunQueued();
            Assert.IsEmpty(PageAssets);
        }

        [Test]
        public void AScriptTexture_WaitsItsTurnAmongTheWorldsDownloads()
        {
            var texture = Cached(Url);
            for (var n = 0; n < BSAssetDatabase.MaxDownloads; n++) _registry.Database.EnterDownload();

            Handle(APICommands.CREATE_ASSET + P + "asset_Texture2D_1" + S + "0" + S + Url + S);
            RunQueued();
            Assert.IsNull(_registry.GetAsset<Texture2D>("asset_Texture2D_1"));

            _registry.Database.LeaveDownload();
            Assert.AreSame(texture, _registry.GetAsset<Texture2D>("asset_Texture2D_1"));
        }

        [Test]
        public void AScriptTexture_PastTheWorldsByteBudget_Fails()
        {
            _registry.SetAsset("asset_Texture2D_big", Made(new Texture2D(2, 2)), AssetType.Texture2D, null, Url);
            _registry.Database.Replace(_registry.Database.Find("asset_Texture2D_big"), Made(new Texture2D(2, 2)), BSAssetDatabase.MaxDownloadBytes);
            _sent.Clear();

            Handle(APICommands.CREATE_ASSET + P + "asset_Texture2D_1" + S + "0" + S + Url + S);
            RunQueued();

            CollectionAssert.AreEqual(new[] { "!af!" + P + "asset_Texture2D_1" + S + BSAssetDatabase.WorldFull }, _sent);
        }

        [Test]
        public void ALoadThatLandsAfterThePageLeft_AnswersNobody()
        {
            var finished = new TaskCompletionSource<Texture2D>();
            var downloads = (Dictionary<string, Task<Texture2D>>)typeof(Get).GetField("textureDownloads", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            downloads[Url] = finished.Task;
            try
            {
                Handle(Prefix(1) + P + APICommands.ASSET_DATABASE + P + "load" + S + "url:" + Url + S);
                RunQueued();
                BSAssetRegistry.WorldChanging(Task.CompletedTask);
                _sent.Clear();
                finished.SetResult(Made(new Texture2D(2, 2)));
            }
            finally
            {
                downloads.Remove(Url);
            }
            Assert.IsEmpty(_sent, "the next page's request 1 isn't this one");
        }

        // ------------------------------------------------------------------------------------------ the older wire

        [Test]
        public void QueryAsset_AnswersForAnUnknownId()
        {
            Handle(Prefix(5) + P + APICommands.QUERY_ASSET + P + "asset_Texture2D_none");
            CollectionAssert.AreEqual(new[] { Prefix(5) + P + APICommands.REQUEST_ERROR + P + APICommands.QUERY_ASSET + ": no asset asset_Texture2D_none" }, _sent);
        }

        [Test]
        public void QueryAsset_AnswersForAKnownIdAsItAlwaysDid()
        {
            _registry.SetAsset("asset_Texture2D_1", Made(new Texture2D(2, 2)), AssetType.Texture2D, "hero", Url);
            _sent.Clear();
            Handle(Prefix(5) + P + APICommands.QUERY_ASSET + P + "asset_Texture2D_1");
            CollectionAssert.AreEqual(new[] { Prefix(5) + P + "asset_Texture2D_1" + S + "0" + S + Url + S + "1" + S + "16" + S + "hero" }, _sent);
        }

        [Test]
        public void APagesMaterial_IsDestroyedWithItsAsset()
        {
            const string id = "asset_Material_0001";
            Handle(APICommands.CREATE_ASSET + P + id + S + (int)AssetType.Material + S + S);
            RunQueued();
            var material = _registry.GetAsset<Material>(id);
            Assert.IsNotNull(material);

            Handle(APICommands.DESTROY_ASSET + P + id);
            RunQueued();
            Assert.IsTrue(material == null);
        }

        [Test]
        public void WhileMemoryIsLow_AScriptTextureFails_ButAMaterialIsMade()
        {
            _registry.Memory.LowMemoryReported();
            Handle(APICommands.CREATE_ASSET + P + "asset_Texture2D_1" + S + "0" + S + Url + S);
            RunQueued();
            CollectionAssert.AreEqual(new[] { "!af!" + P + "asset_Texture2D_1" + S + "LIMIT: memory is low, try again later" }, _sent);

            Handle(APICommands.CREATE_ASSET + P + "asset_Material_1" + S + (int)AssetType.Material + S + S);
            RunQueued();
            Made(_registry.GetAsset<Material>("asset_Material_1"));
            Assert.IsNotNull(_registry.GetAsset<Material>("asset_Material_1"));
        }

        [TestCase("file:///c:/a.png")]
        [TestCase("FILE:///c:/a.png")]
        [TestCase("jar:file:///a.apk!/a.png")]
        [TestCase("c:/a.png")]
        public void AScriptTexture_LoadsOnlyFromAWebAddress(string url)
        {
            Cached(url);
            Handle(APICommands.CREATE_ASSET + P + "asset_Texture2D_1" + S + "0" + S + url + S);
            RunQueued();
            CollectionAssert.AreEqual(new[] { "!af!" + P + "asset_Texture2D_1" + S + BSAssetKey.WebAddressesOnly }, _sent);
            Assert.IsNull(_registry.GetAsset<Texture2D>("asset_Texture2D_1"));
        }

        [TestCase("file:///c:/a.ogg")]
        [TestCase("c:/a.ogg")]
        public void AScriptClip_LoadsOnlyFromAWebAddress(string url)
        {
            Handle(APICommands.CREATE_ASSET + P + "asset_AudioClip_1" + S + "2" + S + url + S);
            RunQueued();
            CollectionAssert.AreEqual(new[] { "!af!" + P + "asset_AudioClip_1" + S + BSAssetKey.WebAddressesOnly }, _sent);
            Assert.IsNull(_registry.GetAsset<AudioClip>("asset_AudioClip_1"));
        }

        [Test]
        public void AScriptTexture_FromAWebAddress_StillLoads()
        {
            var texture = Cached(Url);
            Handle(APICommands.CREATE_ASSET + P + "asset_Texture2D_1" + S + "0" + S + Url + S);
            RunQueued();
            Assert.AreSame(texture, _registry.GetAsset<Texture2D>("asset_Texture2D_1"));
        }

        [TestCase("0")]
        [TestCase("3")]
        [TestCase("999")]
        public void ACreate_CannotTakeAnIdInUse(string type)
        {
            var mine = Made(new Texture2D(2, 2));
            _registry.SetAsset("asset_Texture2D_1", mine, AssetType.Texture2D);
            Cached(Url);
            _sent.Clear();

            Handle(APICommands.CREATE_ASSET + P + "asset_Texture2D_1" + S + type + S + Url + S);
            RunQueued();
            Handle(APICommands.DESTROY_ASSET + P + "asset_Texture2D_1");
            RunQueued();

            Assert.AreSame(mine, _registry.GetAsset<Texture2D>("asset_Texture2D_1"));
            CollectionAssert.AreEqual(new[] { "!af!" + P + "asset_Texture2D_1" + S + "This asset id can't be used" }, _sent);
        }

        [TestCase("asset_browser_world")]
        [TestCase("asset_browser_7")]
        [TestCase("browser:7")]
        [TestCase("world:logo")]
        [TestCase("url:" + Url)]
        [TestCase("my-id")]
        public void ACreate_CannotNameAnotherSourcesKey(string id)
        {
            Cached(Url);
            Handle(APICommands.CREATE_ASSET + P + id + S + "0" + S + Url + S);
            RunQueued();
            Assert.IsNull(_registry.GetAsset<Texture2D>(id));
            CollectionAssert.AreEqual(new[] { "!af!" + P + id + S + "This asset id can't be used" }, _sent);
        }

        [Test]
        public void TheSameIdTwice_WhileTheFirstStillLoads_IsRefused()
        {
            var downloads = (Dictionary<string, Task<Texture2D>>)typeof(Get).GetField("textureDownloads", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            var finished = new TaskCompletionSource<Texture2D>();
            downloads[Url] = finished.Task;
            var first = Made(new Texture2D(2, 2));
            try
            {
                Handle(APICommands.CREATE_ASSET + P + "asset_Texture2D_1" + S + "0" + S + Url + S);
                RunQueued();
                Handle(APICommands.CREATE_ASSET + P + "asset_Texture2D_1" + S + "0" + S + Url + S);
                RunQueued();
                finished.SetResult(first);
            }
            finally
            {
                downloads.Remove(Url);
            }
            Assert.AreSame(first, _registry.GetAsset<Texture2D>("asset_Texture2D_1"));
            Assert.AreEqual(1, _sent.Count(m => m.StartsWith("!af!")));
        }

        [TestCase("0")]
        [TestCase("999")]
        public void AWorldBrowsersTexture_SurvivesAPageThatNamedIt_LeavingForTheNextWorld(string type)
        {
            typeof(BSLink).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_link, null);
            var texture = Made(new Texture2D(2, 2));
            _registry.SetAsset(BSLink.WORLD_BROWSER_TEXTURE, texture, AssetType.Texture2D, "browser:world");
            Cached(Url);

            Handle(APICommands.CREATE_ASSET + P + BSLink.WORLD_BROWSER_TEXTURE + S + type + S + Url + S);
            RunQueued();
            BSAssetRegistry.WorldChanging(Task.CompletedTask);
            _link.scene.events.OnLoad.Invoke();

            Assert.AreSame(texture, _registry.GetAsset<Texture2D>(BSLink.WORLD_BROWSER_TEXTURE));
        }

        // -------------------------------------------------------------------------------------------- a world change

        [Test]
        public void Nothing_ScansTheProcessOnStart()
        {
            Assert.IsNull(typeof(BSAssetRegistry).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public));
            var loose = Made(new Texture2D(2, 2));
            loose.name = "Loose";
            _registry.ScanScene();
            CollectionAssert.DoesNotContain(_registry.FindAssets(AssetType.Texture2D), loose);
        }

        [Test]
        public void AWorldsAssets_GoOnceItsSceneHasGone_AndThePageIsNotTold()
        {
            var texture = Cached(Url);
            var id = Load("url:" + Url).id;
            var destroyed = new List<string>();
            _registry.OnAssetDestroyed += destroyed.Add;
            _sent.Clear();
            var gone = new TaskCompletionSource<bool>();

            BSAssetRegistry.WorldChanging(gone.Task);

            Assert.IsNull(_registry.GetAsset<Texture2D>(id), "the next world can't see it");
            Assert.IsEmpty(destroyed, "and it stays until the world's scene has gone");
            Assert.IsTrue(Cache.ContainsKey(Url));

            gone.SetResult(true);

            CollectionAssert.AreEqual(new[] { id }, destroyed);
            Assert.IsFalse(Cache.ContainsKey(Url));
            Assert.IsEmpty(_sent);
        }

        [Test]
        public void AnUnloadThatFails_StillReleases()
        {
            Cached(Url);
            var id = Load("url:" + Url).id;
            var destroyed = new List<string>();
            _registry.OnAssetDestroyed += destroyed.Add;
            var gone = new TaskCompletionSource<bool>();
            BSAssetRegistry.WorldChanging(gone.Task);

            LogAssert.Expect(LogType.Warning, new Regex("releasing its assets anyway"));
            gone.SetException(new System.InvalidOperationException("no"));

            CollectionAssert.AreEqual(new[] { id }, destroyed);
        }

        [Test]
        public void TheWorldBrowsersTexture_IsStillThereInTheNextWorld()
        {
            var texture = Made(new Texture2D(2, 2));
            _registry.SetAsset(BSLink.WORLD_BROWSER_TEXTURE, texture, AssetType.Texture2D, "browser:world");
            BSAssetRegistry.WorldChanging(Task.CompletedTask);
            Assert.AreSame(texture, _registry.GetAsset<Texture2D>(BSLink.WORLD_BROWSER_TEXTURE));
            Assert.AreSame(texture, _registry.GetAsset<Texture2D>("browser:world"));
        }

        [Test]
        public void ThePagesAssets_LeaveWithTheWorld_WithoutAWord()
        {
            const string id = "asset_Material_0001";
            Handle(APICommands.CREATE_ASSET + P + id + S + (int)AssetType.Material + S + S);
            RunQueued();
            var material = _registry.GetAsset<Material>(id);
            _sent.Clear();

            BSAssetRegistry.WorldChanging(Task.CompletedTask);

            Assert.IsTrue(material == null);
            Assert.IsEmpty(_sent);
        }

        [Test]
        public void WithNoRegistry_AWorldChangeIsNothing()
        {
            RegistryInstance.SetValue(null, null);
            Assert.DoesNotThrow(() => BSAssetRegistry.WorldChanging(Task.CompletedTask));
        }

        // -------------------------------------------------------------------------------------------- a world's scene

        // A preview scene: a run's untitled scene stays open and untouched, and closing this one leaves nothing behind.
        GameObject InWorld(string name)
        {
            if (!_scene.IsValid()) _scene = EditorSceneManager.NewPreviewScene();
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, _scene);
            return go;
        }

        BSAssetSources WorldWith(out Texture2D texture, out Material material)
        {
            texture = Made(new Texture2D(2, 2) { name = "Logo" });
            material = Made(new Material(BSShaderResolver.Find("Standard")) { name = "Logo" });
            material.SetTexture(material.GetTexturePropertyNameIDs().First(), texture);
            InWorld("Sign").AddComponent<MeshRenderer>().sharedMaterial = material;
            var scene = _scene;
            return new BSAssetSources { WorldScene = () => scene };
        }

        static BSAssetKey Key(string text)
        {
            Assert.IsTrue(BSAssetKey.TryParse(text, out var key));
            return key;
        }

        [Test]
        public void AWorldKey_FindsWhatTheScenesObjectsUse_ByNameAndType()
        {
            var sources = WorldWith(out var texture, out var material);
            Assert.AreSame(material, sources.Load(Key("world:logo"), AssetType.Material).Result.Asset);
            Assert.AreSame(texture, sources.Load(Key("world:Textures/Logo.png"), AssetType.Texture2D).Result.Asset);
            Assert.IsTrue(sources.Load(Key("world:nothing"), null).IsFaulted);
        }

        [Test]
        public void AnOldWorld_IsListedFromItsScene()
        {
            var sources = WorldWith(out _, out _);
            var listing = sources.List(new BSAssetDatabase(_registry, _registry.Memory)).Single(l => l.Key == "world:logo");
            Assert.AreEqual("Logo", listing.Name);
        }

        [Test]
        public void ANewWorld_IsListedFromItsManifest_AndItsPathsResolve()
        {
            var sources = WorldWith(out var texture, out _);
            var manifest = new BSAssetManifest();
            manifest.Assets.Add(new BSAssetManifest.Item { Name = "Logo", Path = "assets/art/signs.png", Type = AssetType.Texture2D, Size = 99 });
            var holder = InWorld(BSAssetManifest.HolderName);
            foreach (var chunk in BSAssetManifest.ToChunks(manifest.ToJson("now", "unity", "URP", "Linear")))
                InWorld(chunk).transform.SetParent(holder.transform, false);

            var listing = sources.List(new BSAssetDatabase(_registry, _registry.Memory)).Single();
            Assert.AreEqual("world:assets/art/signs.png", listing.Key);
            Assert.AreEqual(99, listing.Size);
            Assert.AreSame(texture, sources.Load(Key(listing.Key), null).Result.Asset);
        }

        // ---------------------------------------------------------------------------------------------- the Builder

        [Test]
        public void TheBuilder_LeavesTheManifestInTheScene_WhereTheRuntimeReadsIt()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>("Packages/com.sidequest.creator-sdk/Runtime/Materials/ShinyBlack.mat");
            Assert.IsNotNull(material);
            InWorld("Sign").AddComponent<MeshRenderer>().sharedMaterial = material;

            AssetManifestStamp.Apply(_scene);

            var holder = _scene.GetRootGameObjects().Single(root => root.name == BSAssetManifest.HolderName);
            Assert.IsFalse(holder.activeSelf);
            CollectionAssert.AreEquivalent(new[] { typeof(Transform) }, holder.GetComponents<Component>().Select(c => c.GetType()), "no script a client could lack");
            var item = BSAssetSources.ReadManifest(_scene).Assets.Single(a => a.Type == AssetType.Material);
            Assert.AreEqual("ShinyBlack", item.Name);
            Assert.AreEqual("packages/com.sidequest.creator-sdk/runtime/materials/shinyblack.mat", item.Path);
        }
    }
}
