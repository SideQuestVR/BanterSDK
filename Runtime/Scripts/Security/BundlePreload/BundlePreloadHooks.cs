using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace BS.Security
{
    /// <summary>
    /// Wires the pre-load content check (<see cref="BundleCheckService"/>) into the app's bundle loaders, in Greenfield:
    /// <list type="bullet">
    /// <item>encrypted worlds (<c>.bee</c>), through Basis' <see cref="BasisPreLoadCheck.Hook"/>: the decrypted bytes are
    /// checked before Basis hands the same array to <c>AssetBundle.LoadFromMemoryAsync</c>, and the verdict is kept beside
    /// Basis' cached file (<c>{bee}.bundlecheck.json</c>), bound to that file as it was when its bytes were read;</item>
    /// <item>raw worlds and kits, through <see cref="Get"/> and <see cref="BundleFileLoader"/>: downloaded into
    /// <see cref="BundleFileCache"/> (a file of their own per download), checked from that file, which is then loaded with
    /// <c>AssetBundle.LoadFromFileAsync</c>.</item>
    /// </list>
    /// Checks run on a thread of their own under a budget (<see cref="BundleCheckService.RunCheckAsync"/>) and only read;
    /// the caches are written here, after the check. What a refusal does is <see cref="BundleCheckService.Mode"/> and
    /// <see cref="Stops"/>: under ReportOnly (the default for now) only a structural refusal fails the load and any other
    /// is logged; under Enforce every refusal fails it.
    /// Avatars are not checked: they load through Basis as <see cref="BasisPreLoadCheck.Kind.Unchecked"/>, because a
    /// peer's avatar resolves only to this client's own catalog, and a URL avatar only ever loads for the user who chose it.
    /// </summary>
    /// <remarks>
    /// A verdict is bound to the app's own cache entry (URL, HTTP validators or version tag, file length and write time,
    /// a nonce written when the entry was made), not to a hash of its bytes: on Quest hashing a world costs more than
    /// checking it. Bytes that come from none of these caches (a local <c>.bee</c>) fall back to the content-hash verdict
    /// cache (<see cref="BundleCheckService.Hashing"/>). Someone who can write this app's private storage is a local
    /// attacker and out of scope: they could as well change the app. A creator project (no GREENFIELD_PROJECT) registers
    /// nothing and loads unchecked, as before.
    /// </remarks>
    internal static class BundlePreloadHooks
    {
        /// <summary>Whether the check is wired in: from start-up in Greenfield, never in a creator project.</summary>
        internal static bool Active { get; private set; }

        /// <summary>The raw-bundle cache's cap (the .bee files are in Basis' own cache).</summary>
        const long CacheCapacityBytes =
#if UNITY_ANDROID && !UNITY_EDITOR
            2L * 1024 * 1024 * 1024;
#else
            6L * 1024 * 1024 * 1024;
#endif

#if GREENFIELD_PROJECT
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register() => EnsureRegistered();
#endif

        /// <summary>Main thread: configures the verdict caches and installs the Basis hook. Calling it again is harmless.</summary>
        internal static void EnsureRegistered()
        {
            var root = Path.Combine(Application.persistentDataPath, "SideQuest");
            BundleCheckService.Configure(Path.Combine(root, "bundle-verdicts.json"), Application.version + "+" + Application.buildGUID);
            BundleFileCache.Configure(Path.Combine(root, "bundle-cache"), CacheCapacityBytes);
            BasisPreLoadCheck.SetHook(CheckDecryptedAsync);
            if (Active) return;
            Active = true;
            ReclaimUnityCacheOnce(root);
            _ = Task.Run(() =>
            {
                BundleFileCache.Trim(); // last run's leftovers, and the cap
                RemoveOrphanVerdicts();
            });
        }

        /// <summary>
        /// Main thread: the kind a loader should load <paramref name="kind"/> content as. In Greenfield the check is wired
        /// in first if start-up has not done it yet (fail closed); in a creator project nothing is checked.
        /// </summary>
        internal static BasisPreLoadCheck.Kind EffectiveKind(BasisPreLoadCheck.Kind kind)
        {
#if GREENFIELD_PROJECT
            if (kind != BasisPreLoadCheck.Kind.Unchecked && !Active) EnsureRegistered();
            return kind;
#else
            return BasisPreLoadCheck.Kind.Unchecked;
#endif
        }

        internal static BundleCheckOptions OptionsFor(BasisPreLoadCheck.Kind kind) =>
            kind == BasisPreLoadCheck.Kind.Kit ? BundleCheckOptions.Kit() : BundleCheckOptions.World();

        /// <summary>Beside a cached .bee: <c>{bee}{VerdictSuffix}</c> (Basis' constant, here for callers without Basis).</summary>
        internal const string VerdictSuffix = BasisPreLoadCheck.VerdictSuffix;

        // ------------------------------------------------------------------ old caches

        /// <summary>
        /// Raw bundles used to go through Unity's own bundle cache, which nothing reads any more: it is cleared once (a marker
        /// file remembers), at start-up before any bundle is loaded. If Unity reports it in use, it is tried again next start.
        /// </summary>
        static void ReclaimUnityCacheOnce(string root)
        {
            var marker = Path.Combine(root, "unity-bundle-cache-reclaimed");
            try
            {
                if (File.Exists(marker)) return;
                if (!Caching.ClearCache())
                {
                    BundleLog.Info("[BundleCheck] Unity's old bundle cache is in use; it is cleared at a later start.");
                    return;
                }
                Directory.CreateDirectory(root);
                File.WriteAllText(marker, DateTime.UtcNow.ToString("o"));
            }
            catch (Exception e)
            {
                BundleLog.Info("[BundleCheck] Unity's old bundle cache was not cleared (" + e.GetType().Name + ").");
            }
        }

        /// <summary>
        /// Verdicts beside .bee files Basis has evicted (its LRU deletes the .bee, not what sits beside it); any thread.
        /// Answers the bytes freed.
        /// </summary>
        internal static long RemoveOrphanVerdicts()
        {
            long freed = 0;
            try
            {
                var folder = BasisIOManagement.GenerateFolderPath(BasisBeeConstants.AssetBundlesFolder);
                if (!Directory.Exists(folder)) return 0;
                foreach (var sidecar in Directory.GetFiles(folder, "*" + VerdictSuffix))
                {
                    var bee = sidecar.Substring(0, sidecar.Length - VerdictSuffix.Length);
                    if (File.Exists(bee)) continue;
                    try
                    {
                        var size = new FileInfo(sidecar).Length;
                        File.Delete(sidecar);
                        freed += size;
                    }
                    catch (Exception)
                    {
                        // tried again next time
                    }
                }
            }
            catch (Exception)
            {
                // Basis not ready yet (its folder is set on the main thread at its start): tried again next time
            }
            return freed;
        }

        // ------------------------------------------------------------------ loaded raw bundles

        static readonly List<(string fileName, UnityEngine.Object bundle)> loadedFiles = new List<(string, UnityEngine.Object)>();

        /// <summary>Main thread: a raw bundle Unity loaded from this entry's file (<see cref="BundleFileCache.MarkInUse"/> was taken before the load).</summary>
        internal static void TrackLoaded(BundleFileCache.Entry entry, UnityEngine.Object bundle)
        {
            loadedFiles.Add((entry.FileName, bundle));
        }

        /// <summary>Main thread: releases the files of raw bundles that have been unloaded since, so they may be trimmed.</summary>
        internal static void PruneUnloaded()
        {
            for (var i = loadedFiles.Count - 1; i >= 0; i--)
            {
                if (loadedFiles[i].bundle != null) continue; // Unity's null: unloaded
                BundleFileCache.ReleaseInUse(loadedFiles[i].fileName);
                loadedFiles.RemoveAt(i);
            }
        }

        // ------------------------------------------------------------------ encrypted (.bee), from Basis

        /// <summary>The Basis hook: null when the decrypted bundle may load, else the message its load fails with.</summary>
        internal static async Task<string> CheckDecryptedAsync(byte[] bytes, BasisPreLoadCheck.Request request)
        {
            if (request == null || request.Kind == BasisPreLoadCheck.Kind.Unchecked) return null;
            var mode = BundleCheckService.Mode;
            if (mode == BundleCheckService.EnforcementMode.Off) return null;
            if (BundleCheckService.SkippedForTests) return null; // nothing judged, nothing remembered
            var kit = request.Kind == BasisPreLoadCheck.Kind.Kit;
            var options = OptionsFor(request.Kind);
            var url = request.Url;
            if (bytes == null)
            {
                var none = new BundleCheckService.CheckRun(Malformed("no bundle data", options), BundleCheckService.CheckState.Judged, TimeSpan.Zero, 0);
                return Decide(none, kit, url, mode);
            }
            var versionTag = request.VersionTag;
            var file = request.CacheEntryFile;
            var downloaded = request.Downloaded;
            var stampLength = request.EntryLength;
            var stampTicks = request.EntryWriteTicks;
            // Basis decrypts in place: only the first DataLength bytes are the bundle, and exactly those are what Unity loads.
            var length = request.DataLength > 0 && request.DataLength <= bytes.Length ? request.DataLength : bytes.Length;
            BeeVerdictRecord record = null;
            string contentKey = null;
            var run = await BundleCheckService.RunCheckAsync(
                () => JudgeDecrypted(bytes, length, options, url, versionTag, file, downloaded, stampLength, stampTicks, out record, out contentKey), options);
            AfterDecryptedCheck(run, record, contentKey, file, stampLength, stampTicks);
            return Decide(run, kit, url, mode);
        }

        /// <summary>
        /// On the check's thread, reading only: the verdict kept beside Basis' cached file (<paramref name="file"/>) when it
        /// still holds, else a fresh check, with the <paramref name="record"/> to keep for it (or the content-hash
        /// <paramref name="contentKey"/>, for bytes from no cache entry). The stamp is the file's length and write time when
        /// Basis read its bytes (or wrote them, <paramref name="downloaded"/>); a file that no longer matches it changed since,
        /// and nothing is recalled or kept.
        /// </summary>
        internal static BundleVerdict JudgeDecrypted(byte[] bytes, BundleCheckOptions options, string url, string versionTag, string file, bool downloaded,
            long stampLength, long stampTicks, out BeeVerdictRecord record, out string contentKey) =>
            JudgeDecrypted(bytes, bytes.Length, options, url, versionTag, file, downloaded, stampLength, stampTicks, out record, out contentKey);

        /// <summary>As above, for the bundle in the first <paramref name="count"/> bytes of <paramref name="bytes"/>.</summary>
        internal static BundleVerdict JudgeDecrypted(byte[] bytes, int count, BundleCheckOptions options, string url, string versionTag, string file, bool downloaded,
            long stampLength, long stampTicks, out BeeVerdictRecord record, out string contentKey)
        {
            record = null;
            contentKey = null;
            if (BundleCheckService.SkippedForTests) return Fresh(bytes, count, options);
            if (string.IsNullOrEmpty(file))
            {
                // Not from Basis' cache (a .bee on disk, read where it lies): the content-hash fallback.
                return BundleCheckService.CheckBytes(bytes, 0, count, options, out contentKey);
            }
            if (stampTicks == 0 || !TryStamp(file, out var length, out var written) || length != stampLength || written != stampTicks)
            {
                return Fresh(bytes, count, options); // no stamp, or the file changed after its bytes were read: judged, nothing kept
            }
            var versions = BundleCheckPolicy.Versions(options, BundleCheckService.Environment);
            var candidate = new BeeVerdictRecord
            {
                UrlHash = BundleFileCache.HashUrl(url),
                TagHash = BundleFileCache.HashUrl(versionTag ?? ""),
                Kind = options.MaxScenes == 0 ? "Kit" : "World",
                Versions = versions,
                Length = stampLength,
                Written = stampTicks,
                Section = count,
            };
            var kept = downloaded ? null : BeeVerdictRecord.Read(file + VerdictSuffix);
            if (kept != null && kept.SameEntry(candidate))
            {
                if (kept.Versions == versions) return Recalled(kept.Passed, kept.Reason, versions, count);
                candidate.Nonce = kept.Nonce; // the same download, judged again under newer rules
            }
            if (string.IsNullOrEmpty(candidate.Nonce)) candidate.Nonce = Guid.NewGuid().ToString("N");
            var verdict = Fresh(bytes, count, options);
            candidate.Passed = verdict.Passed;
            candidate.Reason = verdict.FirstReason;
            record = candidate;
            return verdict;
        }

        static BundleVerdict Fresh(byte[] bytes, int count, BundleCheckOptions options) =>
            BundleCheckService.CheckFresh(new ByteArrayBundleSource(bytes, 0, count), options);

        /// <summary>
        /// After a .bee check has ended, on the caller's side: keeps its verdict beside the cache file (if that file is still
        /// as it was read) or in the content-hash cache, only when the check finished on its own and did not call the bytes
        /// malformed. Nothing is ever deleted here.
        /// </summary>
        internal static void AfterDecryptedCheck(BundleCheckService.CheckRun run, BeeVerdictRecord record, string contentKey, string file,
            long stampLength, long stampTicks)
        {
            if (!run.Keepable) return;
            if (record != null && TryStamp(file, out var length, out var written) && length == stampLength && written == stampTicks)
                record.Write(file + VerdictSuffix);
            BundleCheckService.RememberContent(contentKey, run);
        }

        static bool TryStamp(string file, out long length, out long written)
        {
            length = written = 0;
            try
            {
                var info = new FileInfo(file);
                if (!info.Exists) return false;
                length = info.Length;
                written = info.LastWriteTimeUtc.Ticks;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// A .bee's verdict, beside it: valid only for the same URL, version tag, kind, file length and write time, and
        /// decrypted section length, under the same checker/list/option versions. Basis deletes it whenever it rewrites the
        /// file (<see cref="BasisPreLoadCheck.ForgetVerdict"/>). One line of JSON whose values carry no quotes or escapes.
        /// </summary>
        internal sealed class BeeVerdictRecord
        {
            internal string UrlHash, TagHash, Kind, Versions, Nonce, Reason;
            internal long Length, Written, Section;
            internal bool Passed;

            internal bool SameEntry(BeeVerdictRecord other) =>
                UrlHash == other.UrlHash && TagHash == other.TagHash && Kind == other.Kind && Length == other.Length
                && Written == other.Written && Section == other.Section && !string.IsNullOrEmpty(Nonce);

            internal void Write(string path)
            {
                var text = new StringBuilder("{\"format\":1")
                    .Append(",\"url\":\"").Append(Plain(UrlHash)).Append('"')
                    .Append(",\"tag\":\"").Append(Plain(TagHash)).Append('"')
                    .Append(",\"kind\":\"").Append(Plain(Kind)).Append('"')
                    .Append(",\"versions\":\"").Append(Plain(Versions)).Append('"')
                    .Append(",\"length\":").Append(Length)
                    .Append(",\"written\":").Append(Written)
                    .Append(",\"section\":").Append(Section)
                    .Append(",\"nonce\":\"").Append(Plain(Nonce)).Append('"')
                    .Append(",\"passed\":").Append(Passed ? "true" : "false")
                    .Append(",\"reason\":\"").Append(Plain(Reason)).Append("\"}\n");
                var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllText(temporary, text.ToString(), new UTF8Encoding(false));
                    if (File.Exists(path)) File.Delete(path);
                    File.Move(temporary, path);
                }
                catch (Exception)
                {
                    // unwritten: the next load checks again
                    try
                    {
                        if (File.Exists(temporary)) File.Delete(temporary);
                    }
                    catch (Exception)
                    {
                        // left for Basis' own clear
                    }
                }
            }

            /// <summary>The record at <paramref name="path"/>, or null (missing, or anything unexpected in it).</summary>
            internal static BeeVerdictRecord Read(string path)
            {
                try
                {
                    if (!File.Exists(path)) return null;
                    var info = new FileInfo(path);
                    if (info.Length > 4096) return null;
                    var line = File.ReadAllText(path, Encoding.UTF8).Trim();
                    if (!line.StartsWith("{\"format\":1,", StringComparison.Ordinal) || !line.EndsWith("}", StringComparison.Ordinal)) return null;
                    var record = new BeeVerdictRecord
                    {
                        UrlHash = Text(line, "url"),
                        TagHash = Text(line, "tag"),
                        Kind = Text(line, "kind"),
                        Versions = Text(line, "versions"),
                        Nonce = Text(line, "nonce"),
                        Reason = Text(line, "reason"),
                    };
                    var passed = Bare(line, "passed");
                    if (record.UrlHash == null || record.TagHash == null || record.Kind == null || record.Versions == null
                        || string.IsNullOrEmpty(record.Nonce) || record.Reason == null || (passed != "true" && passed != "false")
                        || !long.TryParse(Bare(line, "length"), out record.Length)
                        || !long.TryParse(Bare(line, "written"), out record.Written)
                        || !long.TryParse(Bare(line, "section"), out record.Section))
                        return null;
                    record.Passed = passed == "true";
                    return record;
                }
                catch (Exception)
                {
                    return null; // corrupt: judged again
                }
            }

            static string Text(string line, string name)
            {
                var prefix = "\"" + name + "\":\"";
                var start = line.IndexOf(prefix, StringComparison.Ordinal);
                if (start < 0) return null;
                start += prefix.Length;
                var end = line.IndexOf('"', start);
                return end < 0 ? null : line.Substring(start, end - start);
            }

            static string Bare(string line, string name)
            {
                var prefix = "\"" + name + "\":";
                var start = line.IndexOf(prefix, StringComparison.Ordinal);
                if (start < 0) return null;
                start += prefix.Length;
                var end = start;
                while (end < line.Length && line[end] != ',' && line[end] != '}') end++;
                return line.Substring(start, end - start);
            }

            static string Plain(string text)
            {
                if (string.IsNullOrEmpty(text)) return "";
                var clean = new StringBuilder(Math.Min(text.Length, 400));
                foreach (var ch in text)
                {
                    if (clean.Length >= 400) break;
                    clean.Append(char.IsControl(ch) || ch == '"' || ch == '\\' ? '?' : ch);
                }
                return clean.ToString();
            }
        }

        // ------------------------------------------------------------------ raw bundles (worlds and kits), from Get

        /// <summary>
        /// Main thread, before a raw download becomes a cache entry: an LZMA bundle (one block, which LoadFromFile would
        /// decode whole into memory, and the check would decode in managed memory) is recompressed to LZ4 on disk by Unity,
        /// as Unity's download cache did for the old path. Recompressing re-blocks the archive; nothing in it is loaded.
        /// </summary>
        internal static async Task PrepareDownloadAsync(string partialPath, Action<float> progress)
        {
            if (!await Task.Run(() => IsLzma(partialPath))) return;
            var recompressed = partialPath + ".lz4";
            var operation = UnityEngine.AssetBundle.RecompressAssetBundleAsync(partialPath, recompressed, BuildCompression.LZ4Runtime, 0, UnityEngine.ThreadPriority.Low);
            while (!operation.isDone)
            {
                progress?.Invoke(operation.progress);
                await Task.Delay(100);
            }
            if (!operation.success)
            {
                TryDelete(recompressed);
                TryDelete(partialPath);
                throw new Exception("Unable to read the asset bundle (" + operation.result + ")");
            }
            progress?.Invoke(1f);
            File.Delete(partialPath);
            File.Move(recompressed, partialPath);
        }

        /// <summary>Whether the archive's blocks are LZMA (header and block table only, both bounded).</summary>
        static bool IsLzma(string path)
        {
            try
            {
                using (var source = new FileBundleSource(path))
                {
                    var options = BundleCheckOptions.World();
                    options.MaxLzmaBlockBytes = long.MaxValue; // only asking
                    var stats = new BundleCheckStats();
                    BundleArchive.Open(source, options, stats);
                    return stats.Compression == "LZMA";
                }
            }
            catch (Exception)
            {
                return false; // not an archive the checker opens: the check refuses it as it is
            }
        }

        static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception)
            {
                // a stale .part is trimmed later
            }
        }

        /// <summary>
        /// On the check's thread, reading only: the verdict kept with the entry when it still holds, else a check of its
        /// file. What to keep or drop is the loader's to do once the check has ended (<see cref="BundleFileLoader"/>).
        /// </summary>
        internal static BundleVerdict JudgeCacheEntry(BundleFileCache.Entry entry, BundleCheckOptions options)
        {
            var versions = BundleCheckPolicy.Versions(options, BundleCheckService.Environment);
            if (!BundleCheckService.SkippedForTests && BundleFileCache.TryRecall(entry, versions, out var passed, out var reason))
                return Recalled(passed, reason, versions, entry.Length);
            using (var source = new FileBundleSource(entry.BundlePath))
            {
                return BundleCheckService.CheckFresh(source, options);
            }
        }

        // ------------------------------------------------------------------ verdicts and messages

        internal static BundleVerdict Recalled(bool passed, string reason, string versions, long bytes) =>
            new BundleVerdict(passed ? BundleVerdictKind.Pass : BundleVerdictKind.Refuse,
                passed ? null : new[] { BundleCheckService.RecalledFinding(reason) }, null,
                new BundleCheckStats { BundleBytes = bytes }, versions, true);

        /// <summary>"This world was refused by the content check: Rule: detail" (a kit: "A kit in this world ..."). No argument values: findings never carry them.</summary>
        internal static string RefusalMessage(bool kit, BundleVerdict verdict)
        {
            var reason = verdict.Reasons.Count > 0 ? verdict.Reasons[0] : null;
            var text = reason == null ? "no reason given"
                : reason.Rule == BundleRule.RefusedBefore ? reason.Detail
                : reason.Rule + ": " + reason.Detail;
            return RefusalMessage(kit, text);
        }

        static string RefusalMessage(bool kit, string reason)
        {
            var what = kit ? "A kit in this world" : "This world";
            var clean = new StringBuilder(Math.Min(reason?.Length ?? 0, 240));
            foreach (var ch in reason ?? "")
            {
                if (clean.Length >= 240) break;
                clean.Append(char.IsControl(ch) ? ' ' : ch);
            }
            return what + " was refused by the content check: " + clean;
        }

        /// <summary>
        /// Logs a finished check (verdict, how it ended, how long it took on its thread, what it charged) and answers what
        /// the load does: null to go on, or the refusal message when <paramref name="mode"/> enforces it. In ReportOnly a
        /// refusal is logged (sanitized: rules, names and counts, never argument values) and the load goes on.
        /// </summary>
        internal static string Decide(BundleCheckService.CheckRun run, bool kit, string url, BundleCheckService.EnforcementMode mode)
        {
            var label = LogLabel(url);
            var verdict = run.Verdict;
            var cost = "\n  check: " + run.State + (verdict.FromCache ? " (kept verdict)" : "") + ", " + (long)run.Elapsed.TotalMilliseconds
                       + " ms on its thread, " + run.Bytes / 1024 + " KiB charged (budget " + (int)BundleCheckService.WallClockBudget.TotalSeconds + " s)";
            var outcome = Outcome(run, kit, mode);
            if (verdict.Passed && run.State == BundleCheckService.CheckState.Judged) BundleLog.Info(verdict.Describe(label, 12) + cost);
            else if (outcome != null && IsStructural(run)) BundleLog.Warning("[BundleCheck] Refused (structural): " + outcome + "\n" + verdict.Describe(label) + cost);
            else if (outcome != null) BundleLog.Warning(verdict.Describe(label) + cost);
            else BundleLog.Warning("[BundleCheck] REPORT-ONLY, loading anyway: " + RefusalMessage(kit, verdict) + "\n" + verdict.Describe(label) + cost);
            return outcome;
        }

        /// <summary>
        /// A structural refusal (<see cref="BundleRules.IsStructural"/>: bytes the native loader could misread or crash on)
        /// from a check that finished on its own, its kept verdict included. The TooLarge of the size limits (an LZMA block
        /// over the platform's limit, an oversize LZ4 block or block table) comes this way: the archive refuses the bytes
        /// while the check runs, and the run ends <see cref="BundleCheckService.CheckState.Judged"/>. The TooLarge of the
        /// check's own budget (wall clock, bytes walked) never does: a spent budget ends the run
        /// <see cref="BundleCheckService.CheckState.OverBudget"/> (or Unknown, from the watchdog), which says how long the
        /// check took, not what the bytes are.
        /// </summary>
        internal static bool IsStructural(BundleCheckService.CheckRun run) =>
            run.State == BundleCheckService.CheckState.Judged && !run.Verdict.Passed && run.Verdict.RefusedStructurally;

        /// <summary>
        /// Whether the load stops after this check under <paramref name="mode"/>: Off never checks; Enforce stops every
        /// check that did not pass in full (refused, over budget, unknown); ReportOnly stops only structural refusals and
        /// loads everything else (policy refusals, over budget, unknown), logged.
        /// </summary>
        internal static bool Stops(BundleCheckService.CheckRun run, BundleCheckService.EnforcementMode mode)
        {
            if (mode == BundleCheckService.EnforcementMode.Off) return false;
            if (run.State == BundleCheckService.CheckState.Judged && run.Verdict.Passed) return false;
            if (mode == BundleCheckService.EnforcementMode.Enforce) return true;
            return IsStructural(run);
        }

        /// <summary>What a load does after a check under <paramref name="mode"/>: null to go on, else the message it fails with (see <see cref="Stops"/>).</summary>
        internal static string Outcome(BundleCheckService.CheckRun run, bool kit, BundleCheckService.EnforcementMode mode) =>
            Stops(run, mode) ? RefusalMessage(kit, run.Verdict) : null;

        /// <summary>A verdict from a finished check: what a load does with it under <paramref name="mode"/> (probes and tools).</summary>
        internal static string Outcome(BundleVerdict verdict, bool kit, BundleCheckService.EnforcementMode mode) =>
            Outcome(new BundleCheckService.CheckRun(verdict, BundleCheckService.CheckState.Judged, TimeSpan.Zero, 0), kit, mode);

        static BundleVerdict Malformed(string detail, BundleCheckOptions options) =>
            new BundleVerdict(BundleVerdictKind.Refuse, new[] { new BundleFinding(BundleRule.Malformed, "", detail) }, null, null,
                BundleCheckPolicy.Versions(options, BundleCheckService.Environment));

        /// <summary>Host and file name only: query strings can carry tokens.</summary>
        internal static string LogLabel(string url)
        {
            if (string.IsNullOrEmpty(url)) return "bundle";
            var text = url;
            var cut = text.IndexOfAny(new[] { '?', '#' });
            if (cut >= 0) text = text.Substring(0, cut);
            var host = "";
            var scheme = text.IndexOf("://", StringComparison.Ordinal);
            if (scheme >= 0)
            {
                text = text.Substring(scheme + 3);
                var slash = text.IndexOf('/');
                host = (slash < 0 ? text : text.Substring(0, slash));
                var at = host.LastIndexOf('@');
                if (at >= 0) host = host.Substring(at + 1); // never user info
                text = slash < 0 ? "" : text.Substring(slash);
            }
            text = text.TrimEnd('/', '\\');
            var name = text.Substring(Math.Max(text.LastIndexOf('/'), text.LastIndexOf('\\')) + 1);
            return host.Length > 0 ? host + "/" + name : name;
        }
    }
}
