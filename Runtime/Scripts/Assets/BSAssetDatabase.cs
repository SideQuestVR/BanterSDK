using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BS
{
    /// <summary>What the page and C# listeners hear about a database's assets (<see cref="BSAssetRegistry"/>).</summary>
    internal interface IBSAssetListener
    {
        void AssetRegistered(BSAssetEntry entry);
        void AssetLoaded(BSAssetEntry entry);
        void AssetUpdated(BSAssetEntry entry);
        /// <param name="tellPage">False once the world has gone: its page went with it.</param>
        void AssetRemoved(BSAssetEntry entry, bool tellPage);
    }

    internal sealed class BSAssetEntry
    {
        internal readonly string Key;
        internal AssetMetadata Meta;
        internal UnityEngine.Object Asset;
        /// <summary>Loads through <c>BS.Assets</c> that the page hasn't released.</summary>
        internal int PageRefs;
        /// <summary>Made by a load: joins the unused list at zero references. Others stay until removed, or their world goes.</summary>
        internal bool Cached;
        /// <summary>Runs as the entry goes: destroys what only it owns, or lets go of a shared cache's copy.</summary>
        internal Action Dispose;
        internal LinkedListNode<BSAssetEntry> UnusedNode;

        internal BSAssetEntry(string key) => Key = key;
    }

    /// <summary>What a key's source hands the database once it has loaded.</summary>
    internal struct BSLoadedAsset
    {
        internal UnityEngine.Object Asset;
        internal AssetType Type;
        internal string Url;
        internal long Size;
        internal Action Dispose;
    }

    /// <summary>A load refused while memory is low. The message is what the page reads.</summary>
    internal sealed class BSAssetLimitException : Exception
    {
        internal BSAssetLimitException(string message) : base(message) { }
    }

    /// <summary>
    /// One world's assets: what is loaded, under which key, who uses it. Unused assets wait in a bounded list, oldest out
    /// first; everything goes with the world (<see cref="ReleaseAll"/>). Main thread.
    /// </summary>
    internal sealed class BSAssetDatabase
    {
        internal const int MaxUnusedEntries = 64;
        internal const long MaxUnusedBytes = 128L << 20;

        /// <summary>Most assets in one world before scripts are refused more. A world's own never are.</summary>
        internal const int MaxScriptEntries = 1024;
        /// <summary>Most bytes of downloaded assets one world holds. An estimate, as every size here is.</summary>
        internal const long MaxDownloadBytes = 512L << 20;
        /// <summary>Downloads running at once in one world. The rest wait their turn.</summary>
        internal const int MaxDownloads = 6;
        /// <summary>Downloads that may wait. Past this a script hears it is asking too fast.</summary>
        internal const int MaxWaitingDownloads = 256;

        internal const string MemoryLow = "LIMIT: memory is low, try again later";
        internal const string WorldFull = "LIMIT: this world holds as many script assets as it may";
        internal const string TooManyWaiting = "LIMIT: too many downloads are waiting, try again later";
        const string WorldChanged = "the world has changed";

        static int nextId;

        readonly Dictionary<string, BSAssetEntry> entries = new Dictionary<string, BSAssetEntry>();
        // A loaded asset's asset_ id, which is what the page and components name it by, to its key.
        readonly Dictionary<string, string> loadedIds = new Dictionary<string, string>();
        readonly Dictionary<string, HashSet<string>> references = new Dictionary<string, HashSet<string>>();
        readonly Dictionary<string, Task<BSAssetEntry>> loading = new Dictionary<string, Task<BSAssetEntry>>();
        readonly LinkedList<BSAssetEntry> unused = new LinkedList<BSAssetEntry>();
        readonly Queue<TaskCompletionSource<bool>> waitingDownloads = new Queue<TaskCompletionSource<bool>>();
        readonly IBSAssetListener listener;
        readonly BSAssetMemoryPolicy memory;
        long unusedBytes;
        long downloadedBytes;
        int activeDownloads;
        bool retired;

        internal BSAssetDatabase(IBSAssetListener listener, BSAssetMemoryPolicy memory)
        {
            this.listener = listener;
            this.memory = memory;
        }

        internal IEnumerable<BSAssetEntry> Entries => entries.Values;

        /// <summary>Entries the database has let go of since the last call: what a reclaim of unused assets would find.</summary>
        internal int TakeEvicted()
        {
            var count = evicted;
            evicted = 0;
            return count;
        }
        int evicted;

        // ------------------------------------------------------------------------------------------------- the table

        string KeyOf(string idOrKey)
        {
            var key = BSAssetKey.Canonical(idOrKey);
            return loadedIds.TryGetValue(key, out var loaded) ? loaded : key;
        }

        internal BSAssetEntry Find(string idOrKey)
        {
            if (string.IsNullOrEmpty(idOrKey) || !entries.TryGetValue(KeyOf(idOrKey), out var entry)) return null;
            if (entry.UnusedNode != null)
            {
                unused.Remove(entry.UnusedNode);
                unused.AddLast(entry.UnusedNode);
            }
            return entry;
        }

        internal BSAssetEntry Store(string id, UnityEngine.Object asset, AssetType type, string url, string tag, long size) =>
            Add(BSAssetKey.Canonical(id), id, asset, type, url, tag, size);

        BSAssetEntry Add(string key, string id, UnityEngine.Object asset, AssetType type, string url, string tag, long size)
        {
            var entry = new BSAssetEntry(key)
            {
                Asset = asset,
                Meta = new AssetMetadata { id = id, type = type, url = url, loaded = false, memorySize = size, createdAt = DateTime.Now, tag = tag },
            };
            entries[key] = entry;
            if (IsDownload(entry)) downloadedBytes += size;
            listener.AssetRegistered(entry);
            return entry;
        }

        static bool IsDownload(BSAssetEntry entry) => !string.IsNullOrEmpty(entry.Meta.url);

        /// <summary>Replaces the object behind an existing entry.</summary>
        internal void Replace(BSAssetEntry entry, UnityEngine.Object asset, long size)
        {
            entry.Asset = asset;
            var growth = size - entry.Meta.memorySize;
            entry.Meta.memorySize = size;
            if (entry.UnusedNode != null) unusedBytes += growth;
            if (IsDownload(entry)) downloadedBytes += growth;
            listener.AssetUpdated(entry);
        }

        internal void MarkLoaded(BSAssetEntry entry)
        {
            entry.Meta.loaded = true;
            listener.AssetLoaded(entry);
        }

        internal void Remove(string idOrKey)
        {
            if (entries.TryGetValue(KeyOf(idOrKey), out var entry)) Drop(entry, true);
        }

        void Drop(BSAssetEntry entry, bool tellPage)
        {
            entries.Remove(entry.Key);
            references.Remove(entry.Key);
            loadedIds.Remove(entry.Meta.id);
            LeaveUnused(entry);
            if (IsDownload(entry)) downloadedBytes -= entry.Meta.memorySize;
            evicted++;
            entry.Dispose?.Invoke();
            listener.AssetRemoved(entry, tellPage);
        }

        // ---------------------------------------------------------------------------------------------- references

        internal void AddReference(string idOrKey, string owner)
        {
            var key = KeyOf(idOrKey);
            if (!references.TryGetValue(key, out var owners)) references[key] = owners = new HashSet<string>();
            owners.Add(owner);
            if (entries.TryGetValue(key, out var entry)) Settle(entry);
        }

        internal void RemoveReference(string idOrKey, string owner)
        {
            var key = KeyOf(idOrKey);
            if (references.TryGetValue(key, out var owners)) owners.Remove(owner);
            if (entries.TryGetValue(key, out var entry)) Settle(entry);
        }

        /// <summary>Bindings and the page's unreleased loads.</summary>
        internal int ReferenceCount(string idOrKey)
        {
            var key = KeyOf(idOrKey);
            var owners = references.TryGetValue(key, out var set) ? set.Count : 0;
            return owners + (entries.TryGetValue(key, out var entry) ? entry.PageRefs : 0);
        }

        /// <summary>The page is done with one load of this asset. False when it holds none.</summary>
        internal bool Release(string idOrKey)
        {
            if (!entries.TryGetValue(KeyOf(idOrKey), out var entry) || entry.PageRefs == 0) return false;
            entry.PageRefs--;
            Settle(entry);
            return true;
        }

        void Settle(BSAssetEntry entry)
        {
            if (!entry.Cached) return;
            if (ReferenceCount(entry.Key) > 0)
            {
                LeaveUnused(entry);
                return;
            }
            if (entry.UnusedNode != null) return;
            entry.UnusedNode = unused.AddLast(entry);
            unusedBytes += entry.Meta.memorySize;
            while (unused.Count > MaxUnusedEntries || unusedBytes > MaxUnusedBytes) Drop(unused.First.Value, true);
        }

        void LeaveUnused(BSAssetEntry entry)
        {
            if (entry.UnusedNode == null) return;
            unused.Remove(entry.UnusedNode);
            entry.UnusedNode = null;
            unusedBytes -= entry.Meta.memorySize;
        }

        // -------------------------------------------------------------------------------------------------- loading

        /// <summary>
        /// The entry for <paramref name="key"/> with one more page reference: the one held, or what <paramref name="load"/>
        /// gives, which runs once however many ask. Throws <see cref="BSAssetLimitException"/> when memory is low, unless
        /// the load is <paramref name="critical"/>.
        /// </summary>
        internal async Task<BSAssetEntry> Acquire(BSAssetKey key, bool critical, Func<Task<BSLoadedAsset>> load)
        {
            if (retired) throw new InvalidOperationException(WorldChanged);
            // Its bundle unloaded under it, say: load it afresh.
            if (entries.TryGetValue(KeyOf(key.Text), out var entry) && entry.Asset == null)
            {
                Drop(entry, true);
                entry = null;
            }
            if (entry == null)
            {
                if (!loading.TryGetValue(key.Text, out var inFlight))
                {
                    loading[key.Text] = inFlight = Complete(key, critical, load);
                }
                try
                {
                    entry = await inFlight;
                }
                finally
                {
                    if (loading.TryGetValue(key.Text, out var current) && current == inFlight) loading.Remove(key.Text);
                }
            }
            entry.PageRefs++;
            Settle(entry);
            return entry;
        }

        async Task<BSAssetEntry> Complete(BSAssetKey key, bool critical, Func<Task<BSLoadedAsset>> load)
        {
            BSLoadedAsset loaded;
            if (critical)
            {
                Tick();
                loaded = await load();
            }
            else
            {
                await EnterDownload();
                try
                {
                    loaded = await load();
                }
                finally
                {
                    LeaveDownload();
                }
            }
            if (retired) throw new InvalidOperationException(WorldChanged);
            var id = "asset_" + key.Kind.ToString().ToLowerInvariant() + "_" + ++nextId;
            loadedIds[id] = key.Text;
            var entry = Add(key.Text, id, loaded.Asset, loaded.Type, loaded.Url, key.Text, loaded.Size);
            entry.Cached = true;
            entry.Dispose = loaded.Dispose;
            MarkLoaded(entry);
            return entry;
        }

        // --------------------------------------------------------------------------------------------------- memory

        /// <summary>
        /// Why a load may not start; null when it may. While memory is low, or the world is full, the unused list goes first,
        /// oldest out. A critical load always goes ahead.
        /// </summary>
        internal string WhyNot(bool critical)
        {
            Tick();
            if (critical) return null;
            if (memory.IsLow) return MemoryLow;
            while (IsFull && unused.First != null) Drop(unused.First.Value, true);
            return IsFull ? WorldFull : null;
        }

        bool IsFull => entries.Count >= MaxScriptEntries || downloadedBytes >= MaxDownloadBytes;

        internal bool MayLoad(bool critical) => WhyNot(critical) == null;

        /// <summary>
        /// Takes a place among this world's downloads, waiting for one if need be, then asks <see cref="WhyNot"/>: what held
        /// when a burst of loads began may not by the time its turn comes. Throws <see cref="BSAssetLimitException"/> when
        /// it's refused. Each success needs a <see cref="LeaveDownload"/>.
        /// </summary>
        internal async Task EnterDownload()
        {
            if (activeDownloads < MaxDownloads)
            {
                activeDownloads++;
            }
            else
            {
                if (waitingDownloads.Count >= MaxWaitingDownloads) throw new BSAssetLimitException(TooManyWaiting);
                var turn = new TaskCompletionSource<bool>();
                waitingDownloads.Enqueue(turn);
                await turn.Task; // the place comes from a LeaveDownload
            }
            var refusal = WhyNot(false);
            if (refusal == null) return;
            LeaveDownload();
            throw new BSAssetLimitException(refusal);
        }

        internal void LeaveDownload()
        {
            if (waitingDownloads.Count > 0) waitingDownloads.Dequeue().SetResult(true);
            else activeDownloads--;
        }

        /// <summary>Evicts what nothing uses while memory is low.</summary>
        internal void Tick()
        {
            if (!memory.Refresh()) return;
            while (unused.First != null) Drop(unused.First.Value, true);
        }

        // ------------------------------------------------------------------------------------------------ world end

        /// <summary>A browser's texture is its browser's, not its world's: it stays until the browser unregisters it.</summary>
        internal void TakeBrowsers(BSAssetDatabase from)
        {
            foreach (var entry in from.entries.Values.Where(e => BSAssetKey.TryParse(e.Key, out var key) && key.Kind == BSAssetKeyKind.Browser).ToList())
            {
                from.entries.Remove(entry.Key);
                entries[entry.Key] = entry;
                if (from.references.TryGetValue(entry.Key, out var owners))
                {
                    from.references.Remove(entry.Key);
                    references[entry.Key] = owners;
                }
            }
        }

        /// <summary>Its world has been left: whatever is still loading is dropped when it lands, and nothing starts.</summary>
        internal void Retire()
        {
            retired = true;
            while (waitingDownloads.Count > 0) waitingDownloads.Dequeue().SetException(new InvalidOperationException(WorldChanged));
        }

        internal bool Retired => retired;

        /// <summary>Lets everything go. For after the world's scene has gone. One entry's failure doesn't stop the rest.</summary>
        internal void ReleaseAll()
        {
            foreach (var entry in entries.Values.ToList())
            {
                try
                {
                    Drop(entry, false);
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogWarning("[BSAssetDatabase] Couldn't release " + entry.Key + ": " + e.Message);
                }
            }
            references.Clear();
        }
    }
}
