using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// The world's asset table on its own: keys, references, the unused list, memory pressure, the end of a world, and the
    /// manifest. No scene or link: a fake memory reading, a clock and a listener stand in for them.
    /// </summary>
    public class BSAssetDatabaseTests
    {
        const long MiB = 1L << 20;

        sealed class FakeMemory : IBSFreeMemory
        {
            public long Free = -1;
            public long FreeBytes() => Free;
        }

        sealed class Listener : IBSAssetListener
        {
            public readonly List<string> Heard = new List<string>();
            public void AssetRegistered(BSAssetEntry entry) => Heard.Add("registered " + entry.Meta.id);
            public void AssetLoaded(BSAssetEntry entry) => Heard.Add("loaded " + entry.Meta.id);
            public void AssetUpdated(BSAssetEntry entry) => Heard.Add("updated " + entry.Meta.id);
            public void AssetRemoved(BSAssetEntry entry, bool tellPage) => Heard.Add((tellPage ? "removed " : "released ") + entry.Meta.id);
        }

        FakeMemory memory;
        BSAssetMemoryPolicy policy;
        Listener listener;
        BSAssetDatabase database;
        float now;
        SynchronizationContext context;
        readonly List<UnityEngine.Object> made = new List<UnityEngine.Object>();

        [SetUp]
        public void SetUp()
        {
            // Without one a load's continuation runs inside SetResult, so these need no frames.
            context = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);
            memory = new FakeMemory();
            now = 0;
            policy = new BSAssetMemoryPolicy(memory, () => now);
            listener = new Listener();
            database = new BSAssetDatabase(listener, policy);
        }

        [TearDown]
        public void TearDown()
        {
            SynchronizationContext.SetSynchronizationContext(context);
            foreach (var created in made) UnityEngine.Object.DestroyImmediate(created);
            made.Clear();
        }

        Texture2D Texture()
        {
            var texture = new Texture2D(2, 2);
            made.Add(texture);
            return texture;
        }

        static BSAssetKey Key(string text)
        {
            Assert.IsTrue(BSAssetKey.TryParse(text, out var key), text);
            return key;
        }

        /// <summary>A load that finishes at once, for an asset of <paramref name="size"/>. Sets <paramref name="disposed"/> when its entry goes.</summary>
        BSAssetEntry Acquire(string key, long size = 16, Action disposed = null, bool critical = false)
        {
            var task = database.Acquire(Key(key), critical, () => Task.FromResult(new BSLoadedAsset
            {
                Asset = Texture(), Type = AssetType.Texture2D, Size = size, Dispose = disposed,
                Url = key.StartsWith("url:") ? key.Substring(4) : null,
            }));
            Assert.IsTrue(task.IsCompleted, "a load that needs no frames");
            return task.GetAwaiter().GetResult();
        }

        void GoLow()
        {
            memory.Free = 100 * MiB;
            now += BSAssetMemoryPolicy.ReadEverySeconds;
            policy.Refresh();
            Assert.IsTrue(policy.IsLow);
        }

        // ---------------------------------------------------------------------------------------------------- keys

        [TestCase("world:Textures/Logo.PNG", "World", "world:textures/logo.png")]
        [TestCase("kit:Assets/Props/Crate.prefab", "Kit", "kit:assets/props/crate.prefab")]
        [TestCase("lib:CartoonCubeWorld/Prefabs/Props/Apple.prefab", "Library", "lib:cartooncubeworld/prefabs/props/apple.prefab")]
        [TestCase("url:https://cdn.example/A.png?x=1", "Url", "url:https://cdn.example/A.png?x=1")]
        [TestCase("https://cdn.example/A.png", "Url", "url:https://cdn.example/A.png")]
        [TestCase("browser:world", "Browser", "browser:world")]
        [TestCase("browser:123", "Browser", "browser:123")]
        [TestCase("asset_browser_world", "Browser", "browser:world")]
        [TestCase("asset_browser_123", "Browser", "browser:123")]
        [TestCase("asset_Texture2D_abc", "Runtime", "asset_Texture2D_abc")]
        [TestCase("runtime:Mirror", "Runtime", "runtime:Mirror")]
        public void AKey_ReadsAsItsKindAndCanonicalForm(string text, string kind, string canonical)
        {
            var key = Key(text);
            Assert.AreEqual(kind, key.Kind.ToString());
            Assert.AreEqual(canonical, key.Text);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("nonsense")]
        [TestCase("world:")]
        [TestCase("kit:")]
        [TestCase("ftp://cdn.example/a.png")]
        public void NotAKey(string text)
        {
            Assert.IsFalse(BSAssetKey.TryParse(text, out _));
        }

        [TestCase("https://cdn.example/a.png", true)]
        [TestCase("HTTP://cdn.example/a.png", true)]
        [TestCase("file:///c:/a.png", false)]
        [TestCase("FILE:///c:/a.png", false)]
        [TestCase("file://server/a.png", false)]
        [TestCase("jar:file:///a.apk!/a.png", false)]
        [TestCase("content://media/a.png", false)]
        [TestCase("ftp://cdn.example/a.png", false)]
        [TestCase("c:/a.png", false)]
        [TestCase("/a.png", false)]
        [TestCase("a.png", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void OnlyHttpAndHttps_AreWebAddresses(string text, bool web)
        {
            Assert.AreEqual(web, BSAssetKey.IsWebAddress(text));
        }

        [TestCase("asset_Texture2D_0d45ba23", true)]
        [TestCase("runtime:Mirror", true)]
        [TestCase("asset_browser_world", false)]
        [TestCase("asset_browser_123", false)]
        [TestCase("browser:world", false)]
        [TestCase("world:logo", false)]
        [TestCase("kit:assets/a.prefab", false)]
        [TestCase("lib:a.prefab", false)]
        [TestCase("url:https://cdn.example/a.png", false)]
        [TestCase("https://cdn.example/a.png", false)]
        [TestCase("my-id", false)]
        [TestCase("", false)]
        public void APageNamesOnlyItsOwnKindOfId(string id, bool allowed)
        {
            Assert.AreEqual(allowed, BSAssetKey.IsPageId(id));
        }

        [Test]
        public void APageIdPastTheLimit_IsRefused()
        {
            Assert.IsTrue(BSAssetKey.IsPageId("asset_" + new string('x', BSAssetKey.MaxLength - 6)));
            Assert.IsFalse(BSAssetKey.IsPageId("asset_" + new string('x', BSAssetKey.MaxLength)));
        }

        [Test]
        public void AnIdOfNoKnownForm_IsItsOwnKey()
        {
            Assert.AreEqual("my-id", BSAssetKey.Canonical("my-id"));
        }

        [Test]
        public void AKitKey_SeparatesItsBundleFromItsPath()
        {
            Assert.AreEqual("assets/a/b.prefab", Key("kit:Assets/A/B.prefab").KitPath);
            Assert.IsNull(Key("kit:Assets/A/B.prefab").KitBundle);
            Assert.AreEqual("assets/a/b.prefab", Key("kit:Props/Assets/A/B.prefab").KitPath);
            Assert.AreEqual("props", Key("kit:Props/Assets/A/B.prefab").KitBundle);
            Assert.AreEqual("packages/x/y.mat", Key("kit:Packages/X/Y.mat").KitPath);
        }

        // ------------------------------------------------------------------------------------------------ patterns

        [TestCase("kit:*.prefab", "kit:assets/a.prefab", true)]
        [TestCase("KIT:*", "kit:assets/a.prefab", true)]
        [TestCase("kit:assets/a.prefab", "KIT:ASSETS/A.PREFAB", true)]
        [TestCase("*a*b*", "xaxbx", true)]
        [TestCase("*a*b", "xaxbx", false)]
        [TestCase("kit:*.prefab", "kit:assets/aXprefab", false)]
        [TestCase("kit:a", "kit:ab", false)]
        [TestCase("a*a", "a", false)]
        [TestCase("a*a", "aa", true)]
        [TestCase("a***b", "ab", true)]
        [TestCase("*", "", true)]
        [TestCase("***", "x", true)]
        [TestCase("*aab*", "aaab", true)]
        [TestCase("*abab*c", "ababxababc", true)]
        [TestCase("*abcabd*", "abcabcabd", true)]
        [TestCase("*abcabd*", "abcabcab", false)]
        public void APattern_TreatsOnlyTheStarAsSpecial(string pattern, string text, bool matches)
        {
            Assert.AreEqual(matches, new BSAssetPattern(pattern).Matches(text));
        }

        [Test]
        public void APattern_AgreesWithARegularExpression()
        {
            var random = new System.Random(7);
            string Letters(string from, int most)
            {
                var letters = new char[random.Next(most + 1)];
                for (var i = 0; i < letters.Length; i++) letters[i] = from[random.Next(from.Length)];
                return new string(letters);
            }
            for (var run = 0; run < 20000; run++)
            {
                var pattern = Letters("ab*", 9);
                var text = Letters("ab", 12);
                var expected = Regex.IsMatch(text, "^" + pattern.Replace("*", ".*") + "$");
                Assert.AreEqual(expected, new BSAssetPattern(pattern).Matches(text), pattern + " on " + text);
            }
        }

        [Test]
        public void APatternPastTheLimit_IsRefused()
        {
            Assert.IsTrue(new BSAssetPattern(new string('*', BSAssetPattern.MaxLength)).Matches("anything"));
            var refused = Assert.Throws<BSAssetLimitException>(() => new BSAssetPattern(new string('*', BSAssetPattern.MaxLength + 1)));
            StringAssert.StartsWith("LIMIT:", refused.Message);
        }

        // -------------------------------------------------------------------------------------------- legacy names

        [Test]
        public void ALegacyBrowserName_AndItsKey_AreTheSameEntry()
        {
            var entry = database.Store(BSLink.WORLD_BROWSER_TEXTURE, Texture(), AssetType.Texture2D, null, "browser:world", 16);
            Assert.AreSame(entry, database.Find("browser:world"));
            Assert.AreSame(entry, database.Find(BSLink.WORLD_BROWSER_TEXTURE));
            Assert.AreEqual(BSLink.WORLD_BROWSER_TEXTURE, entry.Meta.id, "the page keeps hearing the name it knows");
        }

        [Test]
        public void ALoadedAsset_IsFoundByItsKeyAndItsId()
        {
            var entry = Acquire("url:https://cdn.example/a.png");
            StringAssert.StartsWith("asset_", entry.Meta.id, "BSMaterial and BSAudioSource resolve asset_ references");
            Assert.AreSame(entry, database.Find("https://cdn.example/a.png"));
            Assert.AreSame(entry, database.Find(entry.Meta.id));
        }

        // ---------------------------------------------------------------------------------------------- world scope

        [Test]
        public void AWorld_SeesOnlyItsOwnAssets()
        {
            var other = new BSAssetDatabase(new Listener(), policy);
            var entry = Acquire("url:https://cdn.example/a.png");
            Assert.IsNull(other.Find("url:https://cdn.example/a.png"));
            Assert.IsNull(other.Find(entry.Meta.id));
        }

        // ----------------------------------------------------------------------------------------------- references

        [Test]
        public void ReferencesCount_LoadsAndBindings()
        {
            var entry = Acquire("url:https://cdn.example/a.png");
            Acquire("url:https://cdn.example/a.png");
            database.AddReference(entry.Meta.id, "material 1");
            database.AddReference(entry.Meta.id, "material 1");
            Assert.AreEqual(3, database.ReferenceCount(entry.Meta.id), "a binding counts once");

            Assert.IsTrue(database.Release(entry.Meta.id));
            database.RemoveReference(entry.Meta.id, "material 1");
            Assert.AreEqual(1, database.ReferenceCount(entry.Key));
        }

        [Test]
        public void AReleaseWithNoLoadLeft_IsRefused()
        {
            var entry = Acquire("url:https://cdn.example/a.png");
            Assert.IsTrue(database.Release(entry.Key));
            Assert.IsFalse(database.Release(entry.Key));
            Assert.IsFalse(database.Release("url:https://cdn.example/none.png"));
        }

        [Test]
        public void AReferenceTakenBeforeTheAssetExists_IsKept()
        {
            database.AddReference("asset_Texture2D_early", "material 1");
            database.Store("asset_Texture2D_early", Texture(), AssetType.Texture2D, null, null, 16);
            Assert.AreEqual(1, database.ReferenceCount("asset_Texture2D_early"));
        }

        [Test]
        public void AnAssetFromALoad_StaysFoundWhenUnused_AndALoadTakesItBack()
        {
            var disposed = 0;
            var entry = Acquire("url:https://cdn.example/a.png", disposed: () => disposed++);
            database.Release(entry.Key);
            Assert.AreSame(entry, database.Find(entry.Key), "cached, not gone");
            Assert.AreSame(entry, Acquire("url:https://cdn.example/a.png"), "no second load");
            GoLow();
            database.Tick();
            Assert.AreEqual(0, disposed, "in use again, so it isn't evicted");
        }

        [Test]
        public void AnAssetNotFromALoad_IsNotCached()
        {
            var entry = database.Store("asset_Texture2D_page", Texture(), AssetType.Texture2D, null, null, 16);
            database.AddReference(entry.Key, "material 1");
            database.RemoveReference(entry.Key, "material 1");
            GoLow();
            database.Tick();
            Assert.AreSame(entry, database.Find(entry.Key), "its creator decides when it goes");
        }

        // ---------------------------------------------------------------------------------------------- the LRU list

        [Test]
        public void TheUnusedList_IsBoundedByCount_OldestOutFirst()
        {
            var entries = new List<BSAssetEntry>();
            for (var i = 0; i <= BSAssetDatabase.MaxUnusedEntries; i++)
            {
                var entry = Acquire("url:https://cdn.example/" + i + ".png");
                entries.Add(entry);
                database.Release(entry.Key);
            }
            Assert.IsNull(database.Find(entries[0].Key));
            Assert.IsNotNull(database.Find(entries[1].Key));
        }

        [Test]
        public void TheUnusedList_IsBoundedByBytes()
        {
            var half = BSAssetDatabase.MaxUnusedBytes / 2;
            var a = Acquire("url:https://cdn.example/a.png", half);
            var b = Acquire("url:https://cdn.example/b.png", half);
            var c = Acquire("url:https://cdn.example/c.png", half);
            database.Release(a.Key);
            database.Release(b.Key);
            CollectionAssert.Contains(database.Entries, a, "exactly at the limit");
            database.Release(c.Key);
            CollectionAssert.AreEquivalent(new[] { b, c }, database.Entries);
        }

        [Test]
        public void LookingAnAssetUp_MovesItToTheBackOfTheList()
        {
            var a = Acquire("url:https://cdn.example/a.png");
            var b = Acquire("url:https://cdn.example/b.png");
            database.Release(a.Key);
            database.Release(b.Key);
            database.Find(a.Key);

            var order = new List<string>();
            a.Dispose = () => order.Add("a");
            b.Dispose = () => order.Add("b");
            GoLow();
            database.Tick();
            CollectionAssert.AreEqual(new[] { "b", "a" }, order);
        }

        [Test]
        public void AnEvictedAsset_TellsThePage_AndRunsItsDispose()
        {
            var disposed = 0;
            var entry = Acquire("url:https://cdn.example/a.png", disposed: () => disposed++);
            database.Release(entry.Key);
            GoLow();
            database.Tick();
            Assert.AreEqual(1, disposed);
            Assert.IsNull(database.Find(entry.Key));
            CollectionAssert.Contains(listener.Heard, "removed " + entry.Meta.id);
            Assert.AreEqual(1, database.TakeEvicted());
            Assert.AreEqual(0, database.TakeEvicted());
        }

        [Test]
        public void AnAssetInUse_SurvivesLowMemory()
        {
            var entry = Acquire("url:https://cdn.example/a.png");
            GoLow();
            database.Tick();
            Assert.AreSame(entry, database.Find(entry.Key));
        }

        // ------------------------------------------------------------------------------------------------- loading

        [Test]
        public void TwoLoadsOfOneKey_RunOnce()
        {
            var runs = 0;
            var finish = new TaskCompletionSource<BSLoadedAsset>();
            Func<Task<BSLoadedAsset>> load = () => { runs++; return finish.Task; };
            var first = database.Acquire(Key("url:https://cdn.example/a.png"), false, load);
            var second = database.Acquire(Key("url:https://cdn.example/a.png"), false, load);
            finish.SetResult(new BSLoadedAsset { Asset = Texture(), Type = AssetType.Texture2D });
            Assert.AreEqual(1, runs);
            Assert.AreSame(first.GetAwaiter().GetResult(), second.GetAwaiter().GetResult());
            Assert.AreEqual(2, first.Result.PageRefs);
        }

        [Test]
        public void AFailedLoad_CanBeTriedAgain()
        {
            var failed = database.Acquire(Key("url:https://cdn.example/a.png"), false, () => throw new InvalidOperationException("no"));
            Assert.IsTrue(failed.IsFaulted);
            Assert.IsNotNull(Acquire("url:https://cdn.example/a.png"));
        }

        [Test]
        public void AnAssetWhoseObjectWasDestroyed_LoadsAgain()
        {
            var entry = Acquire("kit:assets/a.png");
            UnityEngine.Object.DestroyImmediate(entry.Asset);
            Assert.AreNotSame(entry, Acquire("kit:assets/a.png"));
        }

        // ----------------------------------------------------------------------------------------------- downloads

        /// <summary>A load of a url of its own, which runs until its TaskCompletionSource is finished.</summary>
        (Task<BSAssetEntry> load, TaskCompletionSource<BSLoadedAsset> finish) Download(int number, List<int> started)
        {
            var finish = new TaskCompletionSource<BSLoadedAsset>();
            var load = database.Acquire(Key("url:https://cdn.example/" + number + ".png"), false, () =>
            {
                started.Add(number);
                return finish.Task;
            });
            return (load, finish);
        }

        BSLoadedAsset Loaded() => new BSLoadedAsset { Asset = Texture(), Type = AssetType.Texture2D, Url = "https://cdn.example/x.png", Size = 16 };

        [Test]
        public void Downloads_RunAFewAtATime_AndTheRestWaitInOrder()
        {
            var started = new List<int>();
            var downloads = Enumerable.Range(0, BSAssetDatabase.MaxDownloads + 2).Select(n => Download(n, started)).ToList();
            CollectionAssert.AreEqual(Enumerable.Range(0, BSAssetDatabase.MaxDownloads), started);

            downloads[0].finish.SetResult(Loaded());
            Assert.AreEqual(BSAssetDatabase.MaxDownloads, started.Last());

            downloads[1].finish.SetException(new InvalidOperationException("no"));
            Assert.IsTrue(downloads[1].load.IsFaulted);
            Assert.AreEqual(BSAssetDatabase.MaxDownloads + 1, started.Last(), "a failed download gives up its place too");
        }

        [Test]
        public void TooManyWaitingDownloads_AreRefused()
        {
            var started = new List<int>();
            var count = BSAssetDatabase.MaxDownloads + BSAssetDatabase.MaxWaitingDownloads;
            for (var n = 0; n < count; n++) Assert.IsFalse(Download(n, started).load.IsFaulted);

            var refused = Download(count, started).load;

            Assert.IsTrue(refused.IsFaulted);
            Assert.AreEqual(BSAssetDatabase.TooManyWaiting, refused.Exception.GetBaseException().Message);
        }

        [Test]
        public void AWaitingDownload_AsksAgainWhenItsTurnComes()
        {
            var started = new List<int>();
            var running = Enumerable.Range(0, BSAssetDatabase.MaxDownloads).Select(n => Download(n, started)).ToList();
            var waiting = Download(100, started).load;

            GoLow();
            running[0].finish.SetResult(Loaded());

            Assert.IsTrue(waiting.IsFaulted);
            Assert.AreEqual(BSAssetDatabase.MemoryLow, waiting.Exception.GetBaseException().Message);
            CollectionAssert.DoesNotContain(started, 100);
        }

        [Test]
        public void ACriticalLoad_NeverWaitsForADownload()
        {
            var started = new List<int>();
            for (var n = 0; n < BSAssetDatabase.MaxDownloads; n++) Download(n, started);
            Assert.IsNotNull(Acquire("kit:assets/a.prefab", critical: true));
        }

        [Test]
        public void WaitingDownloads_EndWithTheirWorld()
        {
            var started = new List<int>();
            for (var n = 0; n < BSAssetDatabase.MaxDownloads; n++) Download(n, started);
            var waiting = Download(100, started).load;

            database.Retire();

            Assert.IsTrue(waiting.IsFaulted);
            CollectionAssert.DoesNotContain(started, 100);
        }

        [Test]
        public void EveryWorld_HasItsOwnDownloads()
        {
            var started = new List<int>();
            for (var n = 0; n < BSAssetDatabase.MaxDownloads; n++) Download(n, started);
            var next = new BSAssetDatabase(listener, policy);
            Assert.IsTrue(next.EnterDownload().IsCompleted);
        }

        [Test]
        public void AWorldsDownloads_HaveAByteBudget()
        {
            var big = Acquire("url:https://cdn.example/big.png", BSAssetDatabase.MaxDownloadBytes);

            var refused = database.Acquire(Key("url:https://cdn.example/more.png"), false, () => Task.FromResult(Loaded()));

            Assert.AreEqual(BSAssetDatabase.WorldFull, refused.Exception.GetBaseException().Message);
            Assert.IsNotNull(Acquire("kit:assets/a.prefab", critical: true), "a world's own assets aren't counted against it");

            database.Release(big.Key);
            Assert.IsNotNull(Acquire("url:https://cdn.example/after.png"), "what nothing uses goes first");
            Assert.IsNull(database.Find(big.Key));
        }

        [Test]
        public void ReplacingADownload_CountsItsNewSize()
        {
            var entry = Acquire("url:https://cdn.example/a.png", 16);
            database.Replace(entry, Texture(), BSAssetDatabase.MaxDownloadBytes);
            Assert.IsFalse(database.MayLoad(false));
            database.Replace(entry, Texture(), 16);
            Assert.IsTrue(database.MayLoad(false));
        }

        [Test]
        public void AWorld_HoldsSoManyAssets_AndUnusedOnesGoFirst()
        {
            for (var n = 0; n < BSAssetDatabase.MaxScriptEntries - 1; n++)
                database.Store("asset_Texture2D_" + n, Texture(), AssetType.Texture2D, null, null, 16);
            var unused = Acquire("kit:assets/a.png", critical: true);
            Assert.AreEqual(BSAssetDatabase.WorldFull, Refusal());

            database.Release(unused.Key);
            Assert.IsNull(Refusal());
            Assert.IsNull(database.Find(unused.Key));

            string Refusal() => database.WhyNot(false);
        }

        // -------------------------------------------------------------------------------------------------- memory

        [Test]
        public void LowMemory_EvictsFirst_ThenRefusesWhatIsNotCritical()
        {
            var unused = Acquire("url:https://cdn.example/a.png");
            database.Release(unused.Key);
            memory.Free = 100 * MiB;
            now = BSAssetMemoryPolicy.ReadEverySeconds;

            var ran = false;
            var refused = database.Acquire(Key("url:https://cdn.example/b.png"), false, () => { ran = true; return Task.FromResult(default(BSLoadedAsset)); });

            Assert.IsTrue(refused.IsFaulted);
            Assert.IsInstanceOf<BSAssetLimitException>(refused.Exception.GetBaseException());
            StringAssert.StartsWith("LIMIT:", refused.Exception.GetBaseException().Message);
            Assert.IsFalse(ran);
            Assert.IsNull(database.Find(unused.Key), "the unused list went first");
        }

        [TestCase("world:textures/logo.png")]
        [TestCase("kit:assets/props/crate.prefab")]
        public void ACriticalLoad_IsNeverRefused(string key)
        {
            GoLow();
            Assert.IsTrue(database.MayLoad(true));
            Assert.IsNotNull(Acquire(key, critical: true));

            policy.LowMemoryReported();
            Assert.IsTrue(database.MayLoad(true));
            Assert.IsFalse(database.MayLoad(false));
        }

        [Test]
        public void TheLowMemoryReport_DuringALoad_RefusesNothingCritical()
        {
            var finish = new TaskCompletionSource<BSLoadedAsset>();
            var loading = database.Acquire(Key("kit:assets/a.prefab"), true, () => finish.Task);
            policy.LowMemoryReported();
            database.Tick();
            finish.SetResult(new BSLoadedAsset { Asset = Texture(), Type = AssetType.GameObject });
            Assert.IsNotNull(loading.GetAwaiter().GetResult());
        }

        [Test]
        public void FreeMemory_HasTwoThresholds()
        {
            float Next() => now += BSAssetMemoryPolicy.ReadEverySeconds;

            memory.Free = 250 * MiB; Next();
            Assert.IsFalse(policy.Refresh(), "between the two, and it wasn't low");
            memory.Free = 150 * MiB; Next();
            Assert.IsTrue(policy.Refresh());
            memory.Free = 250 * MiB; Next();
            Assert.IsTrue(policy.Refresh(), "between the two, and it was low");
            memory.Free = 350 * MiB; Next();
            Assert.IsFalse(policy.Refresh());
        }

        [Test]
        public void FreeMemory_IsNotReadOnEveryCheck()
        {
            memory.Free = 500 * MiB;
            policy.Refresh();
            memory.Free = 100 * MiB;
            now += BSAssetMemoryPolicy.ReadEverySeconds / 2;
            Assert.IsFalse(policy.Refresh());
            now += BSAssetMemoryPolicy.ReadEverySeconds;
            Assert.IsTrue(policy.Refresh());
        }

        [Test]
        public void WithNoReadings_TheReportHoldsForAWhile()
        {
            Assert.IsFalse(policy.Refresh());
            policy.LowMemoryReported();
            Assert.IsTrue(policy.Refresh());
            now += BSAssetMemoryPolicy.ReportHoldSeconds + BSAssetMemoryPolicy.ReadEverySeconds;
            Assert.IsFalse(policy.Refresh());
        }

        [Test]
        public void AGoodReading_DoesNotEndTheReportEarly()
        {
            memory.Free = 500 * MiB;
            policy.LowMemoryReported();
            now += BSAssetMemoryPolicy.ReadEverySeconds;
            Assert.IsTrue(policy.Refresh());
        }

        // ----------------------------------------------------------------------------------------------- world end

        [Test]
        public void ReleaseAll_LetsEverythingGo_WithoutTellingThePageThatHasGone()
        {
            var disposed = 0;
            var held = Acquire("url:https://cdn.example/a.png", disposed: () => disposed++);
            var unused = Acquire("url:https://cdn.example/b.png", disposed: () => disposed++);
            database.Release(unused.Key);
            database.Store("asset_Texture2D_page", Texture(), AssetType.Texture2D, null, null, 16);
            listener.Heard.Clear();

            database.ReleaseAll();

            Assert.AreEqual(2, disposed);
            Assert.IsEmpty(database.Entries);
            Assert.AreEqual(3, listener.Heard.Count);
            Assert.IsTrue(listener.Heard.All(h => h.StartsWith("released ")));
            Assert.AreEqual(0, database.ReferenceCount(held.Key));
        }

        [Test]
        public void OneEntrysFailure_DoesNotStopTheRest()
        {
            var disposed = 0;
            Acquire("url:https://cdn.example/a.png", disposed: () => throw new InvalidOperationException("no"));
            Acquire("url:https://cdn.example/b.png", disposed: () => disposed++);
            database.ReleaseAll();
            Assert.AreEqual(1, disposed);
        }

        [Test]
        public void ABrowsersTexture_MovesToTheNextWorld_WithItsReferences()
        {
            var world = database.Store(BSLink.WORLD_BROWSER_TEXTURE, Texture(), AssetType.Texture2D, null, "browser:world", 16);
            var screen = database.Store("asset_browser_7", Texture(), AssetType.Texture2D, null, "browser", 16);
            database.AddReference(screen.Key, "material 1");
            var page = database.Store("asset_Texture2D_page", Texture(), AssetType.Texture2D, null, null, 16);

            var next = new BSAssetDatabase(listener, policy);
            next.TakeBrowsers(database);
            database.Retire();
            database.ReleaseAll();

            Assert.AreSame(world, next.Find("browser:world"));
            Assert.AreSame(screen, next.Find("asset_browser_7"));
            Assert.AreEqual(1, next.ReferenceCount("browser:7"));
            Assert.IsNull(next.Find(page.Key));
            CollectionAssert.DoesNotContain(listener.Heard, "released " + BSLink.WORLD_BROWSER_TEXTURE);
        }

        [Test]
        public void ALoadThatLandsAfterItsWorldLeft_IsDropped()
        {
            var finish = new TaskCompletionSource<BSLoadedAsset>();
            var loading = database.Acquire(Key("url:https://cdn.example/a.png"), false, () => finish.Task);
            database.Retire();
            finish.SetResult(new BSLoadedAsset { Asset = Texture(), Type = AssetType.Texture2D });
            Assert.IsTrue(loading.IsFaulted);
            Assert.IsEmpty(database.Entries);
        }

        // ------------------------------------------------------------------------------------------------ manifest

        const string Sample = "{\"schemaVersion\":1,\"generatedAtUtc\":\"2026-11-06T10:00:00Z\",\"generatorVersion\":\"1.0.0\"," +
            "\"unityVersion\":\"6000.3.21f1\",\"renderPipeline\":\"URP\",\"colorSpace\":\"Linear\",\"counts\":{\"assets\":2},\"assets\":[" +
            "{\"name\":\"Logo\",\"path\":\"assets/textures/logo.png\",\"guid\":\"abc\",\"type\":\"Texture2D\",\"size\":4096}," +
            "{\"name\":\"Hum\",\"path\":\"assets/audio/hum.ogg\",\"type\":\"AudioClip\"}]}";

        [Test]
        public void AManifest_Parses()
        {
            Assert.IsTrue(BSAssetManifest.TryParse(Sample, out var manifest));
            Assert.AreEqual(2, manifest.Assets.Count);
            var logo = manifest.Assets[0];
            Assert.AreEqual("Logo", logo.Name);
            Assert.AreEqual("assets/textures/logo.png", logo.Path);
            Assert.AreEqual("abc", logo.Guid);
            Assert.AreEqual(AssetType.Texture2D, logo.Type);
            Assert.AreEqual(4096, logo.Size);
            Assert.AreEqual(0, manifest.Assets[1].Size, "absent reads as unknown");
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("not json")]
        [TestCase("[]")]
        [TestCase("{\"assets\":[]}")]
        [TestCase("{\"schemaVersion\":1}")]
        [TestCase("{\"schemaVersion\":99,\"assets\":[]}")]
        public void NotAManifestThisClientReads(string json)
        {
            Assert.IsFalse(BSAssetManifest.TryParse(json, out _));
        }

        [Test]
        public void AnAssetOfAnUnknownType_IsStillListed()
        {
            Assert.IsTrue(BSAssetManifest.TryParse("{\"schemaVersion\":1,\"assets\":[{\"path\":\"assets/a.xyz\",\"type\":\"Hologram\"}]}", out var manifest));
            Assert.AreEqual("a", manifest.Assets[0].Name);
            Assert.IsNull(manifest.Assets[0].Type);
        }

        [Test]
        public void AManifest_RoundTrips()
        {
            Assert.IsTrue(BSAssetManifest.TryParse(Sample, out var manifest));
            var json = manifest.ToJson("2026-11-06T10:00:00Z", "6000.3.21f1", "URP", "Linear");
            Assert.IsTrue(BSAssetManifest.TryParse(json, out var again));
            Assert.AreEqual(2, again.Assets.Count);
            Assert.AreEqual(AssetType.AudioClip, again.Assets[1].Type);
            StringAssert.Contains("\"counts\":{\"assets\":2}", json);
            StringAssert.Contains("\"schemaVersion\":1", json);
        }

        [Test]
        public void ABundleWithoutAManifest_FallsBackToItsAssetNames()
        {
            var manifest = BSAssetManifest.FromAssetNames(new[] { "assets/props/crate.prefab", "assets/a.png", "assets/b.ogg", "assets/data.bytes", "assets/assets.manifest.json" });
            CollectionAssert.AreEqual(new[] { "crate", "a", "b", "data" }, manifest.Assets.Select(a => a.Name));
            CollectionAssert.AreEqual(new AssetType?[] { AssetType.GameObject, AssetType.Texture2D, AssetType.AudioClip, null }, manifest.Assets.Select(a => a.Type));
            Assert.IsTrue(manifest.Assets.All(a => a.Size == 0));
        }

        [Test]
        public void AManifest_SurvivesBeingSplitIntoNames()
        {
            var json = new string('x', BSAssetManifest.ChunkLength - 1) + "\U0001F600" + new string('y', BSAssetManifest.ChunkLength * 2);
            var chunks = BSAssetManifest.ToChunks(json).ToList();
            Assert.IsTrue(chunks.All(c => c.Length <= BSAssetManifest.ChunkLength));
            Assert.IsTrue(chunks.All(c => !char.IsHighSurrogate(c[c.Length - 1])), "no chunk ends halfway through a character");
            Assert.AreEqual(json, BSAssetManifest.FromChunks(chunks));
            Assert.IsEmpty(BSAssetManifest.ToChunks(""));
        }
    }
}
