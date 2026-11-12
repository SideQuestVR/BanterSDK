using System;
using System.Buffers.Binary;

namespace BS.Bridge
{
    /// <summary>
    /// The 24-byte little-endian ORAB v1 header:
    /// 0 u32 magic | 4 u8 ver | 5 u8 kind | 6 u16 flags | 8 u32 docEpoch | 12 u32 seq | 16 u32 ack | 20 u32 payloadBytes.
    /// </summary>
    internal struct FrameHeader
    {
        public byte Kind;
        public ushort Flags;
        public uint Epoch;
        public uint Seq;
        public uint Ack;
        /// <summary>Payload length: bytes, or UTF-16 units for a TEXT (carrier text-form) frame.</summary>
        public int Length;

        public FrameHeader(byte kind, ushort flags, uint epoch, uint seq, uint ack, int length)
        {
            Kind = kind; Flags = flags; Epoch = epoch; Seq = seq; Ack = ack; Length = length;
        }

        public bool IsFirst => (Flags & FrameFlags.FragFirst) != 0;
        public bool IsLast => (Flags & FrameFlags.FragLast) != 0;
        public bool IsText => (Flags & FrameFlags.Text) != 0;
        public override string ToString() => $"kind={Kind} flags={Flags} epoch={Epoch} seq={Seq} ack={Ack} len={Length}";
    }

    /// <summary>CLOSE/RESET body: u8 reason · u32 consumedSeq · UTF-8 text.</summary>
    internal struct CloseBody
    {
        public byte Reason;
        public uint ConsumedSeq;
        public string Text;
    }

    /// <summary>ORAB frame codec. Decoding validates everything and returns an error; it never throws on input.</summary>
    internal static class BridgeFrame
    {
        public static void WriteHeader(Span<byte> o, in FrameHeader h)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(o, BridgeWire.Magic);
            o[4] = BridgeWire.Version;
            o[5] = h.Kind;
            BinaryPrimitives.WriteUInt16LittleEndian(o.Slice(6), h.Flags);
            BinaryPrimitives.WriteUInt32LittleEndian(o.Slice(8), h.Epoch);
            BinaryPrimitives.WriteUInt32LittleEndian(o.Slice(12), h.Seq);
            BinaryPrimitives.WriteUInt32LittleEndian(o.Slice(16), h.Ack);
            BinaryPrimitives.WriteUInt32LittleEndian(o.Slice(20), (uint)h.Length);
        }

        /// <summary>A whole binary frame; the header's length is taken from the payload.</summary>
        public static byte[] Encode(FrameHeader h, ReadOnlySpan<byte> payload)
        {
            var b = new byte[BridgeWire.HeaderBytes + payload.Length];
            h.Length = payload.Length;
            WriteHeader(b, h);
            payload.CopyTo(new Span<byte>(b, BridgeWire.HeaderBytes, payload.Length));
            return b;
        }

        /// <summary>
        /// Reads and validates a header. text=true accepts only a carrier text-form header (DATA + TEXT); text=false
        /// rejects the TEXT flag.
        /// </summary>
        public static BridgeError TryReadHeader(ReadOnlySpan<byte> b, bool text, out FrameHeader h)
        {
            h = default;
            if (b.Length < BridgeWire.HeaderBytes) return BridgeError.Truncated;
            if (BinaryPrimitives.ReadUInt32LittleEndian(b) != BridgeWire.Magic) return BridgeError.BadMagic;
            if (b[4] != BridgeWire.Version) return BridgeError.BadVersion;
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(20));
            h.Kind = b[5];
            h.Flags = BinaryPrimitives.ReadUInt16LittleEndian(b.Slice(6));
            h.Epoch = BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(8));
            h.Seq = BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(12));
            h.Ack = BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(16));
            h.Length = length > BridgeWire.MaxFramePayload ? -1 : (int)length;
            return Validate(h, text);
        }

        /// <summary>The per-kind rules; the same table is in frame.ts.</summary>
        public static BridgeError Validate(in FrameHeader h, bool text)
        {
            if (!FrameKind.IsKnown(h.Kind)) return BridgeError.BadKind;
            if ((h.Flags & ~FrameFlags.Known) != 0) return BridgeError.BadFlags;
            if (h.Length < 0 || h.Length > BridgeWire.MaxFramePayload) return BridgeError.TooLarge;
            if (h.IsText != text) return BridgeError.BadFlags;
            if (h.Kind == FrameKind.Data)
            {
                if (h.Seq == 0) return BridgeError.BadSeq;
                if (h.Length == 0) return BridgeError.BadLength;
                return BridgeError.None;
            }
            if (h.Flags != 0) return BridgeError.BadFlags;
            if (h.Seq != 0) return BridgeError.BadSeq;
            switch (h.Kind)
            {
                case FrameKind.Ack:
                case FrameKind.Heartbeat:
                case FrameKind.Pause:
                case FrameKind.Resume:
                    return h.Length == 0 ? BridgeError.None : BridgeError.BadLength;
                case FrameKind.Credit:
                    return h.Length == 4 ? BridgeError.None : BridgeError.BadLength;
                case FrameKind.Close:
                case FrameKind.Reset:
                    return h.Length >= 5 && h.Length <= BridgeWire.MaxControlPayload ? BridgeError.None : BridgeError.BadLength;
                case FrameKind.Open:
                    return h.Length <= BridgeWire.MaxOpenPayload ? BridgeError.None : BridgeError.BadLength;
            }
            return BridgeError.BadKind;
        }

        /// <summary>Decodes one complete binary frame occupying all of b; payload is a slice of b.</summary>
        public static BridgeError TryDecode(ReadOnlySpan<byte> b, out FrameHeader h, out ReadOnlySpan<byte> payload)
        {
            payload = default;
            var e = TryReadHeader(b, false, out h);
            if (e != BridgeError.None) return e;
            if (b.Length - BridgeWire.HeaderBytes != h.Length) return BridgeError.BadLength;
            payload = b.Slice(BridgeWire.HeaderBytes);
            if (h.Kind == FrameKind.Credit)
            {
                if (BinaryPrimitives.ReadUInt32LittleEndian(payload) > BridgeWire.MaxWindow) return BridgeError.BadPayload;
            }
            else if (h.Kind == FrameKind.Close || h.Kind == FrameKind.Reset)
            {
                if (!TryDecodeClose(payload, out _)) return BridgeError.BadPayload;
            }
            return BridgeError.None;
        }

        public static byte[] EncodeClose(byte reason, uint consumedSeq, string text)
        {
            text ??= string.Empty;
            if (text.Length > BridgeWire.MaxReasonText) text = text.Substring(0, BridgeWire.MaxReasonText);
            var t = BridgeUtf8.Encode(text);
            var b = new byte[5 + t.Length];
            b[0] = reason;
            BinaryPrimitives.WriteUInt32LittleEndian(new Span<byte>(b, 1, 4), consumedSeq);
            t.CopyTo(b, 5);
            return b;
        }

        public static bool TryDecodeClose(ReadOnlySpan<byte> p, out CloseBody c)
        {
            c = default;
            if (p.Length < 5 || p.Length > BridgeWire.MaxControlPayload) return false;
            var text = BridgeUtf8.Decode(p.Slice(5));
            if (text == null) return false;
            c.Reason = p[0];
            c.ConsumedSeq = BinaryPrimitives.ReadUInt32LittleEndian(p.Slice(1));
            c.Text = text;
            return true;
        }
    }
}
