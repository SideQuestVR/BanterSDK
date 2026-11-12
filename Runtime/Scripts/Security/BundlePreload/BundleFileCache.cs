using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BS.Security
{
    /// <summary>
    /// The raw-bundle cache (worlds published as plain UnityFS, and every kit): one downloaded file per URL + HTTP
    /// validators, checked once and loaded with <c>AssetBundle.LoadFromFileAsync</c>, so no managed copy of a large world
    /// is held. Each entry keeps its content verdict, bound to the entry itself: a re-download writes a new file under a
    /// new nonce, which drops the verdict.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The file Unity loads is the file that was checked: every download gets a file of its own
    /// (<c>{key}.{nonce}.bundle</c>), never written again, and a file Unity loaded is never deleted while its bundle may
    /// still be loaded (<see cref="MarkInUse"/> until <see cref="ReleaseInUse"/>), because Unity reads a bundle's file
    /// lazily for as long as it is loaded. The record, <c>{key}.entry</c> (plain key=value lines), names the entry's file.
    /// Writes happen on the loader's side, under the entry's lock, never on the check's thread.
    /// </para>
    /// <para>
    /// The key is a SHA-256 of the URL and its validators; the record keeps a hash of the URL only (page URLs can carry
    /// tokens). Someone who can write to this app's private storage is a local attacker and out of scope. Bounded: least
    /// recently used entries go first, at start-up and after each load.
    /// </para>
    /// </remarks>
    internal static class BundleFileCache
    {
        internal sealed class Entry
        {
            internal string Key;
            internal string FileName;
            internal string BundlePath;
            internal string EntryPath;
            internal string UrlHash;
            internal long Length;
            internal long Written;
            internal string Nonce;
            internal long Used;
            internal string VerdictVersions;
            internal bool Passed;
            internal string Reason;
        }

        sealed class KeyLock
        {
            internal readonly SemaphoreSlim Semaphore = new SemaphoreSlim(1, 1);
            internal int Users; // holders and waiters; the entry goes when it drops to 0
        }

        static readonly object gate = new object();
        static readonly Dictionary<string, KeyLock> locks = new Dictionary<string, KeyLock>(StringComparer.Ordinal);
        static readonly Dictionary<string, int> inUse = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        static string root;
        static long capacity;
        static long lastUse;

        /// <summary>Use stamps: the clock, but strictly increasing within a run (the LRU order of two quick loads holds).</summary>
        static long NextUse()
        {
            lock (gate)
            {
                lastUse = Math.Max(DateTime.UtcNow.Ticks, lastUse + 1);
                return lastUse;
            }
        }

        /// <summary>Call on the main thread: the folder (e.g. {persistentDataPath}/SideQuest/bundle-cache) and its size cap.</summary>
        internal static void Configure(string directory, long capacityBytes)
        {
            lock (gate)
            {
                root = directory;
                capacity = Math.Max(0, capacityBytes);
            }
        }

        internal static bool Configured => root != null;

        internal static string Root => root;

        /// <summary>The entry key for <paramref name="url"/> as served now: SHA-256 (hex) of the URL and its HTTP validators.</summary>
        internal static string KeyFor(string url, string lastModified, string etag) =>
            Sha256Hex((url ?? "") + "\n" + (lastModified ?? "") + "\n" + (etag ?? ""), 32);

        static KeyLock Join(string key)
        {
            lock (gate)
            {
                if (!locks.TryGetValue(key, out var keyLock)) locks[key] = keyLock = new KeyLock();
                keyLock.Users++;
                return keyLock;
            }
        }

        static void Leave(string key, KeyLock keyLock)
        {
            lock (gate)
            {
                if (--keyLock.Users == 0 && locks.TryGetValue(key, out var current) && ReferenceEquals(current, keyLock)) locks.Remove(key);
            }
        }

        /// <summary>One load at a time per entry (twins of the same URL wait for the first).</summary>
        internal static async Task<IDisposable> Lock(string key)
        {
            var keyLock = Join(key);
            try
            {
                await keyLock.Semaphore.WaitAsync().ConfigureAwait(false);
            }
            catch
            {
                Leave(key, keyLock);
                throw;
            }
            return new Releaser(key, keyLock);
        }

        sealed class Releaser : IDisposable
        {
            readonly string key;
            KeyLock keyLock;

            internal Releaser(string key, KeyLock keyLock)
            {
                this.key = key;
                this.keyLock = keyLock;
            }

            public void Dispose()
            {
                var held = keyLock;
                keyLock = null;
                if (held == null) return;
                held.Semaphore.Release();
                Leave(key, held);
            }
        }

        /// <summary>Takes the entry's lock if no load holds it (Trim deletes only what it holds itself), else null.</summary>
        static IDisposable TryHold(string key)
        {
            var keyLock = Join(key);
            if (keyLock.Semaphore.Wait(0)) return new Releaser(key, keyLock);
            Leave(key, keyLock);
            return null;
        }

        /// <summary>Entries in the lock table now (holders and waiters): bounded by the loads in progress.</summary>
        internal static int LockedKeys
        {
            get
            {
                lock (gate) return locks.Count;
            }
        }

        /// <summary>
        /// Unity is loading or has loaded this entry's file: it is not deleted, whatever else happens to the entry (a
        /// re-download, a trim, a clear), until <see cref="ReleaseInUse"/> says its bundle is gone.
        /// </summary>
        internal static void MarkInUse(Entry entry)
        {
            lock (gate)
            {
                inUse.TryGetValue(entry.FileName, out var count);
                inUse[entry.FileName] = count + 1;
            }
        }

        /// <summary>The bundle loaded from <paramref name="fileName"/> was unloaded (or never loaded): its file may go.</summary>
        internal static void ReleaseInUse(string fileName)
        {
            lock (gate)
            {
                if (!inUse.TryGetValue(fileName, out var count)) return;
                if (count <= 1) inUse.Remove(fileName);
                else inUse[fileName] = count - 1;
            }
        }

        internal static int InUseCount
        {
            get
            {
                lock (gate) return inUse.Count;
            }
        }

        static bool IsInUse(string fileName)
        {
            lock (gate) return inUse.ContainsKey(fileName);
        }

        static string Path(string name) => System.IO.Path.Combine(root, name);

        /// <summary>Where a download is written before it becomes an entry (under the entry's lock).</summary>
        internal static string PartialPath(string key)
        {
            Directory.CreateDirectory(root);
            return Path(key + ".part");
        }

        internal static string HashUrl(string url) => Sha256Hex(url ?? "", 16);

        static string Sha256Hex(string text, int bytes)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                var hex = new StringBuilder(bytes * 2);
                for (var i = 0; i < bytes; i++) hex.Append(hash[i].ToString("x2"));
                return hex.ToString();
            }
        }

        /// <summary>The complete entry for this key and URL, or null (missing, partial, or rewritten behind the record).</summary>
        internal static Entry Find(string key, string url)
        {
            var entry = Read(key);
            if (entry == null) return null;
            try
            {
                var file = new FileInfo(entry.BundlePath);
                if (!file.Exists || file.Length != entry.Length || file.LastWriteTimeUtc.Ticks != entry.Written || entry.UrlHash != HashUrl(url))
                {
                    Remove(entry);
                    return null;
                }
            }
            catch (Exception)
            {
                return null;
            }
            return entry;
        }

        /// <summary>
        /// Makes a finished download (<see cref="PartialPath"/>) the entry for this key: a file of its own under a fresh
        /// nonce, no verdict. The key's previous file is left for <see cref="Trim"/> (it may be loaded).
        /// </summary>
        internal static Entry Commit(string key, string url)
        {
            if (!IsKey(key)) throw new ArgumentException("not a cache key", nameof(key));
            var nonce = Guid.NewGuid().ToString("N");
            var fileName = key + "." + nonce + ".bundle";
            var bundlePath = Path(fileName);
            File.Move(Path(key + ".part"), bundlePath);
            var file = new FileInfo(bundlePath);
            var entry = new Entry
            {
                Key = key,
                FileName = fileName,
                BundlePath = bundlePath,
                EntryPath = Path(key + ".entry"),
                UrlHash = HashUrl(url),
                Length = file.Length,
                Written = file.LastWriteTimeUtc.Ticks,
                Nonce = nonce,
                Used = NextUse(),
            };
            Write(entry);
            return entry;
        }

        /// <summary>The verdict kept with this entry, when it was made under the same checker/list/option versions.</summary>
        internal static bool TryRecall(Entry entry, string versions, out bool passed, out string reason)
        {
            passed = entry.Passed;
            reason = entry.Reason;
            return entry.VerdictVersions != null && entry.VerdictVersions == versions;
        }

        internal static void Remember(Entry entry, string versions, BundleVerdict verdict)
        {
            entry.VerdictVersions = versions;
            entry.Passed = verdict.Passed;
            entry.Reason = verdict.FirstReason;
            Write(entry);
        }

        internal static void Touch(Entry entry)
        {
            entry.Used = NextUse();
            Write(entry);
        }

        /// <summary>Deletes the entry's record, and its file unless Unity loaded it in this run.</summary>
        internal static void Remove(Entry entry)
        {
            TryDelete(entry.EntryPath);
            if (!IsInUse(entry.FileName)) TryDelete(entry.BundlePath);
        }

        static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception)
            {
                // gone, or held by the system: tried again on the next trim
            }
        }

        /// <summary>
        /// Before a download of about <paramref name="expectedBytes"/> (0: unknown): when the disk has less free space than
        /// the download may need (recompressing an LZMA bundle to LZ4 can take several times its size), trims the cache by
        /// the shortfall. Never fails the download itself; the disk's own error does, if it comes to that.
        /// </summary>
        internal static void MakeRoom(long expectedBytes)
        {
            var free = FreeBytes();
            if (free < 0) return; // unknown here
            var need = Math.Max(0, expectedBytes) * 4 + 256L * 1024 * 1024;
            if (free >= need) return;
            var shortfall = need - free;
            Trim(Math.Max(0, TotalBytes() - shortfall));
        }

        /// <summary>Free bytes on the cache's volume, or -1 when the platform does not say.</summary>
        internal static long FreeBytes()
        {
            try
            {
                if (root == null) return -1;
                var full = System.IO.Path.GetFullPath(root);
                DriveInfo best = null;
                var bestLength = -1;
                foreach (var drive in DriveInfo.GetDrives())
                {
                    try
                    {
                        var at = drive.RootDirectory.FullName;
                        if (at.Length > bestLength && full.StartsWith(at, StringComparison.OrdinalIgnoreCase))
                        {
                            best = drive;
                            bestLength = at.Length;
                        }
                    }
                    catch (Exception)
                    {
                        // a volume that cannot be asked
                    }
                }
                return best != null && best.IsReady ? best.AvailableFreeSpace : -1;
            }
            catch (Exception)
            {
                return -1;
            }
        }

        /// <summary>Bytes of bundle files in the cache (entries, files still loaded, leftovers). Any thread.</summary>
        internal static long TotalBytes()
        {
            try
            {
                if (root == null || !Directory.Exists(root)) return 0;
                long total = 0;
                foreach (var path in Directory.GetFiles(root)) total += SizeOf(path);
                return total;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        /// <summary>
        /// Deletes every entry and file no load holds and Unity has not loaded (Clear Cache); answers the bytes freed.
        /// Loaded bundles keep their files and records. Any thread.
        /// </summary>
        internal static long ClearUnused()
        {
            var before = TotalBytes();
            Trim(0);
            return Math.Max(0, before - TotalBytes());
        }

        /// <summary>
        /// Deletes least recently used entries until the cache fits its cap (or <paramref name="targetBytes"/>), plus
        /// leftovers (a bundle file no record names, a partial download or temporary record over an hour old). Never an
        /// entry a load holds, and never a file whose bundle may be loaded. Any thread.
        /// </summary>
        internal static void Trim() => Trim(capacity);

        internal static void Trim(long targetBytes)
        {
            try
            {
                if (root == null || !Directory.Exists(root)) return;
                var entries = new List<Entry>();
                foreach (var path in Directory.GetFiles(root, "*.entry"))
                {
                    var entry = Read(System.IO.Path.GetFileNameWithoutExtension(path));
                    if (entry != null) entries.Add(entry);
                }
                long total = 0;
                foreach (var path in Directory.GetFiles(root))
                {
                    var name = System.IO.Path.GetFileName(path);
                    var dot = name.IndexOf('.');
                    if (dot <= 0) continue;
                    var key = name.Substring(0, dot);
                    var isBundle = name.EndsWith(".bundle", StringComparison.Ordinal);
                    var held = TryHold(key);
                    if (held == null)
                    {
                        if (isBundle) total += SizeOf(path);
                        continue;
                    }
                    using (held)
                    {
                        bool leftover;
                        if (isBundle) leftover = !IsInUse(name) && Read(key)?.FileName != name;
                        else if (name.EndsWith(".entry", StringComparison.Ordinal)) leftover = Read(key) == null;
                        else leftover = DateTime.UtcNow - File.GetLastWriteTimeUtc(path) > TimeSpan.FromHours(1); // .part, .tmp
                        if (leftover) TryDelete(path);
                        else if (isBundle) total += SizeOf(path);
                    }
                }
                if (total <= targetBytes) return;
                entries.Sort((a, b) => a.Used.CompareTo(b.Used));
                foreach (var entry in entries)
                {
                    if (total <= targetBytes) break;
                    if (IsInUse(entry.FileName)) continue;
                    var held = TryHold(entry.Key);
                    if (held == null) continue;
                    using (held)
                    {
                        var current = Read(entry.Key); // as it is now that this holds it
                        if (current == null || current.FileName != entry.FileName || IsInUse(current.FileName)) continue;
                        Remove(current);
                        if (!File.Exists(current.BundlePath)) total -= current.Length;
                    }
                }
            }
            catch (Exception)
            {
                // a cache that cannot be trimmed only costs disk until the next trim
            }
        }

        static long SizeOf(string path)
        {
            try
            {
                return new FileInfo(path).Length;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        static Entry Read(string key)
        {
            if (root == null || !IsKey(key)) return null;
            var entryPath = Path(key + ".entry");
            try
            {
                if (!File.Exists(entryPath)) return null;
                var entry = new Entry { Key = key, EntryPath = entryPath };
                foreach (var line in File.ReadAllLines(entryPath, Encoding.UTF8))
                {
                    var at = line.IndexOf('=');
                    if (at <= 0) continue;
                    var name = line.Substring(0, at);
                    var value = line.Substring(at + 1);
                    switch (name)
                    {
                        case "file": entry.FileName = value; break;
                        case "url": entry.UrlHash = value; break;
                        case "length": entry.Length = long.Parse(value); break;
                        case "written": entry.Written = long.Parse(value); break;
                        case "nonce": entry.Nonce = value; break;
                        case "used": entry.Used = long.Parse(value); break;
                        case "versions": entry.VerdictVersions = value.Length > 0 ? value : null; break;
                        case "passed": entry.Passed = value == "1"; break;
                        case "reason": entry.Reason = value; break;
                    }
                }
                // The file must be the key's own, under the record's nonce: a record cannot point elsewhere.
                if (string.IsNullOrEmpty(entry.UrlHash) || !IsKey(entry.Nonce) || entry.FileName != key + "." + entry.Nonce + ".bundle") return null;
                entry.BundlePath = Path(entry.FileName);
                return entry;
            }
            catch (Exception)
            {
                return null; // a corrupt record: treated as no entry (the bundle is downloaded again)
            }
        }

        static void Write(Entry entry)
        {
            var text = new StringBuilder()
                .Append("file=").Append(entry.FileName).Append('\n')
                .Append("url=").Append(entry.UrlHash).Append('\n')
                .Append("length=").Append(entry.Length).Append('\n')
                .Append("written=").Append(entry.Written).Append('\n')
                .Append("nonce=").Append(entry.Nonce).Append('\n')
                .Append("used=").Append(entry.Used).Append('\n')
                .Append("versions=").Append(OneLine(entry.VerdictVersions)).Append('\n')
                .Append("passed=").Append(entry.Passed ? "1" : "0").Append('\n')
                .Append("reason=").Append(OneLine(entry.Reason)).Append('\n');
            try
            {
                var temporary = entry.EntryPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(temporary, text.ToString(), new UTF8Encoding(false));
                if (File.Exists(entry.EntryPath)) File.Delete(entry.EntryPath);
                File.Move(temporary, entry.EntryPath);
            }
            catch (Exception)
            {
                // an entry record that cannot be written makes the entry be downloaded again next time
            }
        }

        static string OneLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var clean = new StringBuilder(Math.Min(text.Length, 200));
            foreach (var ch in text)
            {
                if (clean.Length >= 200) break;
                clean.Append(char.IsControl(ch) ? ' ' : ch);
            }
            return clean.ToString();
        }

        static bool IsKey(string key)
        {
            if (string.IsNullOrEmpty(key) || key.Length > 64) return false;
            foreach (var ch in key)
            {
                if (!(ch >= '0' && ch <= '9' || ch >= 'a' && ch <= 'z' || ch >= 'A' && ch <= 'Z')) return false;
            }
            return true;
        }
    }

    /// <summary>Bundle bytes read from a file on demand (the checker touches ~1 MiB of an LZ4 world).</summary>
    internal sealed class FileBundleSource : BundleSource, IDisposable
    {
        readonly FileStream stream;
        readonly long length;

        internal FileBundleSource(string path)
        {
            stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.RandomAccess);
            length = stream.Length;
        }

        internal override long Length => length;

        internal override void Read(long position, byte[] buffer, int offset, int count)
        {
            if (position < 0 || count < 0 || position > length - count) throw new BundleRefusedException(BundleRule.Malformed, "read past the end of the bundle");
            lock (stream)
            {
                stream.Position = position;
                var done = 0;
                while (done < count)
                {
                    var read = stream.Read(buffer, offset + done, count - done);
                    if (read <= 0) throw new BundleRefusedException(BundleRule.Malformed, "bundle file shorter than expected");
                    done += read;
                }
            }
        }

        public void Dispose() => stream.Dispose();
    }
}
