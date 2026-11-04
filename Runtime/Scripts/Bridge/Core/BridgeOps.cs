using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace BS.Bridge
{
    /// <summary>One op of a message: opcode plus the payload's position in the message buffer.</summary>
    internal readonly struct RawOp
    {
        public readonly uint Opcode;
        public readonly int Offset;
        public readonly int Length;
        public RawOp(uint opcode, int offset, int length) { Opcode = opcode; Offset = offset; Length = length; }
    }

    /// <summary>
    /// BSB op envelope: varint opcode · varint len · payload, repeated. Varints are unsigned LEB128, at most 5 bytes,
    /// at most 2^32-1 and canonical (no overlongs). Mirror of ops.ts.
    /// </summary>
    internal static class BridgeOps
    {
        /// <summary>A canonical varint at b[pos] (pos advances), or -1 on any violation.</summary>
        public static long ReadVarint(ReadOnlySpan<byte> b, ref int pos)
        {
            long value = 0;
            for (int i = 0; i < 5; i++)
            {
                if (pos + i >= b.Length) return -1;
                int x = b[pos + i];
                if (i == 4 && x > 0x0F) return -1;
                value |= (long)(x & 0x7F) << (7 * i);
                if ((x & 0x80) == 0)
                {
                    if (i > 0 && x == 0) return -1;
                    pos += i + 1;
                    return value;
                }
            }
            return -1;
        }

        public static int VarintLength(uint v)
        {
            int n = 1;
            while (v >= 0x80) { v >>= 7; n++; }
            return n;
        }

        /// <summary>Splits a message into ops; on envelope damage the ops before it are left in <paramref name="into"/>.</summary>
        public static BridgeError Decode(ReadOnlySpan<byte> b, List<RawOp> into)
        {
            int pos = 0;
            while (pos < b.Length)
            {
                long opcode = ReadVarint(b, ref pos);
                if (opcode < 0) return BridgeError.BadVarint;
                long len = ReadVarint(b, ref pos);
                if (len < 0) return BridgeError.BadVarint;
                if (len > b.Length - pos) return BridgeError.OpOverrun;
                into.Add(new RawOp((uint)opcode, pos, (int)len));
                pos += (int)len;
            }
            return BridgeError.None;
        }

        public static void Write(ByteWriter w, uint opcode, ReadOnlySpan<byte> payload)
        {
            w.Varint(opcode);
            w.Varint((uint)payload.Length);
            w.Bytes(payload);
        }

        public static byte[] Encode(uint opcode, ReadOnlySpan<byte> payload)
        {
            var w = new ByteWriter(payload.Length + 10);
            Write(w, opcode, payload);
            return w.ToArray();
        }

        public static byte[] EncodeU32(uint v)
        {
            var b = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(b, v);
            return b;
        }

        /// <summary>PING/PONG/HOLDING payload, or -1.</summary>
        public static long DecodeU32(ReadOnlySpan<byte> p) => p.Length == 4 ? BinaryPrimitives.ReadUInt32LittleEndian(p) : -1;
    }

    /// <summary>Bounds-checked payload reader: one failure poisons it, Done demands exact consumption.</summary>
    internal ref struct PayloadCursor
    {
        readonly ReadOnlySpan<byte> b;
        int pos;
        bool failed;

        public PayloadCursor(ReadOnlySpan<byte> b) { this.b = b; pos = 0; failed = false; }

        public bool Failed => failed;
        public bool Done => !failed && pos == b.Length;

        bool Need(int n)
        {
            if (failed || b.Length - pos < n) { failed = true; return false; }
            return true;
        }

        public byte U8() { if (!Need(1)) return 0; return b[pos++]; }
        public ushort U16() { if (!Need(2)) return 0; var x = BinaryPrimitives.ReadUInt16LittleEndian(b.Slice(pos)); pos += 2; return x; }
        public uint U32() { if (!Need(4)) return 0; var x = BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(pos)); pos += 4; return x; }
        public ReadOnlySpan<byte> Raw(int n) { if (!Need(n)) return default; var x = b.Slice(pos, n); pos += n; return x; }
        public uint Varint()
        {
            if (failed) return 0;
            long x = BridgeOps.ReadVarint(b, ref pos);
            if (x < 0) { failed = true; return 0; }
            return (uint)x;
        }
        public string Str(int maxBytes)
        {
            uint n = Varint();
            if (failed) return string.Empty;
            if (n > maxBytes || !Need((int)n)) { failed = true; return string.Empty; }
            var s = BridgeUtf8.Decode(b.Slice(pos, (int)n));
            if (s == null) { failed = true; return string.Empty; }
            pos += (int)n;
            return s;
        }
        public ReadOnlySpan<byte> Rest() { var x = b.Slice(pos); pos = b.Length; return x; }
    }

    internal sealed class HelloPayload
    {
        public ushort ProtoMajor = BridgeWire.ProtoMajor;
        public ushort ProtoMinor = BridgeWire.ProtoMinor;
        /// <summary>8 bytes as 16 lowercase hex chars.</summary>
        public string SchemaHash = "0000000000000000";
        public uint Features;
        public uint RecvWindow = BridgeWire.DefaultWindow;
        public bool IsTop = true;
        public string SdkVersion = string.Empty;
        public string DocToken = string.Empty;
        public string Href = string.Empty;

        public byte[] Encode()
        {
            var w = new ByteWriter(64 + Href.Length);
            w.U16(ProtoMajor); w.U16(ProtoMinor);
            w.Bytes(BridgeHex.FromHex((SchemaHash + "0000000000000000").Substring(0, 16)));
            w.U32(Features); w.U32(RecvWindow); w.U8(IsTop ? 1 : 0);
            w.Str(SdkVersion); w.Str(DocToken); w.Str(Href);
            return w.ToArray();
        }

        public static HelloPayload TryDecode(ReadOnlySpan<byte> p)
        {
            var c = new PayloadCursor(p);
            var h = new HelloPayload();
            h.ProtoMajor = c.U16(); h.ProtoMinor = c.U16();
            h.SchemaHash = BridgeHex.ToHex(c.Raw(8));
            h.Features = c.U32(); h.RecvWindow = c.U32();
            byte top = c.U8();
            h.SdkVersion = c.Str(BridgeWire.MaxSdkVersion);
            h.DocToken = c.Str(BridgeWire.MaxDocToken);
            h.Href = c.Str(BridgeWire.MaxHref);
            if (!c.Done || top > 1 || h.DocToken.Length == 0) return null;
            h.IsTop = top == 1;
            return h;
        }
    }

    internal sealed class WelcomePayload
    {
        public uint Epoch;
        public byte Mode;
        public byte Lane;
        public uint Families;
        public uint Caps;
        public uint Features;
        public uint RecvWindow;
        public uint MaxFramePayload = BridgeWire.MaxFramePayload;
        public uint FragmentSize = BridgeWire.FragmentSize;

        /// <summary>Mode 0 (legacy strings) is retired: the only mode is the session.</summary>
        public const byte ModeV2 = 1;
        public const byte LaneCarrier = 0, LaneNative = 1;

        public byte[] Encode()
        {
            var w = new ByteWriter(34);
            w.U32(Epoch); w.U8(Mode); w.U8(Lane);
            w.U32(Families); w.U32(Caps); w.U32(Features);
            w.U32(RecvWindow); w.U32(MaxFramePayload); w.U32(FragmentSize);
            return w.ToArray();
        }

        public static WelcomePayload TryDecode(ReadOnlySpan<byte> p)
        {
            var c = new PayloadCursor(p);
            var x = new WelcomePayload
            {
                Epoch = c.U32(), Mode = c.U8(), Lane = c.U8(), Families = c.U32(), Caps = c.U32(), Features = c.U32(),
                RecvWindow = c.U32(), MaxFramePayload = c.U32(), FragmentSize = c.U32(),
            };
            if (!c.Done || x.Mode > 1 || x.Lane > 1) return null;
            return x;
        }
    }

    /// <summary>BYE: u8 reason · str docToken (empty from Unity).</summary>
    internal struct FarewellPayload
    {
        public byte Reason;
        public string DocToken;

        public byte[] Encode()
        {
            var w = new ByteWriter(8 + (DocToken?.Length ?? 0));
            w.U8(Reason); w.Str(DocToken ?? string.Empty);
            return w.ToArray();
        }

        public static bool TryDecode(ReadOnlySpan<byte> p, out FarewellPayload f)
        {
            var c = new PayloadCursor(p);
            f.Reason = c.U8();
            f.DocToken = c.Str(BridgeWire.MaxDocToken);
            return c.Done;
        }
    }

    /// <summary>REPLY: varint reqId (never 0) · u8 status · body.</summary>
    internal static class ReplyPayload
    {
        public static byte[] Encode(uint reqId, byte status, ReadOnlySpan<byte> body)
        {
            var w = new ByteWriter(8 + body.Length);
            w.Varint(reqId); w.U8(status); w.Bytes(body);
            return w.ToArray();
        }

        public static bool TryDecode(ReadOnlySpan<byte> p, out uint reqId, out byte status, out ReadOnlySpan<byte> body)
        {
            var c = new PayloadCursor(p);
            reqId = c.Varint();
            status = c.U8();
            body = c.Failed ? default : c.Rest();
            return !c.Failed && reqId != 0;
        }
    }
}
