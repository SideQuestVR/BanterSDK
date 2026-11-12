using System;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace BS.Security
{
    /// <summary>
    /// The pre-load bundle check as the loaders use it: whether refusals are enforced (<see cref="Mode"/>), checks on a
    /// thread of their own under a budget (<see cref="RunCheckAsync"/>), and the content-hash verdict cache for bytes that
    /// come from none of the app's own caches.
    /// </summary>
    /// <remarks>
    /// The check thread only reads. Every cache it informs (the raw-bundle entry, the verdict beside a .bee, the
    /// content-hash cache) is written by the loader after the check has ended, from its <see cref="CheckRun"/>: a verdict
    /// is kept only when the check finished on its own (<see cref="CheckState.Judged"/>) and did not call the bytes
    /// malformed. In players the switches below are fixed at build time; the editor and test builds can change them.
    /// </remarks>
    internal static class BundleCheckService
    {
        internal enum EnforcementMode
        {
            /// <summary>No check at all.</summary>
            Off,
            /// <summary>
            /// Every bundle is checked; a structural refusal (bytes the native loader could crash on) stops the load, any
            /// other refusal is logged and the load goes on, as do checks that ran over budget or did not finish.
            /// </summary>
            ReportOnly,
            /// <summary>A refused bundle is not loaded; its load fails with the refusal.</summary>
            Enforce,
        }

        /// <summary>How one check ended.</summary>
        internal enum CheckState
        {
            /// <summary>The check finished within its budget: its verdict is about the bytes.</summary>
            Judged,
            /// <summary>The check ran over its byte or time budget and stopped itself: refused, never kept.</summary>
            OverBudget,
            /// <summary>
            /// No verdict about the bytes: the check did not return in time (abandoned), was stopped, or could not start
            /// (too many abandoned checks). Enforce refuses; nothing is kept or deleted.
            /// </summary>
            Unknown,
        }

        /// <summary>A finished check: its verdict, how it ended, and what it cost.</summary>
        internal sealed class CheckRun
        {
            internal readonly BundleVerdict Verdict;
            internal readonly CheckState State;
            internal readonly TimeSpan Elapsed;
            internal readonly long Bytes;

            internal CheckRun(BundleVerdict verdict, CheckState state, TimeSpan elapsed, long bytes)
            {
                Verdict = verdict;
                State = state;
                Elapsed = elapsed;
                Bytes = bytes;
            }

            /// <summary>Whether the loader may keep this verdict: judged in full, from the bytes, and not as malformed.</summary>
            internal bool Keepable => State == CheckState.Judged && !Verdict.FromCache && !IsMalformed(Verdict);
        }

        const EnforcementMode BuildMode =
#if GREENFIELD_CONTENT_CHECK_ENFORCE
            EnforcementMode.Enforce;
#elif GREENFIELD_CONTENT_CHECK_OFF
            EnforcementMode.Off;
#else
            EnforcementMode.ReportOnly;
#endif

        /// <summary>
        /// The one switch for what a refusal does: ReportOnly until the checker's hardening round has passed review, then
        /// Enforce (scripting define GREENFIELD_CONTENT_CHECK_ENFORCE; GREENFIELD_CONTENT_CHECK_OFF turns the check off).
        /// Fixed in players; the editor and test builds may change it.
        /// </summary>
#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
        internal static EnforcementMode Mode = BuildMode;
#else
        // A property over the constant, not a readonly field: nothing (reflection included) can change it in a player.
        internal static EnforcementMode Mode => BuildMode;
#endif

        static readonly TimeSpan DefaultWallClock =
#if UNITY_ANDROID && !UNITY_EDITOR
            TimeSpan.FromSeconds(60);
#else
            TimeSpan.FromSeconds(30);
#endif

        /// <summary>How long one check may run before it is refused (a hostile bundle must not hold a load).</summary>
#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
        internal static TimeSpan WallClockBudget = DefaultWallClock;
#else
        internal static readonly TimeSpan WallClockBudget = DefaultWallClock;
#endif

        const long DefaultByteBudget =
#if UNITY_ANDROID && !UNITY_EDITOR
            2L * 1024 * 1024 * 1024;
#else
            4L * 1024 * 1024 * 1024;
#endif

        /// <summary>
        /// Bytes one check may read, decode and walk, summed (<see cref="BundleCheckBudget"/>): legitimate worlds stay far
        /// below it (an LZ4 world: a few MiB; the largest LZMA world read in the survey: ~1.3 GiB with its decode).
        /// </summary>
#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
        internal static long ByteBudget = DefaultByteBudget;
#else
        internal static readonly long ByteBudget = DefaultByteBudget;
#endif

        /// <summary>How long past its wall-clock budget a check that does not return is waited for before it is abandoned.</summary>
#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
        internal static TimeSpan OverrunGrace = TimeSpan.FromSeconds(5);
#else
        internal static readonly TimeSpan OverrunGrace = TimeSpan.FromSeconds(5);
#endif

        /// <summary>
        /// Abandoned check threads still running (a thread cannot be stopped from outside; one that never charges its
        /// budget runs on). While this many are running, a new check is not started: it ends <see cref="CheckState.Unknown"/>.
        /// </summary>
        internal const int MaxAbandoned = 2;

        static int abandoned;

        internal static int AbandonedChecks => Volatile.Read(ref abandoned);

        internal enum HashPolicy
        {
            /// <summary>
            /// Hash and cache only bundles whose check is expensive (one LZMA block: the whole data must be decoded).
            /// For LZ4 bundles the check reads ~1 MiB and takes milliseconds, less than hashing every byte with the
            /// managed SHA-256 an IL2CPP player uses (measured: 558 ms per 128 MiB on desktop, ~4x more on Quest).
            /// </summary>
            Auto,
            /// <summary>Hash every bundle and answer from the cache whenever it can.</summary>
            Always,
            /// <summary>Never hash or cache: check every time.</summary>
            Never,
        }

        static BundleVerdictCache cache;
        static string environment = "";

        const HashPolicy DefaultHashing =
#if UNITY_ANDROID && !UNITY_EDITOR
            HashPolicy.Never;
#else
            HashPolicy.Auto;
#endif

        /// <summary>
        /// When the content-hash verdict cache is used (see <see cref="HashPolicy"/>): only for bytes that are not from one
        /// of the app's own caches (those keep their verdict with the cache entry). Never on Android, where hashing a whole
        /// world with the managed SHA-256 costs more than checking it.
        /// </summary>
#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
        internal static HashPolicy Hashing = DefaultHashing;
#else
        internal static readonly HashPolicy Hashing = DefaultHashing;
#endif

        internal static string Environment => environment;

        /// <summary>
        /// While BSUnityEventGuard.DisabledForTests is set (tests probing raw Unity behaviour with hostile bundles), the
        /// pre-load check stands aside too, so those bundles reach Unity as the probes expect.
        /// </summary>
        internal static bool SkippedForTests => BSUnityEventGuard.DisabledForTests;

        static BundleVerdict Skipped(BundleCheckOptions options) =>
            new BundleVerdict(BundleVerdictKind.Pass, null,
                new[] { new BundleFinding(BundleRule.Unsupported, "", "not checked: BSUnityEventGuard.DisabledForTests is set") }, null,
                BundleCheckPolicy.Versions(options, environment));

        /// <summary>
        /// Checks bytes whose verdict is kept with a cache entry of the app's own (no hashing, no content-hash cache).
        /// Any failure is a Refuse verdict; only cancellation throws. Call it from <see cref="RunCheckAsync"/>.
        /// </summary>
        internal static BundleVerdict CheckFresh(BundleSource source, BundleCheckOptions options)
        {
            options = options ?? BundleCheckOptions.World();
            if (SkippedForTests) return Skipped(options);
            try
            {
                return BundleChecker.Check(source, options, environment);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                return Failed(e, BundleCheckPolicy.Versions(options, environment));
            }
        }

        static BundleVerdict Failed(Exception e, string versions) =>
            new BundleVerdict(BundleVerdictKind.Refuse,
                new[] { new BundleFinding(BundleRule.Malformed, "", "checker failed (" + e.GetType().Name + ")") }, null, null, versions);

        /// <summary>Refused as malformed: perhaps damaged on the way or on disk, or the checker failed. Never kept.</summary>
        internal static bool IsMalformed(BundleVerdict verdict) =>
            !verdict.Passed && verdict.Reasons.Count > 0 && verdict.Reasons[0].Rule == BundleRule.Malformed;

        /// <summary>
        /// The finding a kept refusal is recalled as: its own rule when that rule is structural (so a kept size-limit
        /// refusal still refuses under ReportOnly), else <see cref="BundleRule.RefusedBefore"/> with the kept text.
        /// </summary>
        internal static BundleFinding RecalledFinding(string reason)
        {
            reason = reason ?? "";
            var colon = reason.IndexOf(':');
            if (colon > 0)
            {
                var name = reason.Substring(0, colon);
                if (char.IsLetter(name[0]) && Enum.TryParse(name, false, out BundleRule rule) && Enum.IsDefined(typeof(BundleRule), rule)
                    && BundleRules.IsStructural(rule))
                    return new BundleFinding(rule, "", reason.Substring(colon + 1).TrimStart());
            }
            return new BundleFinding(BundleRule.RefusedBefore, "", reason);
        }

        static BundleVerdict NotChecked(string why, string versions) =>
            new BundleVerdict(BundleVerdictKind.Refuse, new[] { new BundleFinding(BundleRule.TooLarge, "", "it could not be checked: " + why) }, null, null, versions);

        /// <summary><see cref="RunCheckAsync"/>, its verdict only (offline tools and probes).</summary>
        internal static async Task<BundleVerdict> RunAsync(Func<BundleVerdict> work, BundleCheckOptions options, CancellationToken cancellation = default) =>
            (await RunCheckAsync(work, options, cancellation).ConfigureAwait(false)).Verdict;

        /// <summary>
        /// Runs <paramref name="work"/> (a check, which only reads) on a thread of its own, at normal priority, under a fresh
        /// <see cref="BundleCheckBudget"/>: not on the thread pool, which a slow hostile check would hold up. Ends
        /// <see cref="CheckState.Judged"/> with the work's verdict; <see cref="CheckState.OverBudget"/> when the budget ran
        /// out; <see cref="CheckState.Unknown"/> when the work has not returned <see cref="OverrunGrace"/> after its wall-clock
        /// budget (the thread is told to stop and counted as abandoned until it ends) or when <see cref="MaxAbandoned"/>
        /// abandoned checks are still running. Canceled with <paramref name="cancellation"/>.
        /// </summary>
        internal static Task<CheckRun> RunCheckAsync(Func<BundleVerdict> work, BundleCheckOptions options, CancellationToken cancellation = default)
        {
            var versions = BundleCheckPolicy.Versions(options ?? BundleCheckOptions.World(), environment);
            if (AbandonedChecks >= MaxAbandoned)
            {
                BundleLog.Warning("[BundleCheck] " + AbandonedChecks + " abandoned checks are still running: this bundle is not checked now");
                return Task.FromResult(new CheckRun(NotChecked("earlier checks are still running", versions), CheckState.Unknown, TimeSpan.Zero, 0));
            }
            cancellation.ThrowIfCancellationRequested();
            var budget = new BundleCheckBudget(WallClockBudget, ByteBudget, cancellation);
            var done = new TaskCompletionSource<CheckRun>(TaskCreationOptions.RunContinuationsAsynchronously);
            var leftBehind = 0; // 1 once the caller stopped waiting while the thread still ran
            var finished = 0;   // 1 once the thread ended
            void Abandon()
            {
                budget.Stop();
                if (Volatile.Read(ref finished) == 0 && Interlocked.Exchange(ref leftBehind, 1) == 0)
                {
                    Interlocked.Increment(ref abandoned);
                    if (Volatile.Read(ref finished) == 1 && Interlocked.Exchange(ref leftBehind, 2) == 1) Interlocked.Decrement(ref abandoned);
                    else BundleLog.Warning("[BundleCheck] a check was abandoned after " + (int)budget.Elapsed.TotalSeconds + " s; abandoned checks running: " + AbandonedChecks);
                }
            }
            var thread = new Thread(() =>
            {
                BundleCheckBudget.Enter(budget);
                try
                {
                    var verdict = work() ?? Failed(new InvalidOperationException(), versions);
                    if (budget.Exceeded) done.TrySetResult(new CheckRun(budget.Refusal(versions), CheckState.OverBudget, budget.Elapsed, budget.Bytes));
                    else if (budget.Stopped) done.TrySetResult(new CheckRun(budget.Refusal(versions), CheckState.Unknown, budget.Elapsed, budget.Bytes));
                    else done.TrySetResult(new CheckRun(verdict, CheckState.Judged, budget.Elapsed, budget.Bytes));
                }
                catch (OperationCanceledException)
                {
                    if (cancellation.IsCancellationRequested) done.TrySetCanceled();
                    else done.TrySetResult(new CheckRun(budget.Refusal(versions), CheckState.Unknown, budget.Elapsed, budget.Bytes));
                }
                catch (Exception e)
                {
                    done.TrySetResult(budget.Exceeded
                        ? new CheckRun(budget.Refusal(versions), CheckState.OverBudget, budget.Elapsed, budget.Bytes)
                        : new CheckRun(Failed(e, versions), CheckState.Judged, budget.Elapsed, budget.Bytes));
                }
                finally
                {
                    budget.Stop();
                    BundleCheckBudget.Leave();
                    Volatile.Write(ref finished, 1);
                    if (Interlocked.Exchange(ref leftBehind, 2) == 1) Interlocked.Decrement(ref abandoned);
                }
            }, 4 * 1024 * 1024)
            {
                IsBackground = true,
                Name = "BundleCheck",
            };
            thread.Start();
            var watchdog = new CancellationTokenSource();
            _ = Task.Delay(budget.WallClock + OverrunGrace, watchdog.Token).ContinueWith(t =>
            {
                if (t.IsCanceled) return;
                if (done.TrySetResult(new CheckRun(budget.Refusal(versions), CheckState.Unknown, budget.Elapsed, budget.Bytes))) Abandon();
            }, TaskScheduler.Default);
            var registration = cancellation.CanBeCanceled ? cancellation.Register(() =>
            {
                if (done.TrySetCanceled()) Abandon();
            }) : default;
            done.Task.ContinueWith(_ =>
            {
                watchdog.Cancel();
                watchdog.Dispose();
                registration.Dispose();
            }, TaskScheduler.Default);
            return done.Task;
        }

        /// <summary>
        /// Call once on the main thread (it reads nothing itself): the cache file, e.g.
        /// <c>{persistentDataPath}/SideQuest/bundle-verdicts.json</c>, and an environment key that changes with the app
        /// build (e.g. <c>Application.version + "+" + Application.buildGUID</c>), because the checker judges against the
        /// app's own types.
        /// </summary>
        internal static void Configure(string cacheFilePath, string environmentKey)
        {
            cache = string.IsNullOrEmpty(cacheFilePath) ? null : new BundleVerdictCache(cacheFilePath);
            BundleCheckPolicy.Refresh(); // read the guard's lists once, here on the main thread
            environment = BundleNames.Clean(environmentKey ?? "", 80).Replace(' ', '_').Replace('?', '_');
        }

        internal static BundleVerdictCache Cache => cache;

        /// <summary>Keeps a finished check's verdict in the content-hash cache (the loader, after the check; see <see cref="CheckBytes"/>).</summary>
        internal static void RememberContent(string key, CheckRun run)
        {
            if (key != null && run.Keepable) cache?.Put(key, run.Verdict.Passed, run.Verdict.FirstReason);
        }

        /// <summary>
        /// Hash and look up (when <see cref="Hashing"/> says so), else check: for bytes that come from none of the app's own
        /// caches. Writes nothing: <paramref name="cacheKey"/> is what <see cref="RememberContent"/> keeps the verdict
        /// under once the check has ended. Call it from <see cref="RunCheckAsync"/>.
        /// </summary>
        internal static BundleVerdict CheckBytes(byte[] data, int offset, int count, BundleCheckOptions options, out string cacheKey)
        {
            cacheKey = null;
            options = options ?? BundleCheckOptions.World();
            if (SkippedForTests) return Skipped(options);
            var versions = BundleCheckPolicy.Versions(options, environment);
            string key = null;
            var clock = Stopwatch.StartNew();
            if (cache != null && ShouldHash(data, offset, count, options))
            {
                try
                {
                    using (var sha = SHA256.Create())
                    {
                        key = BundleVerdictCache.Key(sha.ComputeHash(data, offset, count), versions);
                    }
                }
                catch (Exception)
                {
                    key = null; // no key, no cache: judge it
                }
            }
            var hashMilliseconds = clock.Elapsed.TotalMilliseconds;
            if (key != null && cache != null && cache.TryGet(key, out var passed, out var reason))
            {
                var stats = new BundleCheckStats { BundleBytes = count, HashMilliseconds = hashMilliseconds };
                var reasons = passed ? null : new[] { RecalledFinding(reason) };
                return new BundleVerdict(passed ? BundleVerdictKind.Pass : BundleVerdictKind.Refuse, reasons, null, stats, versions, true);
            }
            BundleVerdict verdict;
            try
            {
                verdict = BundleChecker.Check(new ByteArrayBundleSource(data, offset, count), options, environment);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                verdict = Failed(e, versions);
            }
            verdict.Stats.HashMilliseconds = hashMilliseconds;
            cacheKey = key;
            return verdict;
        }

        /// <summary>
        /// <see cref="CheckBytes"/> and keep the verdict at once: for a caller that is itself the end of the check (a direct,
        /// unbudgeted call). Malformed verdicts are not kept.
        /// </summary>
        internal static BundleVerdict Check(byte[] data, int offset, int count, BundleCheckOptions options)
        {
            var verdict = CheckBytes(data, offset, count, options, out var key);
            if (key != null && BundleCheckBudget.Current == null && !IsMalformed(verdict)) cache?.Put(key, verdict.Passed, verdict.FirstReason);
            return verdict;
        }

        /// <summary>Hash, look up, and check on a thread of its own; the verdict is kept once the check has ended.</summary>
        internal static async Task<BundleVerdict> CheckAsync(byte[] data, int offset, int count, BundleCheckOptions options, CancellationToken cancel = default)
        {
            string key = null;
            var run = await RunCheckAsync(() => CheckBytes(data, offset, count, options, out key), options, cancel).ConfigureAwait(false);
            RememberContent(key, run);
            return run.Verdict;
        }

        /// <summary>Auto: only LZMA bundles (read from the block table; a bundle that does not open is checked, and refused, directly).</summary>
        static bool ShouldHash(byte[] data, int offset, int count, BundleCheckOptions options)
        {
            switch (Hashing)
            {
                case HashPolicy.Always: return true;
                case HashPolicy.Never: return false;
            }
            try
            {
                var stats = new BundleCheckStats();
                BundleArchive.Open(new ByteArrayBundleSource(data, offset, count), options, stats);
                return stats.Compression == "LZMA";
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// What one check may spend: wall-clock time and bytes read, decoded or walked. Charged on the check's own thread
    /// (<see cref="Current"/>, set by <see cref="BundleCheckService.RunCheckAsync"/>; none elsewhere, so direct calls are
    /// unbudgeted): by <see cref="BundleArchive"/> for every byte it serves or decodes, and by any rule that loops over
    /// captured data without reading (<see cref="ChargeCurrent"/>, or <see cref="CheckCurrent"/> for time alone). Over
    /// budget, a charge throws a <see cref="BundleRule.TooLarge"/> refusal; stopped or cancelled, it throws
    /// <see cref="OperationCanceledException"/>. A verdict made under a spent budget is never kept.
    /// </summary>
    internal sealed class BundleCheckBudget
    {
        [ThreadStatic] static BundleCheckBudget current;

        /// <summary>The budget of the check running on this thread, or null.</summary>
        internal static BundleCheckBudget Current => current;

        internal static void ChargeCurrent(long bytes) => current?.Charge(bytes);

        internal static void CheckCurrent() => current?.Charge(0);

        internal static void Enter(BundleCheckBudget budget) => current = budget;

        internal static void Leave() => current = null;

        internal readonly TimeSpan WallClock;
        internal readonly long MaxBytes;
        readonly CancellationToken cancellation;
        readonly Stopwatch clock = Stopwatch.StartNew();
        long bytes;
        volatile bool stopped;
        volatile string exceeded;

        internal BundleCheckBudget(TimeSpan wallClock, long maxBytes, CancellationToken cancellation = default)
        {
            WallClock = wallClock;
            MaxBytes = maxBytes;
            this.cancellation = cancellation;
        }

        internal long Bytes => Interlocked.Read(ref bytes);

        internal TimeSpan Elapsed => clock.Elapsed;

        internal bool Exceeded => exceeded != null;

        internal bool Stopped => stopped || cancellation.IsCancellationRequested;

        /// <summary>Whether a verdict made under this budget may be kept (not over it, not stopped, not cancelled).</summary>
        internal bool MayKeep => exceeded == null && !Stopped;

        /// <summary>Tells the check to stop at its next charge (its caller stopped waiting).</summary>
        internal void Stop() => stopped = true;

        internal void Charge(long count)
        {
            if (count > 0) Interlocked.Add(ref bytes, count);
            if (Stopped) throw new OperationCanceledException("bundle check stopped");
            if (exceeded == null)
            {
                if (Bytes > MaxBytes) exceeded = "it reads over " + MaxBytes / (1024 * 1024) + " MiB";
                else if (clock.Elapsed > WallClock) exceeded = "it runs over " + (int)WallClock.TotalSeconds + " s";
            }
            if (exceeded != null) throw new BundleRefusedException(BundleRule.TooLarge, "the check went over its budget: " + exceeded);
        }

        internal BundleVerdict Refusal(string versions) =>
            new BundleVerdict(BundleVerdictKind.Refuse,
                new[] { new BundleFinding(BundleRule.TooLarge, "", "the check went over its budget: " + (exceeded ?? "it did not finish within " + (int)WallClock.TotalSeconds + " s")) },
                null, null, versions);
    }

    /// <summary>Log lines from the check and its loaders: Unity's console, or nothing where there is no Unity (offline tools).</summary>
    internal static class BundleLog
    {
        internal static void Info(string text)
        {
            try
            {
                UnityEngine.Debug.Log(text);
            }
            catch (Exception)
            {
                // no Unity runtime (offline tools and tests)
            }
        }

        internal static void Warning(string text)
        {
            try
            {
                UnityEngine.Debug.LogWarning(text);
            }
            catch (Exception)
            {
                // no Unity runtime (offline tools and tests)
            }
        }
    }
}
