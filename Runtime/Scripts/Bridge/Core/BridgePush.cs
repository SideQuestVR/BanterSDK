using System;
using System.Collections.Generic;
using System.Threading;

namespace BS.Bridge
{
    /// <summary>
    /// Scheduler work queued by one document's ops that has not started yet: BridgeContext.Bind counts what is queued
    /// while the document's op runs (and what that work queues in turn), and releases each item as it starts. The
    /// fence family holds a CALL while this is above zero, and DELTA's applied count only moves when it is zero.
    /// Any thread adds; items start, and so release, on the main thread.
    /// </summary>
    internal sealed class BridgeFence
    {
        int pending;
        long counted;
        int emptied;

        /// <summary>The count has just reached zero (main thread, from the work that started).</summary>
        public Action Drained;
        /// <summary>Since when (the view's clock) the count has not been zero; -1: it has been zero since the last look. Main thread.</summary>
        public long BusySinceMs = -1;
        /// <summary>The value of Emptied at the last look. Main thread.</summary>
        public int EmptiedSeen;
        /// <summary>Not empty for longer than the view allows: the fence holds nothing back until it is empty again. Main thread.</summary>
        public bool Expired;

        public int Pending => Volatile.Read(ref pending);
        public long Counted => Interlocked.Read(ref counted);
        /// <summary>How many times the count has reached zero: tells a fence that keeps emptying from one that never does.</summary>
        public int Emptied => Volatile.Read(ref emptied);

        public void Add()
        {
            Interlocked.Increment(ref pending);
            Interlocked.Increment(ref counted);
        }

        public void Release()
        {
            if (Interlocked.Decrement(ref pending) != 0) return;
            Interlocked.Increment(ref emptied);
            Drained?.Invoke();
        }

        /// <summary>The view's once-a-frame look: true when it has just expired. Main thread.</summary>
        public bool Check(long nowMs, int maxBusyMs)
        {
            int e = Emptied;
            if (Pending == 0 || e != EmptiedSeen)
            {
                EmptiedSeen = e;
                BusySinceMs = Pending == 0 ? -1 : nowMs;
                Expired = false;
                return false;
            }
            if (BusySinceMs < 0) { BusySinceMs = nowMs; return false; }
            if (Expired || nowMs - BusySinceMs < maxBusyMs) return false;
            Expired = true;
            return true;
        }
    }

    /// <summary>One DELTA entry. Cid 0 is the object's transform; the value is the property's text as "!u!" or TRANSFORM_UPDATED carry it.</summary>
    internal readonly struct DeltaEntry
    {
        public readonly int Oid, Cid, ComponentType, Prop;
        public readonly string Text;
        public DeltaEntry(int oid, int cid, int componentType, int prop, string text)
        {
            Oid = oid; Cid = cid; ComponentType = componentType; Prop = prop; Text = text ?? string.Empty;
        }
    }

    /// <summary>One ANNOUNCE entry: an object (Kind 1) or a component (Kind 2, Cid and ComponentType set).</summary>
    internal readonly struct AnnounceEntry
    {
        public readonly byte Kind;
        public readonly int Oid, Cid, ComponentType, Layer, Parent;
        public readonly bool Active;
        public readonly string Name, Tag;

        public AnnounceEntry(int oid, bool active, string name, int layer, string tag, int parent)
        {
            Kind = BridgePush.AnnounceObject; Oid = oid; Active = active; Name = name ?? string.Empty; Layer = layer;
            Tag = tag ?? string.Empty; Parent = parent; Cid = 0; ComponentType = 0;
        }

        public AnnounceEntry(int oid, int cid, int componentType)
        {
            Kind = BridgePush.AnnounceComponent; Oid = oid; Cid = cid; ComponentType = componentType;
            Active = false; Name = string.Empty; Layer = 0; Tag = string.Empty; Parent = 0;
        }
    }

    /// <summary>
    /// The pushed families' payloads (Unity to page) and ACCEPT (page to Unity). Mirror of Injection~/src/bridge/push.ts;
    /// Bridge~/vectors/bridge_push_v1_vectors.json pins both.
    /// <list type="bullet">
    /// <item>ACCEPT: u32 families.</item>
    /// <item>ANNOUNCE: varint n · n × (u8 kind · kind 1: varint oid · u8 active · str name · varint layer · str tag · varint parent | kind 2: varint oid · varint cid · varint componentType).</item>
    /// <item>DELTA: varint applied · varint n · n × (varint oid · varint cid · varint componentType · varint prop · str value).</item>
    /// <item>DESTROYED: varint n · n × (varint handle · u8 reason).</item>
    /// </list>
    /// Ids are the page's unityIds (positive); parent 0 is the world root. Strings are UTF-8, at most MaxText bytes.
    /// </summary>
    internal static class BridgePush
    {
        public const byte AnnounceObject = 1;
        public const byte AnnounceComponent = 2;
        /// <summary>Longest string in a push, in UTF-8 bytes.</summary>
        public const int MaxText = 64 * 1024;
        /// <summary>Names and tags are cut to this many UTF-16 units, which always fits MaxText.</summary>
        public const int MaxNameChars = 16 * 1024;

        public static byte[] EncodeAccept(uint families) => BridgeOps.EncodeU32(families);

        /// <summary>The families, or -1.</summary>
        public static long DecodeAccept(ReadOnlySpan<byte> p) => BridgeOps.DecodeU32(p);

        static uint Id(int v) => v < 0 ? 0u : (uint)v;

        internal static string Clip(string s, int max)
        {
            if (s == null) return string.Empty;
            if (s.Length <= max) return s;
            // Never split a surrogate pair.
            int n = char.IsHighSurrogate(s[max - 1]) ? max - 1 : max;
            return s.Substring(0, n);
        }

        public static void WriteDelta(ByteWriter w, in DeltaEntry e)
        {
            w.Varint(Id(e.Oid)); w.Varint(Id(e.Cid)); w.Varint(Id(e.ComponentType)); w.Varint(Id(e.Prop));
            w.Str(Clip(e.Text, MaxNameChars));
        }

        public static void WriteAnnounce(ByteWriter w, in AnnounceEntry e)
        {
            w.U8(e.Kind);
            w.Varint(Id(e.Oid));
            if (e.Kind == AnnounceObject)
            {
                w.U8(e.Active ? 1 : 0);
                w.Str(Clip(e.Name, MaxNameChars));
                w.Varint(Id(e.Layer));
                w.Str(Clip(e.Tag, MaxNameChars));
                w.Varint(Id(e.Parent));
            }
            else
            {
                w.Varint(Id(e.Cid));
                w.Varint(Id(e.ComponentType));
            }
        }

        public static void WriteDestroyed(ByteWriter w, int handle, byte reason)
        {
            w.Varint(Id(handle));
            w.U8(reason);
        }

        // ------------------------------------------------------------------------------------------------ decoders
        // Unity only encodes these; the decoders are the page's rules in C#, for the tests and the vectors.

        /// <summary>A count no larger than the bytes left could hold, or -1.</summary>
        static int Count(ref PayloadCursor c, int minEntryBytes)
        {
            uint n = c.Varint();
            if (c.Failed || (long)n * minEntryBytes > c.Remaining) return -1;
            return (int)n;
        }

        static int Positive(ref PayloadCursor c)
        {
            uint v = c.Varint();
            return v > int.MaxValue ? -1 : (int)v;
        }

        public static bool TryDecodeDelta(ReadOnlySpan<byte> p, out uint applied, out List<DeltaEntry> entries)
        {
            var c = new PayloadCursor(p);
            applied = c.Varint();
            entries = null;
            int n = Count(ref c, 5);
            if (n < 0) return false;
            entries = new List<DeltaEntry>(n);
            for (int i = 0; i < n; i++)
            {
                int oid = Positive(ref c), cid = Positive(ref c), ct = Positive(ref c), prop = Positive(ref c);
                string text = c.Str(MaxText);
                if (c.Failed || oid < 0 || cid < 0 || ct < 0 || prop < 0) return false;
                entries.Add(new DeltaEntry(oid, cid, ct, prop, text));
            }
            return c.Done;
        }

        public static bool TryDecodeAnnounce(ReadOnlySpan<byte> p, out List<AnnounceEntry> entries)
        {
            var c = new PayloadCursor(p);
            entries = null;
            int n = Count(ref c, 4);
            if (n < 0) return false;
            entries = new List<AnnounceEntry>(n);
            for (int i = 0; i < n; i++)
            {
                byte kind = c.U8();
                int oid = Positive(ref c);
                if (kind == AnnounceObject)
                {
                    byte active = c.U8();
                    string name = c.Str(MaxText);
                    int layer = Positive(ref c);
                    string tag = c.Str(MaxText);
                    int parent = Positive(ref c);
                    if (c.Failed || active > 1 || oid < 0 || layer < 0 || parent < 0) return false;
                    entries.Add(new AnnounceEntry(oid, active == 1, name, layer, tag, parent));
                }
                else if (kind == AnnounceComponent)
                {
                    int cid = Positive(ref c), ct = Positive(ref c);
                    if (c.Failed || oid < 0 || cid < 0 || ct < 0) return false;
                    entries.Add(new AnnounceEntry(oid, cid, ct));
                }
                else return false;
            }
            return c.Done;
        }

        public static bool TryDecodeDestroyed(ReadOnlySpan<byte> p, out List<(int handle, byte reason)> entries)
        {
            var c = new PayloadCursor(p);
            entries = null;
            int n = Count(ref c, 2);
            if (n < 0) return false;
            entries = new List<(int, byte)>(n);
            for (int i = 0; i < n; i++)
            {
                int h = Positive(ref c);
                byte reason = c.U8();
                if (c.Failed || h <= 0 || reason == 0) return false;
                entries.Add((h, reason));
            }
            return c.Done;
        }
    }

    /// <summary>
    /// Consecutive pushes of one family, merged for as long as nothing else is queued behind them in BridgeView's
    /// outbox, so each keeps its place among replies, events and strings. A DELTA keeps one entry per (object,
    /// component, property): the newest value, in the place of the first. Its values share one stamp, which is its
    /// applied count: a value read later, with a higher stamp, starts the next DELTA, so the page never drops it as
    /// older than its own write (<see cref="TakesStamp"/>). Guarded by BridgeView's outbox lock.
    /// </summary>
    internal sealed class BridgePushBatch
    {
        public readonly uint Family;
        public int Count => Family == BridgeFamily.Delta ? deltas.Count : count;
        /// <summary>DELTA: the lowest stamp among the values it keeps (0 for none).</summary>
        public uint Applied
        {
            get
            {
                uint low = uint.MaxValue;
                foreach (var s in stamps) if (s < low) low = s;
                return low == uint.MaxValue ? 0 : low;
            }
        }

        /// <summary>DELTA: whether a value read at <paramref name="stamp"/> belongs in this batch (it holds one stamp).</summary>
        public bool TakesStamp(uint stamp) => stamps == null || stamps.Count == 0 || stamps[0] == stamp;

        readonly List<DeltaEntry> deltas;
        readonly List<uint> stamps;
        readonly Dictionary<(int, int, int), int> slots;
        readonly ByteWriter body;
        int count;

        public BridgePushBatch(uint family)
        {
            Family = family;
            if (family == BridgeFamily.Delta)
            {
                deltas = new List<DeltaEntry>();
                stamps = new List<uint>();
                slots = new Dictionary<(int, int, int), int>();
            }
            else body = new ByteWriter(64);
        }

        public void AddDelta(in DeltaEntry e, uint stamp)
        {
            var key = (e.Oid, e.Cid, e.Prop);
            if (slots.TryGetValue(key, out int slot))
            {
                deltas[slot] = e;
                stamps[slot] = stamp;
            }
            else
            {
                slots.Add(key, deltas.Count);
                deltas.Add(e);
                stamps.Add(stamp);
            }
        }

        public void AddAnnounce(in AnnounceEntry e)
        {
            BridgePush.WriteAnnounce(body, e);
            count++;
        }

        public void AddDestroyed(int handle, byte reason)
        {
            BridgePush.WriteDestroyed(body, handle, reason);
            count++;
        }

        /// <summary>The whole op (opcode, length, payload).</summary>
        public byte[] Encode()
        {
            var w = new ByteWriter(64 + (body?.Length ?? deltas.Count * 24));
            if (Family == BridgeFamily.Delta)
            {
                w.Varint(Applied);
                w.Varint((uint)deltas.Count);
                foreach (var e in deltas) BridgePush.WriteDelta(w, e);
                return BridgeOps.Encode(BridgeOp.Delta, w.Span);
            }
            w.Varint((uint)count);
            w.Bytes(body.Span);
            return BridgeOps.Encode(Family == BridgeFamily.Announce ? (uint)BridgeOp.Announce : BridgeOp.Destroyed, w.Span);
        }
    }
}
