using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace BS.Security
{
    /// <summary>
    /// The checked raw-bundle load (worlds published as plain UnityFS, and kits), apart from Unity: <see cref="Get"/> supplies
    /// the download, the LZMA-to-LZ4 recompress and the file load as <see cref="Steps{T}"/>, so tests can drive the whole path.
    /// </summary>
    /// <remarks>
    /// Under the entry's lock: find or download the entry, check its file on the check thread, then (back here, once the
    /// check has ended) keep or drop what the check said, decide under <see cref="BundleCheckService.Mode"/>, and load that
    /// same file. The cache changes only here: a verdict is kept only from a check that finished on its own and did not call
    /// the bytes malformed; an entry is removed only when its load is refused as malformed (Enforce, or the structural
    /// refusals ReportOnly enforces too; perhaps a damaged download) or when Unity itself cannot load it. A check that did
    /// not finish (budget, watchdog, cancel) changes nothing.
    /// </remarks>
    internal static class BundleFileLoader
    {
        internal sealed class Steps<T> where T : class
        {
            /// <summary>Downloads the URL to the path, reporting 0..1; throws on failure (OperationCanceledException when cancelled).</summary>
            internal Func<string, string, Action<float>, CancellationToken, Task> Download;
            /// <summary>Makes the downloaded file loadable (recompresses an LZMA bundle to LZ4 in place), reporting 0..1.</summary>
            internal Func<string, Action<float>, Task> Prepare;
            /// <summary>Loads the bundle from its file; null when it could not.</summary>
            internal Func<string, Task<T>> Load;
        }

        internal sealed class Loaded<T> where T : class
        {
            internal T Bundle;
            internal BundleFileCache.Entry Entry;
            /// <summary>The check's run; null when the check is off (or skipped for tests).</summary>
            internal BundleCheckService.CheckRun Run;
        }

        /// <summary>
        /// Loads <paramref name="url"/> through the raw-bundle cache, checked as <paramref name="options"/> (a world or a kit).
        /// <paramref name="expectedBytes"/>: the download's size when the server said (0 otherwise), to make room first.
        /// Throws <see cref="BundleLoadRefusedException"/> when Enforce refuses it.
        /// </summary>
        internal static async Task<Loaded<T>> LoadAsync<T>(string url, string key, long expectedBytes, BundleCheckOptions options, bool kit,
            Action<float> progress, CancellationToken cancellation, Steps<T> steps) where T : class
        {
            using (await BundleFileCache.Lock(key))
            {
                var entry = BundleFileCache.Find(key, url);
                if (entry == null)
                {
                    BundleFileCache.MakeRoom(expectedBytes);
                    var partial = BundleFileCache.PartialPath(key);
                    await steps.Download(url, partial, p => progress?.Invoke(0.85f * Clamp(p)), cancellation);
                    await steps.Prepare(partial, p => progress?.Invoke(0.85f + 0.1f * Clamp(p)));
                    entry = BundleFileCache.Commit(key, url);
                }
                progress?.Invoke(0.95f);
                var (run, refusal) = await JudgeAsync(entry, url, options, kit, progress, cancellation);
                if (refusal != null) throw new BundleLoadRefusedException(refusal);
                cancellation.ThrowIfCancellationRequested();

                BundleFileCache.MarkInUse(entry); // from here Unity may read the file, for as long as the bundle lives
                T bundle = null;
                try
                {
                    bundle = await steps.Load(entry.BundlePath);
                }
                finally
                {
                    if (bundle == null)
                    {
                        // Unity could not load it: the file is not in use, and the entry goes (downloaded again next time).
                        BundleFileCache.ReleaseInUse(entry.FileName);
                        BundleFileCache.Remove(entry);
                    }
                }
                if (bundle == null) throw new Exception("Unable to load asset bundle from " + url);
                BundleFileCache.Touch(entry);
                progress?.Invoke(1f);
                return new Loaded<T> { Bundle = bundle, Entry = entry, Run = run };
            }
        }

        /// <summary>The check of a cache entry and what it changes in the cache; then what the load does (null: go on).</summary>
        static async Task<(BundleCheckService.CheckRun run, string refusal)> JudgeAsync(BundleFileCache.Entry entry, string url, BundleCheckOptions options,
            bool kit, Action<float> progress, CancellationToken cancellation)
        {
            var mode = BundleCheckService.Mode;
            if (mode == BundleCheckService.EnforcementMode.Off || BundleCheckService.SkippedForTests) return (null, null);
            var versions = BundleCheckPolicy.Versions(options, BundleCheckService.Environment);
            BundleCheckService.CheckRun run;
            if (BundleFileCache.TryRecall(entry, versions, out var passed, out var reason))
            {
                run = new BundleCheckService.CheckRun(BundlePreloadHooks.Recalled(passed, reason, versions, entry.Length), BundleCheckService.CheckState.Judged,
                    TimeSpan.Zero, 0);
            }
            else
            {
                run = await WithProgress(BundleCheckService.RunCheckAsync(() => BundlePreloadHooks.JudgeCacheEntry(entry, options), options, cancellation),
                    progress, 0.95f, 0.99f);
                // The loader's side, under the entry's lock, after the check has ended.
                // A malformed entry the load refuses (Enforce, or a structural refusal under ReportOnly) goes: perhaps damaged,
                // downloaded again next time. One a load still goes on with stays.
                if (run.Keepable) BundleFileCache.Remember(entry, versions, run.Verdict);
                else if (run.State == BundleCheckService.CheckState.Judged && BundleCheckService.IsMalformed(run.Verdict) && BundlePreloadHooks.Stops(run, mode))
                    BundleFileCache.Remove(entry);
            }
            return (run, BundlePreloadHooks.Decide(run, kit, url, mode));
        }

        /// <summary>Waits for the check, moving the progress from <paramref name="from"/> toward <paramref name="to"/> meanwhile.</summary>
        internal static async Task<BundleCheckService.CheckRun> WithProgress(Task<BundleCheckService.CheckRun> check, Action<float> progress, float from, float to)
        {
            if (progress != null)
            {
                var clock = Stopwatch.StartNew();
                while (!check.IsCompleted)
                {
                    progress(from + (to - from) * (float)(1 - Math.Exp(-clock.Elapsed.TotalSeconds / 3)));
                    await Task.WhenAny(check, Task.Delay(100));
                }
            }
            return await check;
        }

        static float Clamp(float value) => value < 0 ? 0 : value > 1 ? 1 : value;
    }

    /// <summary>The content check refused a bundle under Enforce; the message is the one the load fails with.</summary>
    internal sealed class BundleLoadRefusedException : Exception
    {
        internal BundleLoadRefusedException(string message) : base(message) { }
    }
}
