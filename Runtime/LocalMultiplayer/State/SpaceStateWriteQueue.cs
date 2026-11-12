// <mirror source="Assets/Systems/Networking/State/SpaceStateWriteQueue.cs" sha256="af855646688dbce10919e91ca4d785840478427ce88ce5aa9dcd7f205a39b726" mode="verbatim" />
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace BS.LocalMultiplayer.State
{
    /// <summary>What a queued write does to its path.</summary>
    public enum StateWriteKind
    {
        /// <summary>Plain leaf write, or a merge of an object into existing leaves.</summary>
        Set,
        /// <summary>Remove the path and every descendant.</summary>
        Delete,
        /// <summary>Replace: delete the subtree first, then set. See <see cref="SpaceStateWriteQueue"/>.</summary>
        Replace
    }

    /// <summary>One coalesced pending write. Immutable once taken for a batch.</summary>
    public struct PendingWrite
    {
        /// <summary>Already encoded and validated by <see cref="StatePathCodec"/>.</summary>
        public string Path;
        public JToken Value;
        public StateWriteKind Kind;
        public RoomStateScope Scope;
        public DateTime UpdatedUtc;
        /// <summary>Bumped on every update; the in-flight guard compares it on ack.</summary>
        public long Seq;
    }

    /// <summary>
    /// Coalescing outbound queue for room-state writes.
    ///
    /// Three properties earn their complexity:
    /// <list type="number">
    /// <item><b>Coalescing.</b> A page loop doing <c>SetPublicSpaceProps</c> per key emits one
    /// write per key. Room state shares a 20 ops/s, burst-40 token bucket with
    /// <c>app.object.*</c> (grabs, ownership), so an uncoalesced burst starves synced objects and
    /// makes grabs silently fail. Latest-value-wins per (scope, path), then one batch per flush,
    /// turns a 60-key burst into one token.</item>
    /// <item><b>Scope never mixes.</b> <c>scope</c> is a per-MESSAGE field, so public and protected
    /// writes cannot share a batch. Draining the OLDEST pending entry's scope stops either lane
    /// starving the other.</item>
    /// <item><b>Nothing is removed before the ack.</b> Each write carries a <see cref="PendingWrite.Seq"/>
    /// bumped on every update; on ack only entries whose Seq is unchanged are dropped. The previous
    /// bridge removed under the lock BEFORE awaiting, so a write arriving mid-flight was lost.</item>
    /// </list>
    ///
    /// Thread-safe for enqueue: SDK space-state events are delivered on the browser pipe's
    /// dispatch path and <c>BSLink.ParseCommand</c> is <c>async void</c>, so ordering after an
    /// await is not guaranteed. Everything here is lock-guarded and free of Unity API calls.
    /// </summary>
    public sealed class SpaceStateWriteQueue
    {
        /// <summary>Server allows 128; 64 keeps a frame small while still amortising the token.</summary>
        public const int MaxOpsPerBatch = 64;

        /// <summary>Writes queued this long while disconnected are dropped rather than replayed stale.</summary>
        public const double MaxPendingAgeSeconds = 60;

        private readonly object _gate = new object();
        private readonly Dictionary<string, PendingWrite> _pending = new Dictionary<string, PendingWrite>(StringComparer.Ordinal);
        private readonly List<string> _order = new List<string>();
        private long _seq;

        public int Count
        {
            get { lock (_gate) return _pending.Count; }
        }

        private static string IdOf(RoomStateScope scope, string path) =>
            (scope == RoomStateScope.Protected ? "x:" : "p:") + path;

        /// <summary>
        /// Queue a write, replacing any pending write for the same (scope, path) while keeping its
        /// original queue position. Safe to call from any thread.
        /// </summary>
        public void Enqueue(string path, JToken value, StateWriteKind kind, RoomStateScope scope, DateTime utcNow)
        {
            if (string.IsNullOrEmpty(path)) return;
            string id = IdOf(scope, path);
            lock (_gate)
            {
                if (!_pending.ContainsKey(id)) _order.Add(id);
                _pending[id] = new PendingWrite
                {
                    Path = path,
                    Value = value,
                    Kind = kind,
                    Scope = scope,
                    UpdatedUtc = utcNow,
                    Seq = ++_seq
                };
            }
        }

        /// <summary>
        /// Take up to <paramref name="maxOps"/> op-slots worth of writes from the oldest pending
        /// scope, WITHOUT removing them. Entries stay pending until <see cref="Ack"/>.
        /// <paramref name="slotsFor"/> reports how many batch ops a write will occupy, so a
        /// Replace (delete+set) or a protected write (protect+set) is never split across batches.
        /// </summary>
        public List<PendingWrite> TakeBatch(int maxOps, Func<PendingWrite, int> slotsFor)
        {
            var taken = new List<PendingWrite>();
            lock (_gate)
            {
                if (_order.Count == 0) return taken;

                RoomStateScope scope = _pending[_order[0]].Scope;
                int used = 0;

                foreach (string id in _order)
                {
                    if (!_pending.TryGetValue(id, out var write)) continue;
                    if (write.Scope != scope) continue;

                    int slots = slotsFor(write);
                    if (used + slots > maxOps)
                    {
                        if (taken.Count == 0)
                        {
                            // A single write bigger than the whole batch would wedge the queue.
                            taken.Add(write);
                        }
                        break;
                    }

                    taken.Add(write);
                    used += slots;
                }
            }
            return taken;
        }

        /// <summary>
        /// Drop the given writes, but only those unchanged since they were taken — a write that
        /// arrived mid-flight keeps its newer value and is sent again.
        /// </summary>
        public void Ack(IReadOnlyList<PendingWrite> taken)
        {
            if (taken == null || taken.Count == 0) return;
            lock (_gate)
            {
                foreach (var write in taken)
                {
                    string id = IdOf(write.Scope, write.Path);
                    if (_pending.TryGetValue(id, out var current) && current.Seq == write.Seq)
                    {
                        _pending.Remove(id);
                        _order.Remove(id);
                    }
                }
            }
        }

        /// <summary>Ack a single write. Same unchanged-Seq rule as the batch overload.</summary>
        public void Ack(PendingWrite write) => Ack(new[] { write });

        /// <summary>Drop one write unconditionally — used when the server refuses it permanently.</summary>
        public void Drop(PendingWrite write)
        {
            lock (_gate)
            {
                string id = IdOf(write.Scope, write.Path);
                if (_pending.TryGetValue(id, out var current) && current.Seq == write.Seq)
                {
                    _pending.Remove(id);
                    _order.Remove(id);
                }
            }
        }

        /// <summary>
        /// Evict writes queued more than <see cref="MaxPendingAgeSeconds"/> ago. Only meaningful
        /// while disconnected — the governor always drains while connected.
        /// </summary>
        public List<PendingWrite> DropStale(DateTime utcNow)
        {
            var dropped = new List<PendingWrite>();
            lock (_gate)
            {
                for (int i = _order.Count - 1; i >= 0; i--)
                {
                    string id = _order[i];
                    if (!_pending.TryGetValue(id, out var write))
                    {
                        _order.RemoveAt(i);
                        continue;
                    }
                    if ((utcNow - write.UpdatedUtc).TotalSeconds > MaxPendingAgeSeconds)
                    {
                        dropped.Add(write);
                        _pending.Remove(id);
                        _order.RemoveAt(i);
                    }
                }
            }
            return dropped;
        }

        public void Clear()
        {
            lock (_gate)
            {
                _pending.Clear();
                _order.Clear();
            }
        }
    }
}
