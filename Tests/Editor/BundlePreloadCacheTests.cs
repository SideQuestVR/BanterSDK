using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BS.Security;
using NUnit.Framework;
#if UNITY_EDITOR
using UnityEngine.TestTools;
#endif

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// The pre-load check as the loaders use it (Runtime/Scripts/Security/BundlePreload, the service, and Basis' seam): the
    /// raw-bundle load end to end through its steps (download, recompress and Unity's load are fakes here), what may and may
    /// not change the cache after a check, the verdict kept beside a cached .bee, the check thread and its budget, abandoned
    /// checks, reuse records, the enforcement switch, the LZMA property clamp and the platform defaults. Bundles come from
    /// the byte-level writer in BundlePreloadCheckTests (this is the other half of that class). Tests in the
    /// "UnityRuntime" category need Unity itself (logs, Application) and are skipped by the offline runner.
    /// </summary>
    /// <remarks>
    /// No test here blocks this thread on a task. In the editor this thread carries Unity's synchronization context, and
    /// the loaders resume their awaits on it: blocking it would deadlock. Tests that wait for a task are [UnityTest]s in the
    /// editor and yield frames until it ends (<see cref="Await"/>, which fails the test after a deadline); the offline
    /// runner, which has no such context, drains the same routines (<see cref="Drain"/>). Every other wait has a deadline.
    /// </remarks>
    public partial class BundlePreloadCheckTests
    {
        const string CacheKey = "0123456789abcdef0123456789abcdef";
        const string RawUrl = "https://cdn.example/kits/kit.banter?token=do-not-store";

        static byte[] RefusedWorld() => ButtonWorld(new Call { Target = 0, TypeName = "System.IO.File, mscorlib", Method = "Delete", Mode = 5 });

        static byte[] DamagedWorld()
        {
            var whole = ButtonWorld(new Call());
            return whole.Take(whole.Length - 16).ToArray();
        }

        /// <summary>Back to what start-up configured (Greenfield in Play mode), else unconfigured.</summary>
        static void RestoreCheckConfiguration()
        {
            if (BundlePreloadHooks.Active) BundlePreloadHooks.EnsureRegistered();
            else BundleCheckService.Configure(null, "");
        }

        // ------------------------------------------------------------------ waiting without blocking

        /// <summary>
        /// Waits for <paramref name="task"/> frame by frame (in the editor its continuations run on this thread between the
        /// frames); fails the test when it has not ended within <paramref name="seconds"/>.
        /// </summary>
        static IEnumerator Await(Task task, double seconds = 10)
        {
            var deadline = DateTime.UtcNow.AddSeconds(seconds);
            while (!task.IsCompleted)
            {
                if (DateTime.UtcNow > deadline) Assert.Fail("a task under test did not end within " + seconds + " s");
                yield return null;
            }
        }

        /// <summary>Waits (without blocking this thread's context) until <paramref name="done"/>, failing after <paramref name="seconds"/>.</summary>
        static IEnumerator Until(Func<bool> done, string what, double seconds = 10)
        {
            var deadline = DateTime.UtcNow.AddSeconds(seconds);
            while (!done())
            {
                if (DateTime.UtcNow > deadline) Assert.Fail(what + " did not happen within " + seconds + " s");
                yield return null;
            }
        }

        /// <summary>Offline: runs a test routine to its end (no synchronization context here: tasks end on the thread pool).</summary>
        static void Drain(IEnumerator routine)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(routine);
            while (stack.Count > 0)
            {
                var top = stack.Peek();
                if (!top.MoveNext())
                {
                    stack.Pop();
                    continue;
                }
                if (top.Current is IEnumerator nested) stack.Push(nested);
                else Thread.Sleep(1);
            }
        }

        /// <summary>The exception an ended task ended with (its first one), or null.</summary>
        static Exception Fault(Task task)
        {
            Assert.IsTrue(task.IsCompleted, "the task has ended");
            if (task.IsCanceled) return new TaskCanceledException(task);
            return task.Exception?.GetBaseException();
        }

        static T Refusal<T>(Task task) where T : Exception
        {
            var fault = Fault(task);
            Assert.IsInstanceOf<T>(fault, "expected " + typeof(T).Name + ", got " + fault);
            return (T)fault;
        }

        // ------------------------------------------------------------------ fixtures

        sealed class TempCache : IDisposable
        {
            internal readonly string Root = Path.Combine(Path.GetTempPath(), "bundle-cache-test-" + Guid.NewGuid().ToString("N"));

            internal TempCache(long capacity = 1L << 30)
            {
                Directory.CreateDirectory(Root);
                BundleFileCache.Configure(Root, capacity);
            }

            internal BundleFileCache.Entry Add(string key, byte[] bundle, string url = RawUrl)
            {
                File.WriteAllBytes(BundleFileCache.PartialPath(key), bundle);
                return BundleFileCache.Commit(key, url);
            }

            public void Dispose()
            {
                RestoreCheckConfiguration();
                try
                {
                    if (Directory.Exists(Root)) Directory.Delete(Root, true);
                }
                catch (IOException)
                {
                    // the OS temp folder is cleaned eventually
                }
            }
        }

        /// <summary>Switches and budgets as a test sets them, put back afterwards.</summary>
        sealed class Settings : IDisposable
        {
            readonly BundleCheckService.EnforcementMode mode = BundleCheckService.Mode;
            readonly TimeSpan wallClock = BundleCheckService.WallClockBudget, grace = BundleCheckService.OverrunGrace;
            readonly long bytes = BundleCheckService.ByteBudget;

            internal Settings(BundleCheckService.EnforcementMode? mode = null)
            {
                if (mode.HasValue) BundleCheckService.Mode = mode.Value;
            }

            public void Dispose()
            {
                BundleCheckService.Mode = mode;
                BundleCheckService.WallClockBudget = wallClock;
                BundleCheckService.OverrunGrace = grace;
                BundleCheckService.ByteBudget = bytes;
            }
        }

        /// <summary>The raw path's Unity steps, faked: the download writes <see cref="Bytes"/>; the load answers an object or null.</summary>
        sealed class FakeSteps
        {
            internal byte[] Bytes;
            internal bool LoadFails;
            internal Action AfterDownload;
            internal int Downloads, Loads;
            internal bool FileExistedAtLoad;

            internal BundleFileLoader.Steps<object> Steps => new BundleFileLoader.Steps<object>
            {
                Download = (url, path, progress, cancellation) =>
                {
                    Downloads++;
                    File.WriteAllBytes(path, Bytes);
                    progress(1f);
                    AfterDownload?.Invoke();
                    return Task.CompletedTask;
                },
                Prepare = (path, progress) => Task.CompletedTask,
                Load = path =>
                {
                    Loads++;
                    FileExistedAtLoad = File.Exists(path);
                    return Task.FromResult(LoadFails ? null : new object());
                },
            };
        }

        /// <summary>Starts a load on this thread, as a loader does; wait for it with <see cref="Await"/>.</summary>
        static Task<BundleFileLoader.Loaded<object>> StartLoad(FakeSteps fake, BundleCheckOptions options = null, bool kit = false, CancellationToken cancellation = default) =>
            BundleFileLoader.LoadAsync(RawUrl, CacheKey, 0, options ?? BundleCheckOptions.World(), kit, null, cancellation, fake.Steps);

        /// <summary>An ended load's result; its file is then released, as when its bundle is unloaded.</summary>
        static BundleFileLoader.Loaded<object> Loaded(Task<BundleFileLoader.Loaded<object>> task)
        {
            var fault = Fault(task);
            Assert.IsNull(fault, "the load failed: " + fault);
            var loaded = task.Result; // ended: does not block
            BundleFileCache.ReleaseInUse(loaded.Entry.FileName);
            return loaded;
        }

        static bool HasKeptVerdict(BundleCheckOptions options = null)
        {
            var entry = BundleFileCache.Find(CacheKey, RawUrl);
            return entry != null && BundleFileCache.TryRecall(entry, BundleCheckPolicy.Versions(options ?? BundleCheckOptions.World(), BundleCheckService.Environment), out _, out _);
        }

        // ------------------------------------------------------------------ raw bundles: the load end to end

        [Test]
        public void TheRawCacheKeyIsASha256OfTheUrlAndItsValidators()
        {
            var key = BundleFileCache.KeyFor(RawUrl, "Tue, 03 Nov 2026 10:00:00 GMT", "\"abc\"");
            Assert.AreEqual(64, key.Length);
            Assert.IsTrue(key.All(c => c >= '0' && c <= '9' || c >= 'a' && c <= 'f'));
            Assert.AreNotEqual(key, BundleFileCache.KeyFor(RawUrl, "Tue, 03 Nov 2026 10:00:00 GMT", "\"abd\""), "a republish");
            Assert.AreNotEqual(key, BundleFileCache.KeyFor(RawUrl + "x", "Tue, 03 Nov 2026 10:00:00 GMT", "\"abc\""));
            Assert.AreEqual(key, BundleFileCache.KeyFor(RawUrl, "Tue, 03 Nov 2026 10:00:00 GMT", "\"abc\""));
        }

#if UNITY_EDITOR
        [UnityTest] public IEnumerator ARawBundleIsDownloadedOnceAndItsVerdictKeptPerKind() => RawBundleDownloadedOnce();
#else
        [Test] public void ARawBundleIsDownloadedOnceAndItsVerdictKeptPerKind() => Drain(RawBundleDownloadedOnce());
#endif

        IEnumerator RawBundleDownloadedOnce()
        {
            using (new TempCache())
            using (new Settings(BundleCheckService.EnforcementMode.Enforce))
            {
                var fake = new FakeSteps { Bytes = ButtonWorld(new Call()) };
                var task = StartLoad(fake);
                yield return Await(task);
                var first = Loaded(task);
                Assert.AreEqual(BundleCheckService.CheckState.Judged, first.Run.State);
                Assert.IsFalse(first.Run.Verdict.FromCache);
                task = StartLoad(fake);
                yield return Await(task);
                var second = Loaded(task);
                Assert.AreEqual(1, fake.Downloads, "the entry answers the second load");
                Assert.AreEqual(2, fake.Loads);
                Assert.IsTrue(second.Run.Verdict.FromCache, "the verdict kept with the entry answers");
                Assert.AreEqual(first.Entry.BundlePath, second.Entry.BundlePath);
                // The same file as a kit: other rules, judged again (a kit holds no scene), and refused under Enforce.
                task = StartLoad(fake, BundleCheckOptions.Kit(), true);
                yield return Await(task);
                StringAssert.StartsWith("A kit in this world was refused by the content check: TooManyScenes", Refusal<BundleLoadRefusedException>(task).Message);
                Assert.AreEqual(2, fake.Loads, "a refused bundle is not loaded");
                Assert.AreEqual(0, BundleFileCache.LockedKeys, "the lock table holds nothing once the loads end");
            }
        }

#if UNITY_EDITOR
        [UnityTest] public IEnumerator ARefusalIsKeptAndRecalledWithTheSameMessage() => RefusalKept();
#else
        [Test] public void ARefusalIsKeptAndRecalledWithTheSameMessage() => Drain(RefusalKept());
#endif

        IEnumerator RefusalKept()
        {
            using (new TempCache())
            using (new Settings(BundleCheckService.EnforcementMode.Enforce))
            {
                var fake = new FakeSteps { Bytes = RefusedWorld() };
                var task = StartLoad(fake);
                yield return Await(task);
                var first = Refusal<BundleLoadRefusedException>(task);
                Assert.IsTrue(HasKeptVerdict(), "a judged refusal is kept");
                task = StartLoad(fake);
                yield return Await(task);
                var second = Refusal<BundleLoadRefusedException>(task);
                Assert.AreEqual(1, fake.Downloads);
                Assert.AreEqual(0, fake.Loads);
                StringAssert.StartsWith("This world was refused by the content check: ", first.Message);
                Assert.AreEqual(first.Message, second.Message);
                StringAssert.DoesNotContain(nameof(BundleRule.RefusedBefore), second.Message);
            }
        }

#if UNITY_EDITOR
        [UnityTest] public IEnumerator AStructuralRefusalStopsTheLoadEvenUnderReportOnly() => StructuralReportOnly();
#else
        [Test] public void AStructuralRefusalStopsTheLoadEvenUnderReportOnly() => Drain(StructuralReportOnly());
#endif

        IEnumerator StructuralReportOnly()
        {
            using (new TempCache())
            using (new Settings(BundleCheckService.EnforcementMode.ReportOnly))
            {
                var fake = new FakeSteps { Bytes = DamagedWorld() };
                var task = StartLoad(fake);
                yield return Await(task);
                StringAssert.StartsWith("This world was refused by the content check: Malformed", Refusal<BundleLoadRefusedException>(task).Message);
                Assert.AreEqual(0, fake.Loads, "the native loader never sees it");
                Assert.IsNull(BundleFileCache.Find(CacheKey, RawUrl), "handled as under Enforce: perhaps damaged, downloaded again next time");
            }
        }

#if UNITY_EDITOR
        [UnityTest] public IEnumerator APolicyRefusalStillLoadsUnderReportOnly() => PolicyReportOnly();
#else
        [Test] public void APolicyRefusalStillLoadsUnderReportOnly() => Drain(PolicyReportOnly());
#endif

        IEnumerator PolicyReportOnly()
        {
            using (new TempCache())
            using (new Settings(BundleCheckService.EnforcementMode.ReportOnly))
            {
                var fake = new FakeSteps { Bytes = RefusedWorld() };
                var task = StartLoad(fake);
                yield return Await(task);
                var first = Loaded(task);
                Assert.IsFalse(first.Run.Verdict.Passed);
                Assert.IsFalse(first.Run.Verdict.RefusedStructurally, "a policy refusal");
                Assert.AreEqual(1, fake.Loads, "ReportOnly: logged, and the load goes on");
                Assert.IsTrue(HasKeptVerdict(), "the refusal is kept");
                task = StartLoad(fake);
                yield return Await(task);
                Assert.IsTrue(Loaded(task).Run.Verdict.FromCache);
                Assert.AreEqual(2, fake.Loads);
            }
        }

#if UNITY_EDITOR
        [UnityTest] public IEnumerator AKeptSizeLimitRefusalStillStopsTheLoadUnderReportOnly() => KeptSizeLimit();
#else
        [Test] public void AKeptSizeLimitRefusalStillStopsTheLoadUnderReportOnly() => Drain(KeptSizeLimit());
#endif

        IEnumerator KeptSizeLimit()
        {
            using (var cache = new TempCache())
            using (new Settings(BundleCheckService.EnforcementMode.ReportOnly))
            {
                // A size-limit refusal kept from an earlier check (the archive's TooLarge, a structural rule): recalled, it
                // still refuses, while the budget's own TooLarge (AnOverBudgetCheck... below) never does under ReportOnly.
                var entry = cache.Add(CacheKey, ButtonWorld(new Call()));
                var versions = BundleCheckPolicy.Versions(BundleCheckOptions.World(), BundleCheckService.Environment);
                BundleFileCache.Remember(entry, versions, new BundleVerdict(BundleVerdictKind.Refuse,
                    new[] { new BundleFinding(BundleRule.TooLarge, "", "LZMA data to read of 600 MiB, over this platform's 512 MiB") }, null, null, versions));
                var fake = new FakeSteps { Bytes = ButtonWorld(new Call()) };
                var task = StartLoad(fake);
                yield return Await(task);
                StringAssert.StartsWith("This world was refused by the content check: TooLarge: LZMA data", Refusal<BundleLoadRefusedException>(task).Message);
                Assert.AreEqual(0, fake.Downloads);
                Assert.AreEqual(0, fake.Loads);
                Assert.IsNotNull(BundleFileCache.Find(CacheKey, RawUrl), "not malformed: kept, with its verdict");
            }
        }

#if UNITY_EDITOR
        [UnityTest] public IEnumerator ABeeIsRefusedForStructureButNotForPolicyUnderReportOnly() => BeeReportOnly();
#else
        [Test] public void ABeeIsRefusedForStructureButNotForPolicyUnderReportOnly() => Drain(BeeReportOnly());
#endif

        IEnumerator BeeReportOnly()
        {
            using (new Settings(BundleCheckService.EnforcementMode.ReportOnly))
            {
                // Basis' hook as Basis calls it, for decrypted bytes from no cache entry.
                var damaged = BundlePreloadHooks.CheckDecryptedAsync(DamagedWorld(), new BasisPreLoadCheck.Request { Kind = BasisPreLoadCheck.Kind.World, Url = "https://cdn.example/world.bee" });
                yield return Await(damaged);
                StringAssert.StartsWith("This world was refused by the content check: Malformed", damaged.Result);
                var policy = BundlePreloadHooks.CheckDecryptedAsync(RefusedWorld(), new BasisPreLoadCheck.Request { Kind = BasisPreLoadCheck.Kind.World, Url = "https://cdn.example/world.bee" });
                yield return Await(policy);
                Assert.IsNull(policy.Result, "a policy refusal loads under ReportOnly");
                BundleCheckService.Mode = BundleCheckService.EnforcementMode.Off;
                var off = BundlePreloadHooks.CheckDecryptedAsync(DamagedWorld(), new BasisPreLoadCheck.Request { Kind = BasisPreLoadCheck.Kind.World, Url = "https://cdn.example/world.bee" });
                yield return Await(off);
                Assert.IsNull(off.Result, "Off checks nothing");
            }
        }

#if UNITY_EDITOR
        [UnityTest] public IEnumerator AMalformedBundleIsRemovedOnlyWhenEnforceRefusesIt() => MalformedEnforce();
#else
        [Test] public void AMalformedBundleIsRemovedOnlyWhenEnforceRefusesIt() => Drain(MalformedEnforce());
#endif

        IEnumerator MalformedEnforce()
        {
            using (new TempCache())
            using (new Settings(BundleCheckService.EnforcementMode.Enforce))
            {
                var fake = new FakeSteps { Bytes = DamagedWorld() };
                var task = StartLoad(fake);
                yield return Await(task);
                StringAssert.Contains("Malformed", Refusal<BundleLoadRefusedException>(task).Message);
                Assert.AreEqual(0, fake.Loads);
                Assert.IsNull(BundleFileCache.Find(CacheKey, RawUrl), "perhaps damaged: downloaded again next time");
                task = StartLoad(fake);
                yield return Await(task);
                Refusal<BundleLoadRefusedException>(task);
                Assert.AreEqual(2, fake.Downloads);
            }
        }

#if UNITY_EDITOR
        [UnityTest] public IEnumerator AnEntryUnityCannotLoadIsRemoved() => UnloadableRemoved();
#else
        [Test] public void AnEntryUnityCannotLoadIsRemoved() => Drain(UnloadableRemoved());
#endif

        IEnumerator UnloadableRemoved()
        {
            using (new TempCache())
            using (new Settings(BundleCheckService.EnforcementMode.ReportOnly))
            {
                var fake = new FakeSteps { Bytes = ButtonWorld(new Call()), LoadFails = true };
                var inUseBefore = BundleFileCache.InUseCount;
                var task = StartLoad(fake);
                yield return Await(task);
                StringAssert.Contains("Unable to load asset bundle", Fault(task)?.Message);
                Assert.IsNull(BundleFileCache.Find(CacheKey, RawUrl));
                Assert.AreEqual(inUseBefore, BundleFileCache.InUseCount, "a file Unity did not load is not in use");
            }
        }

        [Test]
        public void JudgingAnEntryNeverChangesTheCache()
        {
            using (var cache = new TempCache())
            {
                var entry = cache.Add(CacheKey, DamagedWorld());
                var verdict = BundlePreloadHooks.JudgeCacheEntry(entry, BundleCheckOptions.World());
                AssertRefused(verdict, BundleRule.Malformed);
                Assert.IsTrue(File.Exists(entry.BundlePath));
                Assert.IsNotNull(BundleFileCache.Find(CacheKey, RawUrl));
                Assert.IsFalse(HasKeptVerdict());
            }
        }

#if UNITY_EDITOR
        [UnityTest] public IEnumerator AnOverBudgetCheckRefusesOnlyUnderEnforceAndChangesNothing() => OverBudgetLoads();
#else
        [Test] public void AnOverBudgetCheckRefusesOnlyUnderEnforceAndChangesNothing() => Drain(OverBudgetLoads());
#endif

        IEnumerator OverBudgetLoads()
        {
            using (new TempCache())
            using (new Settings(BundleCheckService.EnforcementMode.ReportOnly))
            {
                BundleCheckService.ByteBudget = 64;
                var fake = new FakeSteps { Bytes = ButtonWorld(new Call()) };
                var task = StartLoad(fake);
                yield return Await(task);
                // The budget's refusal is a TooLarge too, but the run ends OverBudget: no structural refusal, so ReportOnly loads.
                Assert.AreEqual(BundleCheckService.CheckState.OverBudget, Loaded(task).Run.State);
                Assert.AreEqual(1, fake.Loads);
                BundleCheckService.Mode = BundleCheckService.EnforcementMode.Enforce;
                task = StartLoad(fake);
                yield return Await(task);
                StringAssert.Contains("budget", Refusal<BundleLoadRefusedException>(task).Message);
                Assert.IsNotNull(BundleFileCache.Find(CacheKey, RawUrl), "not deleted");
                Assert.IsFalse(HasKeptVerdict(), "not kept");
            }
        }

#if UNITY_EDITOR
        [UnityTest] public IEnumerator AbandonedChecksAreCappedAndAnUncheckedLoadChangesNothing() => AbandonedCapped();
#else
        [Test] public void AbandonedChecksAreCappedAndAnUncheckedLoadChangesNothing() => Drain(AbandonedCapped());
#endif

        IEnumerator AbandonedCapped()
        {
            using (new TempCache())
            using (new Settings(BundleCheckService.EnforcementMode.ReportOnly))
            {
                BundleCheckService.WallClockBudget = TimeSpan.FromMilliseconds(20);
                BundleCheckService.OverrunGrace = TimeSpan.FromMilliseconds(20);
                var gate = new ManualResetEventSlim();
                try
                {
                    // Checks that never charge their budget (and so never stop): the watchdog abandons them.
                    for (var i = 0; i < BundleCheckService.MaxAbandoned; i++)
                    {
                        var stuck = BundleCheckService.RunCheckAsync(() =>
                        {
                            gate.Wait(10000); // on the check's own thread; bounded
                            return null;
                        }, BundleCheckOptions.World());
                        yield return Await(stuck);
                        Assert.AreEqual(BundleCheckService.CheckState.Unknown, stuck.Result.State);
                    }
                    Assert.AreEqual(BundleCheckService.MaxAbandoned, BundleCheckService.AbandonedChecks);

                    var fake = new FakeSteps { Bytes = ButtonWorld(new Call()) };
                    var task = StartLoad(fake);
                    yield return Await(task);
                    var loaded = Loaded(task);
                    Assert.AreEqual(BundleCheckService.CheckState.Unknown, loaded.Run.State, "not started while the cap is reached");
                    StringAssert.Contains("could not be checked", loaded.Run.Verdict.FirstReason);
                    Assert.AreEqual(1, fake.Loads, "ReportOnly loads it");
                    BundleCheckService.Mode = BundleCheckService.EnforcementMode.Enforce;
                    task = StartLoad(fake);
                    yield return Await(task);
                    Refusal<BundleLoadRefusedException>(task);
                    Assert.IsNotNull(BundleFileCache.Find(CacheKey, RawUrl), "not deleted");
                    Assert.IsFalse(HasKeptVerdict(), "not kept");
                }
                finally
                {
                    gate.Set();
                }
                yield return Until(() => BundleCheckService.AbandonedChecks == 0, "the abandoned checks ending");
            }
        }

#if UNITY_EDITOR
        [UnityTest] public IEnumerator ACancelledLoadChangesNothing() => CancelledLoad();
#else
        [Test] public void ACancelledLoadChangesNothing() => Drain(CancelledLoad());
#endif

        IEnumerator CancelledLoad()
        {
            using (new TempCache())
            using (new Settings(BundleCheckService.EnforcementMode.Enforce))
            using (var cancel = new CancellationTokenSource())
            {
                var fake = new FakeSteps { Bytes = RefusedWorld(), AfterDownload = cancel.Cancel };
                var task = StartLoad(fake, cancellation: cancel.Token);
                yield return Await(task);
                Assert.IsInstanceOf<OperationCanceledException>(Fault(task));
                Assert.IsNotNull(BundleFileCache.Find(CacheKey, RawUrl), "the download stays");
                Assert.IsFalse(HasKeptVerdict(), "no verdict");
                Assert.AreEqual(0, fake.Loads);
            }
        }

#if UNITY_EDITOR
        [UnityTest] public IEnumerator TheGuardsTestSwitchSkipsTheCheckAndKeepsNothing() => GuardSwitchSkips();
#else
        [Test] public void TheGuardsTestSwitchSkipsTheCheckAndKeepsNothing() => Drain(GuardSwitchSkips());
#endif

        IEnumerator GuardSwitchSkips()
        {
            using (new TempCache())
            using (new Settings(BundleCheckService.EnforcementMode.Enforce))
            {
                var fake = new FakeSteps { Bytes = RefusedWorld() };
                var was = BSUnityEventGuard.DisabledForTests;
                Task<BundleFileLoader.Loaded<object>> task;
                try
                {
                    BSUnityEventGuard.DisabledForTests = true;
                    task = StartLoad(fake);
                    yield return Await(task);
                }
                finally
                {
                    BSUnityEventGuard.DisabledForTests = was;
                }
                Assert.IsNull(Loaded(task).Run, "not checked");
                Assert.AreEqual(1, fake.Loads);
                Assert.IsFalse(HasKeptVerdict(), "the skipped load kept nothing");
                task = StartLoad(fake);
                yield return Await(task);
                Refusal<BundleLoadRefusedException>(task);
            }
        }

        // ------------------------------------------------------------------ raw bundles: the file cache

        [Test]
        public void ARewrittenEntryGetsAFileOfItsOwn()
        {
            using (var cache = new TempCache())
            {
                var entry = cache.Add(CacheKey, ButtonWorld(new Call()));
                var again = cache.Add(CacheKey, RefusedWorld());
                Assert.AreNotEqual(entry.Nonce, again.Nonce);
                Assert.AreNotEqual(entry.BundlePath, again.BundlePath);
                Assert.IsTrue(File.Exists(entry.BundlePath), "left for the trim, which knows whether it is loaded");
                File.AppendAllText(again.BundlePath, "x");
                Assert.IsNull(BundleFileCache.Find(CacheKey, RawUrl), "bytes changed behind the record");
            }
        }

        [Test]
        public void TheEntryRecordKeepsNoUrlAndACorruptRecordIsNoEntry()
        {
            using (var cache = new TempCache())
            {
                var entry = cache.Add(CacheKey, ButtonWorld(new Call()));
                var record = File.ReadAllText(entry.EntryPath);
                StringAssert.DoesNotContain("do-not-store", record);
                StringAssert.DoesNotContain("cdn.example", record);
                Assert.IsNull(BundleFileCache.Find(CacheKey, "https://cdn.example/kits/other.banter"), "another URL never matches");
                entry = cache.Add(CacheKey, ButtonWorld(new Call()));
                File.WriteAllText(entry.EntryPath, record.Replace(entry.Nonce, "0000"));
                Assert.IsNull(BundleFileCache.Find(CacheKey, RawUrl), "a record naming another file");
                File.WriteAllText(entry.EntryPath, "url=\nnonce=\nlength=many\n");
                Assert.IsNull(BundleFileCache.Find(CacheKey, RawUrl));
            }
        }

        [Test]
        public void TrimDropsTheLeastRecentlyUsedButNeverAHeldEntry()
        {
            var bundle = ButtonWorld(new Call());
            using (var cache = new TempCache(bundle.Length * 2L + 16))
            {
                const string a = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", b = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", c = "cccccccccccccccccccccccccccccccc",
                    d = "dddddddddddddddddddddddddddddddd";
                cache.Add(a, bundle);
                cache.Add(b, bundle);
                cache.Add(c, bundle);
                BundleFileCache.Touch(BundleFileCache.Find(a, RawUrl)); // least recently used first: b, c, a
                var hold = BundleFileCache.Lock(b);
                Assert.IsTrue(hold.IsCompleted, "a free entry's lock is taken at once"); // so reading Result below never blocks
                using (hold.Result)
                {
                    BundleFileCache.Trim();
                    Assert.IsNotNull(BundleFileCache.Find(b, RawUrl), "held by a load");
                    Assert.IsNull(BundleFileCache.Find(c, RawUrl), "the next least recently used went instead");
                }
                cache.Add(d, bundle);
                BundleFileCache.Trim();
                Assert.IsNull(BundleFileCache.Find(b, RawUrl));
                Assert.IsNotNull(BundleFileCache.Find(a, RawUrl));
                Assert.IsNotNull(BundleFileCache.Find(d, RawUrl));
                Assert.AreEqual(0, BundleFileCache.LockedKeys);
            }
        }

        [Test]
        public void AFileInUseIsNeverDeletedUntilItsBundleIsGone()
        {
            var bundle = ButtonWorld(new Call());
            using (var cache = new TempCache(0))
            {
                var loaded = cache.Add(CacheKey, bundle);
                BundleFileCache.MarkInUse(loaded);
                var replaced = cache.Add(CacheKey, bundle); // a re-download while the first is loaded
                BundleFileCache.Trim();
                Assert.IsTrue(File.Exists(loaded.BundlePath), "in use");
                Assert.IsFalse(File.Exists(replaced.BundlePath), "over a cap of nothing, and not in use");
                BundleFileCache.Remove(loaded);
                Assert.IsTrue(File.Exists(loaded.BundlePath), "Remove keeps a file in use too");
                BundleFileCache.ReleaseInUse(loaded.FileName); // its bundle was unloaded
                BundleFileCache.Trim();
                Assert.IsFalse(File.Exists(loaded.BundlePath), "then it goes");
            }
        }

        [Test]
        public void ClearCacheFreesWhatNoBundleUses()
        {
            var bundle = ButtonWorld(new Call());
            using (var cache = new TempCache())
            {
                const string a = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", b = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
                var inUse = cache.Add(a, bundle);
                BundleFileCache.MarkInUse(inUse);
                var unused = cache.Add(b, bundle);
                try
                {
                    var before = BundleCaches.SizeBytes();
                    Assert.GreaterOrEqual(before, 2L * bundle.Length, "both downloads (and their records)");
                    var freed = BundleCaches.ClearUnused();
                    Assert.GreaterOrEqual(freed, bundle.Length);
                    Assert.IsTrue(File.Exists(inUse.BundlePath), "a file in use stays");
                    Assert.IsFalse(File.Exists(unused.BundlePath));
                    Assert.AreEqual(before - freed, BundleCaches.SizeBytes());
                    Assert.IsNotNull(BundleFileCache.Find(a, RawUrl), "and so does its entry");
                }
                finally
                {
                    BundleFileCache.ReleaseInUse(inUse.FileName);
                }
            }
        }

        [Test]
        public void MakeRoomTrimsWhenTheDiskIsShort()
        {
            var bundle = ButtonWorld(new Call());
            using (var cache = new TempCache())
            {
                if (BundleFileCache.FreeBytes() < 0) Assert.Inconclusive("this platform does not report free space");
                const string a = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", b = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
                var inUse = cache.Add(a, bundle);
                BundleFileCache.MarkInUse(inUse);
                var unused = cache.Add(b, bundle);
                try
                {
                    BundleFileCache.MakeRoom(1L << 60); // more than any disk has
                    Assert.IsTrue(File.Exists(inUse.BundlePath), "never a file in use");
                    Assert.IsFalse(File.Exists(unused.BundlePath));
                }
                finally
                {
                    BundleFileCache.ReleaseInUse(inUse.FileName);
                }
            }
        }

        [Test]
        public void TrimRemovesLeftovers()
        {
            using (var cache = new TempCache())
            {
                var orphan = Path.Combine(cache.Root, "dddddddddddddddddddddddddddddddd.0123.bundle");
                File.WriteAllBytes(orphan, new byte[8]);
                var stale = Path.Combine(cache.Root, "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee.part");
                File.WriteAllBytes(stale, new byte[8]);
                File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddHours(-2));
                var fresh = Path.Combine(cache.Root, "ffffffffffffffffffffffffffffffff.part");
                File.WriteAllBytes(fresh, new byte[8]);
                BundleFileCache.Trim();
                Assert.IsFalse(File.Exists(orphan), "a bundle file no record names");
                Assert.IsFalse(File.Exists(stale), "a partial download over an hour old");
                Assert.IsTrue(File.Exists(fresh), "a partial download in progress");
            }
        }

        [Test]
        public void CheckingFromAFileMatchesCheckingTheBytes()
        {
            var path = Path.Combine(Path.GetTempPath(), "bundle-file-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                foreach (var bundle in new[] { ButtonWorld(new Call()), RefusedWorld() })
                {
                    File.WriteAllBytes(path, bundle);
                    BundleVerdict fromFile;
                    using (var source = new FileBundleSource(path))
                    {
                        fromFile = BundleChecker.Check(source, BundleCheckOptions.World(), "test");
                    }
                    var fromBytes = Check(bundle);
                    Assert.AreEqual(fromBytes.Kind, fromFile.Kind);
                    CollectionAssert.AreEqual(fromBytes.Reasons.Select(r => r.ToString()), fromFile.Reasons.Select(r => r.ToString()));
                    Assert.AreEqual(fromBytes.Stats.PersistentCalls, fromFile.Stats.PersistentCalls);
                    Assert.AreEqual(fromBytes.Stats.Objects, fromFile.Stats.Objects);
                }
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        // ------------------------------------------------------------------ encrypted bundles: the verdict beside the .bee

        /// <summary>
        /// Judges as Basis' hook does, with the file stamped now (as Basis stamps it at its read), then keeps the verdict as
        /// the hook does once a check has ended. The judging runs here, synchronously: what is under test is what is kept
        /// beside the file; the check thread itself is covered by the RunCheckAsync tests below.
        /// </summary>
        static BundleVerdict JudgeBee(byte[] bytes, BundleCheckOptions options, string url, string tag, string bee, bool downloaded, long ticksOffset = 0)
        {
            var info = new FileInfo(bee);
            var length = info.Length;
            var ticks = info.LastWriteTimeUtc.Ticks + ticksOffset;
            var verdict = BundlePreloadHooks.JudgeDecrypted(bytes, options, url, tag, bee, downloaded, length, ticks, out var record, out var contentKey);
            var run = new BundleCheckService.CheckRun(verdict, BundleCheckService.CheckState.Judged, TimeSpan.Zero, 0);
            BundlePreloadHooks.AfterDecryptedCheck(run, record, contentKey, bee, length, ticks);
            return verdict;
        }

        [Test]
        public void ABeeVerdictIsBoundToItsCacheFile()
        {
            using (var cache = new TempCache())
            {
                var bee = Path.Combine(cache.Root, "world.bee"); // stands for Basis' cached encrypted file
                File.WriteAllBytes(bee, new byte[100]);
                var sidecar = bee + BundlePreloadHooks.VerdictSuffix;
                const string url = "https://cdn.example/world.bee";
                var bytes = ButtonWorld(new Call());
                var options = BundleCheckOptions.World();

                var downloaded = JudgeBee(bytes, options, url, "tag-1", bee, true);
                AssertPassed(downloaded);
                Assert.IsFalse(downloaded.FromCache);
                Assert.IsTrue(File.Exists(sidecar));
                var text = File.ReadAllText(sidecar);
                StringAssert.DoesNotContain("cdn.example", text);
                StringAssert.DoesNotContain("tag-1", text);

                Assert.IsTrue(JudgeBee(bytes, options, url, "tag-1", bee, false).FromCache, "the same cached file");
                Assert.IsFalse(JudgeBee(bytes, options, url, "tag-1", bee, true).FromCache, "just downloaded: never recalled");
                Assert.IsFalse(JudgeBee(bytes, options, url, "tag-2", bee, false).FromCache, "another version tag");
                Assert.IsFalse(JudgeBee(bytes, options, "https://cdn.example/other.bee", "tag-2", bee, false).FromCache, "another URL");
                Assert.IsFalse(JudgeBee(bytes, BundleCheckOptions.Kit(), url, "tag-2", bee, false).FromCache, "another kind");

                JudgeBee(bytes, options, url, "tag-2", bee, false);
                File.SetLastWriteTimeUtc(bee, DateTime.UtcNow.AddMinutes(-5));
                Assert.IsFalse(JudgeBee(bytes, options, url, "tag-2", bee, false).FromCache, "the file was rewritten");

                var swapped = JudgeBee(RefusedWorld(), options, url, "tag-2", bee, false);
                Assert.IsFalse(swapped.FromCache, "other decrypted bytes (length)");
                Assert.IsFalse(swapped.Passed);

                File.WriteAllText(sidecar, "{\"format\":1,\"url\":\"x\"}");
                Assert.IsFalse(JudgeBee(bytes, options, url, "tag-2", bee, false).FromCache, "a corrupt record");
                Assert.IsTrue(JudgeBee(bytes, options, url, "tag-2", bee, false).FromCache, "and rewritten");
            }
        }

        [Test]
        public void ABeeThatChangedAfterItsReadOrIsMalformedKeepsNothing()
        {
            using (var cache = new TempCache())
            {
                var bee = Path.Combine(cache.Root, "world.bee");
                File.WriteAllBytes(bee, new byte[100]);
                var sidecar = bee + BundlePreloadHooks.VerdictSuffix;
                var bytes = ButtonWorld(new Call());
                // Stamped at the read; the file was replaced before the check looked at it.
                var verdict = JudgeBee(bytes, BundleCheckOptions.World(), "https://cdn.example/world.bee", "t", bee, false, -1);
                AssertPassed(verdict);
                Assert.IsFalse(File.Exists(sidecar));
                AssertRefused(JudgeBee(DamagedWorld(), BundleCheckOptions.World(), "https://cdn.example/world.bee", "t", bee, true), BundleRule.Malformed);
                Assert.IsFalse(File.Exists(sidecar), "a malformed verdict is never kept");
            }
        }

        [Test]
        public void ABeeWithoutACacheFileKeepsNoRecord()
        {
            using (var cache = new TempCache())
            {
                var verdict = BundlePreloadHooks.JudgeDecrypted(ButtonWorld(new Call()), BundleCheckOptions.World(), "C:/worlds/local.bee", "", null, false, 0, 0,
                    out var record, out _);
                AssertPassed(verdict);
                Assert.IsNull(record);
                Assert.IsEmpty(Directory.GetFiles(cache.Root));
            }
        }

        // ------------------------------------------------------------------ the check's thread and budget

        /// <summary>Bundle bytes whose first read waits for <see cref="Gate"/> (a check in progress, held).</summary>
        sealed class HeldSource : BundleSource
        {
            readonly ByteArrayBundleSource inner;
            internal readonly ManualResetEventSlim Reading = new ManualResetEventSlim(), Gate = new ManualResetEventSlim();

            internal HeldSource(byte[] bytes)
            {
                inner = new ByteArrayBundleSource(bytes);
            }

            internal override long Length => inner.Length;

            internal override void Read(long position, byte[] buffer, int offset, int count)
            {
                Reading.Set();
                Gate.Wait(10000); // on the check's own thread; bounded
                inner.Read(position, buffer, offset, count);
            }
        }

#if UNITY_EDITOR
        [UnityTest] public IEnumerator AChecksRunsOnANormalPriorityThreadOfItsOwnUnderABudget() => CheckThread();
#else
        [Test] public void AChecksRunsOnANormalPriorityThreadOfItsOwnUnderABudget() => Drain(CheckThread());
#endif

        IEnumerator CheckThread()
        {
            var bundle = ButtonWorld(new Call());
            Thread worker = null;
            var priority = ThreadPriority.Lowest;
            BundleCheckBudget budget = null;
            var task = BundleCheckService.RunCheckAsync(() =>
            {
                worker = Thread.CurrentThread;
                priority = worker.Priority;
                budget = BundleCheckBudget.Current;
                return BundleCheckService.CheckFresh(new ByteArrayBundleSource(bundle), BundleCheckOptions.World());
            }, BundleCheckOptions.World());
            yield return Await(task);
            var run = task.Result;
            AssertPassed(run.Verdict);
            Assert.AreEqual(BundleCheckService.CheckState.Judged, run.State);
            Assert.IsFalse(worker.IsThreadPoolThread);
            Assert.AreEqual("BundleCheck", worker.Name);
            Assert.AreEqual(ThreadPriority.Normal, priority, "normal priority: a busy load must not starve it past its budget");
            Assert.Greater(run.Bytes, 0, "the archive charged what it read");
            Assert.IsNotNull(budget);
            Assert.IsNull(BundleCheckBudget.Current, "no budget outside the check's thread");
        }

#if UNITY_EDITOR
        [UnityTest] public IEnumerator ACheckOverItsByteBudgetEndsOverBudget() => CheckOverBudget();
#else
        [Test] public void ACheckOverItsByteBudgetEndsOverBudget() => Drain(CheckOverBudget());
#endif

        IEnumerator CheckOverBudget()
        {
            using (new Settings())
            {
                BundleCheckService.ByteBudget = 64;
                var bundle = ButtonWorld(new Call());
                var task = BundleCheckService.RunCheckAsync(() => BundleCheckService.CheckFresh(new ByteArrayBundleSource(bundle), BundleCheckOptions.World()),
                    BundleCheckOptions.World());
                yield return Await(task);
                Assert.AreEqual(BundleCheckService.CheckState.OverBudget, task.Result.State);
                AssertRefused(task.Result.Verdict, BundleRule.TooLarge);
                Assert.IsFalse(task.Result.Keepable);
            }
        }

#if UNITY_EDITOR
        [UnityTest] public IEnumerator CancellingTheRealCheckCancelsItAndKeepsNothing() => CancelRealCheck();
#else
        [Test] public void CancellingTheRealCheckCancelsItAndKeepsNothing() => Drain(CancelRealCheck());
#endif

        IEnumerator CancelRealCheck()
        {
            using (var cancel = new CancellationTokenSource())
            {
                var source = new HeldSource(ButtonWorld(new Call()));
                var task = BundleCheckService.RunCheckAsync(() => BundleCheckService.CheckFresh(source, BundleCheckOptions.World()), BundleCheckOptions.World(), cancel.Token);
                yield return Until(() => source.Reading.IsSet, "the check reading");
                cancel.Cancel();
                source.Gate.Set(); // the check reads on, and its next charge sees the cancel
                yield return Await(task);
                Assert.IsTrue(task.IsCanceled, "cancelled, not a verdict");
                yield return Until(() => BundleCheckService.AbandonedChecks == 0, "the cancelled check ending");
            }
        }

        // ------------------------------------------------------------------ Basis' seam: reuse records and the hook

        [Test]
        public void ABundleIsReusedOnlyForTheSameSourceAndKind()
        {
            const int id = -424242; // a record without a bundle object (never pruned)
            var world = new BasisPreLoadCheck.Request { Kind = BasisPreLoadCheck.Kind.World, Url = "https://cdn.example/a.bee", CacheEntryFile = "C:/cache/a.bee" };
            BasisPreLoadCheck.MarkLoaded(id, world);
            Assert.IsTrue(BasisPreLoadCheck.MayReuse(id, new BasisPreLoadCheck.Request { Kind = BasisPreLoadCheck.Kind.World, Url = world.Url, CacheEntryFile = world.CacheEntryFile }));
            Assert.IsTrue(BasisPreLoadCheck.MayReuse(id, new BasisPreLoadCheck.Request { Kind = BasisPreLoadCheck.Kind.Unchecked, Url = world.Url, CacheEntryFile = world.CacheEntryFile }),
                "an unchecked load of the same source: the same bytes");
            Assert.IsFalse(BasisPreLoadCheck.MayReuse(id, new BasisPreLoadCheck.Request { Kind = BasisPreLoadCheck.Kind.Kit, Url = world.Url, CacheEntryFile = world.CacheEntryFile }),
                "checked as another kind");
            Assert.IsFalse(BasisPreLoadCheck.MayReuse(id, new BasisPreLoadCheck.Request { Kind = BasisPreLoadCheck.Kind.World, Url = "https://cdn.example/b.bee", CacheEntryFile = world.CacheEntryFile }),
                "another URL");
            Assert.IsFalse(BasisPreLoadCheck.MayReuse(id, new BasisPreLoadCheck.Request { Kind = BasisPreLoadCheck.Kind.World, Url = world.Url, CacheEntryFile = "C:/cache/other.bee" }),
                "another cache entry");
            Assert.IsFalse(BasisPreLoadCheck.MayReuse(id - 1, world), "a bundle Basis did not load");

            const int avatar = -424243;
            BasisPreLoadCheck.MarkLoaded(avatar, new BasisPreLoadCheck.Request { Kind = BasisPreLoadCheck.Kind.Unchecked, Url = world.Url, CacheEntryFile = world.CacheEntryFile });
            Assert.IsFalse(BasisPreLoadCheck.MayReuse(avatar, world), "a checked load never reuses an unchecked bundle");

            Assert.IsTrue(BasisPreLoadCheck.MayReuseFor(id, world.Url, BasisPreLoadCheck.Kind.World));
            Assert.IsTrue(BasisPreLoadCheck.MayReuseFor(id, world.Url, BasisPreLoadCheck.Kind.Unchecked));
            Assert.IsFalse(BasisPreLoadCheck.MayReuseFor(id, "https://cdn.example/b.bee", BasisPreLoadCheck.Kind.World));
            Assert.IsFalse(BasisPreLoadCheck.MayReuseFor(avatar, world.Url, BasisPreLoadCheck.Kind.World), "Basis' scene registry: an avatar's bundle never serves a world");
        }

        [Test]
        public void BasisRefusesACheckedLoadWithoutAHook()
        {
            var bytes = ButtonWorld(new Call());
            try
            {
                BasisPreLoadCheck.ResetForTests();
                // Run ends before its first await in all three cases: the tasks are complete when they return (never waited on).
                var refused = BasisPreLoadCheck.Run(bytes, new BasisPreLoadCheck.Request { Kind = BasisPreLoadCheck.Kind.World, Url = "x" });
                StringAssert.Contains("not available", Refusal<BasisBundleRefusedException>(refused).Message);
                var avatarLoad = BasisPreLoadCheck.Run(bytes, new BasisPreLoadCheck.Request { Kind = BasisPreLoadCheck.Kind.Unchecked });
                Assert.IsNull(Fault(avatarLoad), "avatars load unchecked");
                Refusal<ArgumentNullException>(BasisPreLoadCheck.Run(bytes, null));
            }
            finally
            {
                BasisPreLoadCheck.ResetForTests();
                if (BundlePreloadHooks.Active) BasisPreLoadCheck.SetHook(BundlePreloadHooks.CheckDecryptedAsync);
            }
        }

        [Test, Category("UnityRuntime")]
        public void TheFirstHookStays()
        {
            Func<byte[], BasisPreLoadCheck.Request, Task<string>> first = (b, r) => Task.FromResult<string>(null);
            Func<byte[], BasisPreLoadCheck.Request, Task<string>> second = (b, r) => Task.FromResult<string>(null);
            try
            {
                BasisPreLoadCheck.ResetForTests();
                Assert.IsTrue(BasisPreLoadCheck.SetHook(first));
                Assert.IsTrue(BasisPreLoadCheck.SetHook(first), "the same hook again");
#if UNITY_EDITOR
                LogAssert.Expect(UnityEngine.LogType.Error, new System.Text.RegularExpressions.Regex("second content-check hook"));
#endif
                Assert.IsFalse(BasisPreLoadCheck.SetHook(second));
                Assert.AreSame(first, BasisPreLoadCheck.Hook);
            }
            finally
            {
                BasisPreLoadCheck.ResetForTests();
                if (BundlePreloadHooks.Active) BasisPreLoadCheck.SetHook(BundlePreloadHooks.CheckDecryptedAsync);
            }
        }

#if UNITY_EDITOR
        [Test, Category("UnityRuntime")]
        public void LoadersAskForTheKindTheyLoad()
        {
#if GREENFIELD_PROJECT
            Assert.AreEqual(BasisPreLoadCheck.Kind.World, BundlePreloadHooks.EffectiveKind(BasisPreLoadCheck.Kind.World));
            Assert.IsTrue(BundlePreloadHooks.Active, "wired in on first use if start-up has not done it (fail closed)");
            Assert.IsNotNull(BasisPreLoadCheck.Hook);
            Assert.AreEqual(BasisPreLoadCheck.Kind.Kit, BundlePreloadHooks.EffectiveKind(BasisPreLoadCheck.Kind.Kit));
#else
            Assert.AreEqual(BasisPreLoadCheck.Kind.Unchecked, BundlePreloadHooks.EffectiveKind(BasisPreLoadCheck.Kind.World), "a creator project checks nothing");
#endif
            Assert.AreEqual(BasisPreLoadCheck.Kind.Unchecked, BundlePreloadHooks.EffectiveKind(BasisPreLoadCheck.Kind.Unchecked));
        }
#endif

        // ------------------------------------------------------------------ LZMA properties

        [Test]
        public void LzmaDictionarySizesAreClampedToTheOutputAndOddPropertiesRefused()
        {
            byte[] Properties(byte first, uint dictionary) =>
                new[] { first, (byte)dictionary, (byte)(dictionary >> 8), (byte)(dictionary >> 16), (byte)(dictionary >> 24) };
            uint Dictionary(byte[] p) => p[1] | (uint)p[2] << 8 | (uint)p[3] << 16 | (uint)p[4] << 24;

            Assert.AreEqual(4096u, Dictionary(BundleArchive.ClampLzmaProperties(Properties(0x5D, 0xFFFFFFFF), 0, 100, "test")), "a 4 GiB window for 100 bytes");
            Assert.AreEqual(1u << 20, Dictionary(BundleArchive.ClampLzmaProperties(Properties(0x5D, 1u << 30), 0, 1 << 20, "test")));
            Assert.AreEqual(1u << 16, Dictionary(BundleArchive.ClampLzmaProperties(Properties(0x5D, 1u << 16), 0, 1 << 20, "test")), "a small window stays");
            var refused = Assert.Throws<BundleRefusedException>(() => BundleArchive.ClampLzmaProperties(Properties(225, 1u << 16), 0, 100, "test"));
            Assert.AreEqual(BundleRule.Malformed, refused.Rule);
            Assert.Throws<BundleRefusedException>(() => BundleArchive.ClampLzmaProperties(Properties(4 * 9 + 8, 1u << 16), 0, 100, "test"), "lc 8 + lp 4");
        }

        // ------------------------------------------------------------------ messages, the switch and defaults

        [Test]
        public void WhatStopsALoadUnderEachMode()
        {
            BundleCheckService.CheckRun Run(BundleVerdict verdict, BundleCheckService.CheckState state = BundleCheckService.CheckState.Judged) =>
                new BundleCheckService.CheckRun(verdict, state, TimeSpan.Zero, 0);
            var passed = Run(Check(ButtonWorld(new Call())));
            var policy = Run(Check(RefusedWorld()));
            var structural = Run(Check(DamagedWorld()));
            var budget = new BundleCheckBudget(TimeSpan.FromSeconds(1), 1);
            var overBudget = Run(budget.Refusal("v"), BundleCheckService.CheckState.OverBudget); // the budget's TooLarge
            var unknown = Run(budget.Refusal("v"), BundleCheckService.CheckState.Unknown);
            Assert.IsTrue(structural.Verdict.RefusedStructurally);
            Assert.IsFalse(policy.Verdict.RefusedStructurally);

            const BundleCheckService.EnforcementMode off = BundleCheckService.EnforcementMode.Off,
                reportOnly = BundleCheckService.EnforcementMode.ReportOnly, enforce = BundleCheckService.EnforcementMode.Enforce;
            foreach (var run in new[] { passed, policy, structural, overBudget, unknown }) Assert.IsNull(BundlePreloadHooks.Outcome(run, false, off), "Off");
            Assert.IsNull(BundlePreloadHooks.Outcome(passed, false, reportOnly));
            Assert.IsNull(BundlePreloadHooks.Outcome(policy, false, reportOnly), "a policy refusal loads, logged");
            StringAssert.StartsWith("This world was refused by the content check: Malformed", BundlePreloadHooks.Outcome(structural, false, reportOnly));
            Assert.IsNull(BundlePreloadHooks.Outcome(overBudget, false, reportOnly), "a spent budget is no structural refusal");
            Assert.IsNull(BundlePreloadHooks.Outcome(unknown, false, reportOnly));
            Assert.IsNull(BundlePreloadHooks.Outcome(passed, false, enforce));
            foreach (var run in new[] { policy, structural, overBudget, unknown })
                StringAssert.StartsWith("This world was refused by the content check: ", BundlePreloadHooks.Outcome(run, false, enforce), "Enforce: " + run.State);
        }

        [Test]
        public void AKeptRefusalIsRecalledWithItsStructuralRule()
        {
            var size = BundleCheckService.RecalledFinding("TooLarge: LZMA block of 600 MiB");
            Assert.AreEqual(BundleRule.TooLarge, size.Rule);
            Assert.AreEqual("LZMA block of 600 MiB", size.Detail);
            Assert.AreEqual(BundleRule.RefusedBefore, BundleCheckService.RecalledFinding("CallNoTarget: a call").Rule, "policy");
            Assert.AreEqual(BundleRule.RefusedBefore, BundleCheckService.RecalledFinding("3: numbered").Rule);
            Assert.AreEqual(BundleRule.RefusedBefore, BundleCheckService.RecalledFinding("").Rule);
            Assert.AreEqual(BundleRule.RefusedBefore, BundleCheckService.RecalledFinding(null).Rule);
        }

        [Test]
        public void LogLabelsKeepOnlyTheHostAndFileName()
        {
            Assert.AreEqual("cdn.example/world.banter", BundlePreloadHooks.LogLabel("https://user:secret@cdn.example/a/b/world.banter?token=abc#frag"));
            Assert.AreEqual("local.bee", BundlePreloadHooks.LogLabel("C:\\worlds\\local.bee"));
            Assert.AreEqual("bundle", BundlePreloadHooks.LogLabel(null));
        }

        [Test]
        public void Defaults()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            const long lzma = 512L * 1024 * 1024;
            const BundleCheckService.HashPolicy hashing = BundleCheckService.HashPolicy.Never;
#else
            const long lzma = 1024L * 1024 * 1024;
            const BundleCheckService.HashPolicy hashing = BundleCheckService.HashPolicy.Auto;
#endif
            Assert.AreEqual(lzma, new BundleCheckOptions().MaxLzmaBlockBytes);
            Assert.AreEqual(hashing, BundleCheckService.Hashing);
            Assert.AreEqual(1, BundleCheckOptions.World().MaxScenes);
            Assert.AreEqual(0, BundleCheckOptions.Kit().MaxScenes);
#if !GREENFIELD_CONTENT_CHECK_ENFORCE && !GREENFIELD_CONTENT_CHECK_OFF
            Assert.AreEqual(BundleCheckService.EnforcementMode.ReportOnly, BundleCheckService.Mode, "report-only until the hardening review");
#endif
        }
    }
}
