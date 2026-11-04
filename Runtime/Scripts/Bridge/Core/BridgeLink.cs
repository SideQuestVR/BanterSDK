using System;
using System.Collections.Generic;

namespace BS.Bridge
{
    /// <summary>A unit of a delivered message: a legacy string, or a BSB op (payload in Buffer[Offset..+Length]).</summary>
    internal readonly struct BridgeItem
    {
        public readonly string Text;
        public readonly uint Opcode;
        public readonly byte[] Buffer;
        public readonly int Offset;
        public readonly int Length;

        public BridgeItem(string text) { Text = text; Opcode = BridgeOp.LegacyText; Buffer = null; Offset = 0; Length = 0; }
        public BridgeItem(uint opcode, byte[] buffer, int offset, int length) { Text = null; Opcode = opcode; Buffer = buffer; Offset = offset; Length = length; }

        public bool IsText => Text != null;
        public ReadOnlySpan<byte> Payload => new ReadOnlySpan<byte>(Buffer, Offset, Length);
        /// <summary>Memory it pins, in bytes (UTF-16 text counts 2 per unit).</summary>
        public long Cost => IsText ? 2L * Text.Length : Length;
    }

    /// <summary>An outbound entry: a legacy string, or a complete op envelope.</summary>
    internal readonly struct OutEntry
    {
        public readonly string Text;
        public readonly byte[] Op;
        public OutEntry(string text) { Text = text; Op = null; }
        public OutEntry(byte[] op) { Text = null; Op = op; }
        public bool IsText => Text != null;
        public long Cost => IsText ? 2L * Text.Length : Op.Length;
    }

    /// <summary>
    /// A frame ready to encode: header plus a text body (chars [TextStart, +Header.Length) of its message's entries,
    /// written straight into the carrier) or payload bytes.
    /// </summary>
    internal struct OutFrame
    {
        public FrameHeader Header;
        public List<OutEntry> TextSource;
        public TextCursor TextStart;
        public byte[] Bytes;
        public int Offset;
        public int Count;

        public string ToCarrier() => TextSource != null
            ? BridgeCarrier.EncodeText(Header, TextSource, TextStart)
            : BridgeCarrier.EncodeBinary(Header, Bytes, Offset, Count);
    }

    /// <summary>A view of one frame's payload: text (Text[Offset..+Length]) or bytes (Bytes[Offset..+Length]).</summary>
    internal struct Segment
    {
        public string Text;
        public byte[] Bytes;
        public int Offset;
        public int Length;
        public bool IsText => Text != null;
        /// <summary>Bytes of memory: UTF-16 text counts 2 per unit.</summary>
        public long Cost => IsText ? 2L * Length : Length;
    }

    /// <summary>
    /// Fragment reassembly without copies: the fragments stay views into their carrier strings / decoded frames until
    /// the message is parsed, which copies each entry once. Capped per message and, optionally, by a budget shared by
    /// every session (both in bytes; text counts 2 per UTF-16 unit).
    /// </summary>
    internal sealed class BridgeReassembler
    {
        internal sealed class Budget
        {
            public long Used;
            public readonly long Cap;
            public Budget(long cap) { Cap = cap; }
        }

        readonly long maxMessage;
        readonly Budget budget;
        readonly List<Segment> parts = new List<Segment>();
        bool active, text;
        long total, charged;

        public BridgeReassembler(long maxMessage = BridgeWire.MaxMessage, Budget budget = null) { this.maxMessage = maxMessage; this.budget = budget; }

        public bool Busy => active;
        /// <summary>The completed message's fragments (valid until the next Push or Clear).</summary>
        public List<Segment> Parts => parts;

        /// <summary>Feeds one fragment. complete=true leaves the message in <see cref="Parts"/>; call Clear after use.</summary>
        public BridgeError Push(ushort flags, Segment seg, out bool complete)
        {
            complete = false;
            bool first = (flags & FrameFlags.FragFirst) != 0, last = (flags & FrameFlags.FragLast) != 0;
            if (first && last)
            {
                if (active) return BridgeError.FragOrder;
                parts.Clear();
                parts.Add(seg);
                complete = true;
                return BridgeError.None;
            }
            if (first)
            {
                if (active) return BridgeError.FragOrder;
                parts.Clear();
                active = true; text = seg.IsText; total = 0;
            }
            else if (!active || text != seg.IsText) return BridgeError.FragOrder;
            total += seg.Cost;
            if (total > maxMessage) return BridgeError.ReassemblyLimit;
            if (budget != null)
            {
                if (budget.Used + seg.Cost > budget.Cap) return BridgeError.ReassemblyLimit;
                budget.Used += seg.Cost;
                charged += seg.Cost;
            }
            parts.Add(seg);
            if (!last) return BridgeError.None;
            active = false;
            complete = true;
            return BridgeError.None;
        }

        /// <summary>Releases a completed message (and its budget).</summary>
        public void Clear()
        {
            if (budget != null) budget.Used -= charged;
            charged = 0;
            parts.Clear();
        }

        /// <summary>Abandons a message in progress (and releases its budget).</summary>
        public void Reset()
        {
            active = false;
            total = 0;
            Clear();
        }
    }

    /// <summary>
    /// One session's transport state, both directions: DATA seq numbering, cumulative acks, the credit window, batching
    /// queued entries into frames (the frames are the batching: no timer), fragmentation, an optional reorder buffer and
    /// reassembly. Mirror of link.ts; the sender is byte-identical (vector "sequences").
    /// Credits: a frame costs HeaderBytes + its payload in bytes (UTF-16 text counts 2 per unit); at most SendWindow may
    /// be unacknowledged, except that one frame may always go when nothing is in flight.
    /// Acks mean "consumed": a fragment once it is in the reassembler, a message's last fragment only once the message
    /// parsed. So an ack never covers a frame the receiver could not use.
    /// </summary>
    internal sealed class BridgeLink
    {
        struct Pending { public ushort Flags; public List<OutEntry> TextSource; public TextCursor TextStart; public byte[] Bytes; public int Offset; public int Count; public int Length; public long Cost; public List<OutEntry> Entries; }
        struct Inflight { public uint Seq; public int Cost; public List<OutEntry> Entries; }
        struct Unacked { public uint Seq; public int Cost; }

        public uint Epoch;
        public int FragmentSize;
        // send side
        public uint NextSeq = 1;
        public uint LastAckReceived;
        public int SendWindow;
        public int InflightBytes;
        public bool Blocked;
        /// <summary>Queued and built-but-unsent entries, in bytes (text 2 per unit): what a stalled peer pins.</summary>
        public long QueuedCost;
        readonly Queue<Inflight> inflight = new Queue<Inflight>();
        readonly Queue<OutEntry> queue = new Queue<OutEntry>();
        readonly Queue<Pending> pending = new Queue<Pending>();
        // receive side
        public uint LastSeqReceived;
        /// <summary>Last seq whose content was accepted (a fragment held for reassembly, or a parsed message).</summary>
        public uint ConsumedSeq;
        /// <summary>Seq of the last fragment of the last message fully received and parsed.</summary>
        public uint LastCompleteSeq;
        public uint AckSeq;
        public bool AckFrozen;
        public uint LastAckSent;
        public int RecvWindow;
        public int UnackedBytes;
        readonly Queue<Unacked> unacked = new Queue<Unacked>();
        readonly Dictionary<uint, (FrameHeader h, Segment seg)> reorder = new Dictionary<uint, (FrameHeader, Segment)>();
        /// <summary>Out-of-order frames kept. 0 on the carrier (Ora is FIFO): any gap is a protocol error.</summary>
        public int ReorderFrames;
        public readonly BridgeReassembler Reassembler;
        public int Duplicates;
        uint messageFirstSeq;

        public BridgeLink(int fragmentSize = BridgeWire.FragmentSize, int recvWindow = BridgeWire.DefaultWindow,
                          int sendWindow = BridgeWire.DefaultWindow, BridgeReassembler.Budget budget = null, int reorderFrames = 0)
        {
            FragmentSize = fragmentSize;
            RecvWindow = recvWindow;
            SendWindow = sendWindow;
            ReorderFrames = reorderFrames;
            Reassembler = new BridgeReassembler(BridgeWire.MaxMessage, budget);
        }

        /// <summary>What a frame costs against the window: header + payload bytes (text 2 per unit).</summary>
        public static int FrameCost(in FrameHeader h) => BridgeWire.HeaderBytes + (h.IsText ? 2 * h.Length : h.Length);

        // ---------------------------------------------------------------------------------------------- sending

        public void EnqueueText(string text) { var e = new OutEntry(text); queue.Enqueue(e); QueuedCost += e.Cost; }
        public void EnqueueOp(byte[] op) { var e = new OutEntry(op); queue.Enqueue(e); QueuedCost += e.Cost; }
        public bool HasOutbound => queue.Count > 0 || pending.Count > 0;
        public int QueuedCount => queue.Count + pending.Count;

        bool BuildNext()
        {
            if (queue.Count == 0) return false;
            int size = FragmentSize;
            var entries = new List<OutEntry>();
            if (queue.Peek().IsText)
            {
                int total = 0;
                while (queue.Count > 0 && queue.Peek().IsText)
                {
                    int es = BridgeCarrier.EntrySize(queue.Peek().Text);
                    if (entries.Count > 0 && total + es > size) break;
                    var e = queue.Dequeue();
                    QueuedCost -= e.Cost;
                    entries.Add(e);
                    total += es;
                    if (total >= size) break;
                }
                // The body (the entries' "<len>:<text>" forms) is never built: each fragment is a range of it, written
                // straight into its carrier. Cut like BridgeCarrier.SplitText: at most size units, never between the
                // halves of a surrogate pair.
                var cursor = new TextCursor();
                for (int pos = 0, i = 0; pos < total; i++)
                {
                    int cut = Math.Min(pos + size, total);
                    if (cut < total && cut - pos > 1)
                    {
                        var before = TextBody.Advance(entries, cursor, cut - pos - 1);
                        if (char.IsHighSurrogate(TextBody.CharAt(entries, before)) && char.IsLowSurrogate(TextBody.CharAt(entries, TextBody.Advance(entries, before, 1)))) cut--;
                    }
                    int len = cut - pos;
                    bool last = cut == total;
                    var p = new Pending
                    {
                        Flags = (ushort)((i == 0 ? FrameFlags.FragFirst : 0) | (last ? FrameFlags.FragLast : 0)),
                        TextSource = entries, TextStart = cursor, Length = len, Cost = 2L * len, Entries = last ? entries : null,
                    };
                    pending.Enqueue(p);
                    QueuedCost += p.Cost;
                    cursor = TextBody.Advance(entries, cursor, len);
                    pos = cut;
                }
            }
            else
            {
                int total = 0;
                while (queue.Count > 0 && !queue.Peek().IsText)
                {
                    int n = queue.Peek().Op.Length;
                    if (entries.Count > 0 && total + n > size) break;
                    var e = queue.Dequeue();
                    QueuedCost -= e.Cost;
                    entries.Add(e);
                    total += n;
                    if (total >= size) break;
                }
                var body = new byte[total];
                int o = 0;
                foreach (var e in entries) { System.Buffer.BlockCopy(e.Op, 0, body, o, e.Op.Length); o += e.Op.Length; }
                int count = Math.Max(1, (body.Length + size - 1) / size);
                for (int i = 0; i < count; i++)
                {
                    int start = i * size, len = Math.Min(body.Length, (i + 1) * size) - start;
                    var p = new Pending
                    {
                        Flags = (ushort)((i == 0 ? FrameFlags.FragFirst : 0) | (i == count - 1 ? FrameFlags.FragLast : 0)),
                        Bytes = body, Offset = start, Count = len, Length = len, Cost = len, Entries = i == count - 1 ? entries : null,
                    };
                    pending.Enqueue(p);
                    QueuedCost += p.Cost;
                }
            }
            return true;
        }

        /// <summary>
        /// Frames that may go now, in order, within the credit window and the caller's bounds. maxBytes counts payload
        /// (text 2 per unit), not headers, so whole fragments fill it; the first frame always passes it, so a burst
        /// splits across calls instead of stalling.
        /// </summary>
        public int TakeFrames(List<OutFrame> into, int maxBytes = int.MaxValue, int maxFrames = int.MaxValue)
        {
            int n = 0;
            long payload = 0;
            Blocked = false;
            while (n < maxFrames)
            {
                if (pending.Count == 0 && !BuildNext()) break;
                var p = pending.Peek();
                int cost = BridgeWire.HeaderBytes + (int)p.Cost;
                if (InflightBytes > 0 && (long)InflightBytes + cost > SendWindow) { Blocked = true; break; }
                if (n > 0 && payload + p.Cost > maxBytes) break;
                pending.Dequeue();
                QueuedCost -= p.Cost;
                uint seq = NextSeq++;
                inflight.Enqueue(new Inflight { Seq = seq, Cost = cost, Entries = p.Entries });
                InflightBytes += cost;
                into.Add(new OutFrame
                {
                    Header = new FrameHeader(FrameKind.Data, p.Flags, Epoch, seq, AckSeq, p.Length),
                    TextSource = p.TextSource, TextStart = p.TextStart, Bytes = p.Bytes, Offset = p.Offset, Count = p.Count,
                });
                payload += p.Cost;
                n++;
            }
            if (n > 0) NoteAckSent();
            return n;
        }

        /// <summary>The next frame's payload cost (text 2 per unit), building it if needed; -1 when nothing is queued.</summary>
        public long NextPayload()
        {
            if (pending.Count == 0 && !BuildNext()) return -1;
            return pending.Peek().Cost;
        }

        public FrameHeader ControlHeader(byte kind, int length = 0)
        {
            NoteAckSent();
            return new FrameHeader(kind, 0, Epoch, 0, AckSeq, length);
        }

        public bool NeedsAck(bool force = false)
        {
            if (AckSeq == LastAckSent) return false;
            return force || UnackedBytes >= RecvWindow / 4;
        }

        public void NoteAckSent()
        {
            LastAckSent = AckSeq;
            while (unacked.Count > 0 && unacked.Peek().Seq <= AckSeq) UnackedBytes -= unacked.Dequeue().Cost;
        }

        /// <summary>Applies the peer's cumulative ack. False = it acknowledges something never sent.</summary>
        public bool OnAck(uint ack)
        {
            if (ack > NextSeq - 1) return false;
            if (ack <= LastAckReceived) return true;
            LastAckReceived = ack;
            while (inflight.Count > 0 && inflight.Peek().Seq <= ack) InflightBytes -= inflight.Dequeue().Cost;
            return true;
        }

        /// <summary>Everything the peer has not consumed (after consumedSeq), in order, for re-sending another way.</summary>
        public List<OutEntry> TakeUnconsumed(uint consumedSeq)
        {
            var r = new List<OutEntry>();
            foreach (var f in inflight) if (f.Seq > consumedSeq && f.Entries != null) r.AddRange(f.Entries);
            foreach (var p in pending) if (p.Entries != null) r.AddRange(p.Entries);
            r.AddRange(queue);
            inflight.Clear(); pending.Clear(); queue.Clear();
            InflightBytes = 0;
            QueuedCost = 0;
            return r;
        }

        // -------------------------------------------------------------------------------------------- receiving

        /// <summary>
        /// One DATA frame, any arrival order. text: the payload is text[offset..+count] (count 0 = the whole string);
        /// else bytes[offset..+count]. Completed messages are appended to <paramref name="into"/>.
        /// </summary>
        public BridgeError ReceiveData(in FrameHeader h, string text, byte[] bytes, int offset, int count, List<BridgeMessage> into)
        {
            var seg = text != null
                ? new Segment { Text = text, Offset = offset, Length = count == 0 && offset == 0 ? text.Length : count }
                : new Segment { Bytes = bytes, Offset = offset, Length = count };
            return ReceiveData(h, seg, into);
        }

        public BridgeError ReceiveData(in FrameHeader h, Segment seg, List<BridgeMessage> into)
        {
            if (h.Seq <= LastSeqReceived) { Duplicates++; return BridgeError.None; }
            if (h.Seq != LastSeqReceived + 1)
            {
                if (reorder.ContainsKey(h.Seq)) { Duplicates++; return BridgeError.None; }
                if (reorder.Count >= ReorderFrames) return BridgeError.SeqGap;
                reorder[h.Seq] = (h, seg);
                return BridgeError.None;
            }
            var e = Consume(h, seg, into);
            while (e == BridgeError.None && reorder.TryGetValue(LastSeqReceived + 1, out var next))
            {
                reorder.Remove(LastSeqReceived + 1);
                e = Consume(next.h, next.seg, into);
            }
            return e;
        }

        BridgeError Consume(in FrameHeader h, Segment seg, List<BridgeMessage> into)
        {
            LastSeqReceived = h.Seq;
            int cost = FrameCost(h);
            unacked.Enqueue(new Unacked { Seq = h.Seq, Cost = cost });
            UnackedBytes += cost;
            if ((h.Flags & FrameFlags.FragFirst) != 0) messageFirstSeq = h.Seq;
            var e = Reassembler.Push((ushort)(h.Flags & FrameFlags.Complete), seg, out bool complete);
            if (e != BridgeError.None) return e;
            if (!complete)
            {
                Accept(h.Seq); // a fragment in the reassembler is consumed; its message's entries ride on the last one
                return BridgeError.None;
            }
            var m = new BridgeMessage { Seq = h.Seq, FirstSeq = messageFirstSeq, Cost = cost };
            e = BridgeMessage.Parse(Reassembler.Parts, m.Items);
            Reassembler.Clear();
            if (e != BridgeError.None) return e;
            LastCompleteSeq = h.Seq;
            Accept(h.Seq);
            into.Add(m);
            return BridgeError.None;
        }

        void Accept(uint seq)
        {
            ConsumedSeq = seq;
            if (!AckFrozen) AckSeq = seq;
        }

        /// <summary>Lets the advertised ack catch up (a hold released).</summary>
        public void UnfreezeAck() { AckFrozen = false; AckSeq = ConsumedSeq; }
    }

    /// <summary>A reassembled message: its first and last fragments' seqs and its items in order.</summary>
    internal sealed class BridgeMessage
    {
        public uint Seq;
        public uint FirstSeq;
        public int Cost;
        public readonly List<BridgeItem> Items = new List<BridgeItem>();

        [ThreadStatic] static List<RawOp> scratchOps;
        [ThreadStatic] static List<string> scratchTexts;

        /// <summary>Text entries, or BSB ops (LEGACY_TEXT decoded when it is valid UTF-8; otherwise left as an op).</summary>
        public static BridgeError Parse(string text, byte[] bytes, List<BridgeItem> into)
        {
            var parts = new List<Segment>(1);
            parts.Add(text != null ? new Segment { Text = text, Length = text.Length } : new Segment { Bytes = bytes, Length = bytes.Length });
            return Parse(parts, into);
        }

        public static BridgeError Parse(List<Segment> parts, List<BridgeItem> into)
        {
            if (parts.Count == 0) return BridgeError.None;
            if (parts[0].IsText)
            {
                var texts = scratchTexts ??= new List<string>();
                texts.Clear();
                bool ok = parts.Count == 1
                    ? BridgeCarrier.UnpackEntries(parts[0].Text, parts[0].Offset, parts[0].Offset + parts[0].Length, texts)
                    : BridgeCarrier.UnpackEntries(parts, texts);
                if (ok) foreach (var t in texts) into.Add(new BridgeItem(t));
                texts.Clear();
                if (texts.Capacity > BridgeView.ScratchKeep) texts.Capacity = BridgeView.ScratchKeep;
                return ok ? BridgeError.None : BridgeError.BadText;
            }
            byte[] bytes;
            int offset, length;
            if (parts.Count == 1)
            {
                bytes = parts[0].Bytes; offset = parts[0].Offset; length = parts[0].Length;
            }
            else
            {
                long total = 0;
                foreach (var p in parts) total += p.Length;
                bytes = new byte[(int)total];
                int o = 0;
                foreach (var p in parts) { System.Buffer.BlockCopy(p.Bytes, p.Offset, bytes, o, p.Length); o += p.Length; }
                offset = 0; length = bytes.Length;
            }
            var ops = scratchOps ??= new List<RawOp>();
            ops.Clear();
            var e = BridgeOps.Decode(new ReadOnlySpan<byte>(bytes, offset, length), ops);
            if (e != BridgeError.None) return e;
            foreach (var op in ops)
            {
                int at = offset + op.Offset;
                if (op.Opcode == BridgeOp.LegacyText)
                {
                    var s = BridgeUtf8.Decode(new ReadOnlySpan<byte>(bytes, at, op.Length));
                    if (s != null) { into.Add(new BridgeItem(s)); continue; }
                }
                into.Add(new BridgeItem(op.Opcode, bytes, at, op.Length));
            }
            return BridgeError.None;
        }
    }
}
