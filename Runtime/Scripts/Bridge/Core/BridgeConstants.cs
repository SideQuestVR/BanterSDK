// Bridge 2 wire constants (ORAB v1). Mirror of Injection~/src/bridge/constants.ts; the golden vectors in
// Bridge~/vectors keep the two in step. This assembly (BS.Bridge.Core) has no engine references, so `dotnet test`
// can run it outside Unity; it must stay C# 9 and IL2CPP-safe (no Reflection.Emit, no dynamic).

namespace BS.Bridge
{
    internal static class BridgeWire
    {
        /// <summary>"ORAB" read as a little-endian u32: bytes 4F 52 41 42.</summary>
        public const uint Magic = 0x4241524F;
        public const byte Version = 1;
        public const int HeaderBytes = 24;

        /// <summary>Hard cap on one frame's payload (bytes, or UTF-16 units for TEXT frames).</summary>
        public const int MaxFramePayload = 256 * 1024;
        /// <summary>Senders close a batch / cut a fragment at this size.</summary>
        public const int FragmentSize = 64 * 1024;
        /// <summary>Largest message reassembled from fragments.</summary>
        public const int MaxMessage = 32 * 1024 * 1024;
        public const int DefaultWindow = 1024 * 1024;
        public const int MaxWindow = 1024 * 1024;
        public const int MaxControlPayload = 256;
        public const int MaxOpenPayload = 4096;

        public const ushort ProtoMajor = 1;
        public const ushort ProtoMinor = 0;

        public const int MaxSdkVersion = 64;
        public const int MaxDocToken = 64;
        public const int MaxHref = 8192;
        public const int MaxReasonText = 200;
    }

    internal static class FrameKind
    {
        public const byte Data = 1;
        public const byte Ack = 2;
        public const byte Heartbeat = 3;
        public const byte Credit = 4;
        // Host-only: a page may never send these; on the carrier lane Unity plays the host.
        public const byte Open = 16;
        public const byte Close = 17;
        public const byte Pause = 18;
        public const byte Resume = 19;
        public const byte Reset = 20;

        public static bool IsKnown(byte kind) => (kind >= Data && kind <= Credit) || (kind >= Open && kind <= Reset);
        public static bool IsHostOnly(byte kind) => kind >= Open && kind <= Reset;
    }

    internal static class FrameFlags
    {
        public const ushort FragFirst = 1;
        public const ushort FragLast = 2;
        public const ushort Urgent = 4;
        /// <summary>Carrier text projection only: the payload is a UTF-16 entry list.</summary>
        public const ushort Text = 8;
        public const ushort Known = 0x0F;
        /// <summary>An unfragmented DATA frame is its own first and last fragment.</summary>
        public const ushort Complete = 3;
    }

    /// <summary>BSB opcodes (M1 subset; typed families come from the lock in M3).</summary>
    internal static class BridgeOp
    {
        public const int Hello = 0x01;
        public const int Welcome = 0x02;
        public const int Bye = 0x03;
        public const int Ping = 0x04;
        public const int Pong = 0x05;
        public const int Legacy = 0x06;
        public const int Holding = 0x07;
        public const int LegacyText = 0x0F;
        public const int Reply = 0x30;
    }

    internal static class ReplyStatus
    {
        public const byte Ok = 0;
        public const byte Destroyed = 1;
        public const byte UnknownHandle = 2;
        public const byte Type = 3;
        public const byte UnknownMember = 4;
        public const byte Denied = 5;
        public const byte NotReady = 6;
        public const byte Timeout = 7;
        public const byte Limit = 8;
        public const byte Cancelled = 9;
        public const byte Internal = 10;
        public const byte Unsupported = 11;
        public const byte Malformed = 12;
    }

    internal static class BridgeReason
    {
        public const byte None = 0;
        public const byte PageHide = 1;
        public const byte Timeout = 2;
        public const byte Disabled = 3;
        public const byte AboutDocument = 4;
        public const byte NotTop = 5;
        public const byte Protocol = 6;
        public const byte Navigated = 7;
        public const byte Superseded = 8;
        public const byte Version = 9;
        public const byte OptOut = 10;
        public const byte Closed = 11;
        public const byte LateWelcome = 12;
        /// <summary>Unity's queue for a page that stopped acknowledging grew past its cap.</summary>
        public const byte Overflow = 13;
    }

    internal static class BridgeFeature
    {
        public const uint TextForm = 1;
        public const uint OptOut = 2;
    }

    internal enum BridgeError
    {
        None = 0,
        Truncated,
        BadMagic,
        BadVersion,
        BadKind,
        BadFlags,
        BadSeq,
        BadLength,
        TooLarge,
        BadPayload,
        NotCarrier,
        BadForm,
        BadBase64,
        BadText,
        BadVarint,
        OpOverrun,
        BadUtf8,
        Malformed,
        FragOrder,
        ReassemblyLimit,
        SeqGap,
        BadAck,
        CreditExceeded,
    }

    internal static class BridgeErrors
    {
        /// <summary>The vector-file spelling (TRUNCATED, BAD_MAGIC, ...).</summary>
        public static string Name(BridgeError e)
        {
            switch (e)
            {
                case BridgeError.None: return "NONE";
                case BridgeError.Truncated: return "TRUNCATED";
                case BridgeError.BadMagic: return "BAD_MAGIC";
                case BridgeError.BadVersion: return "BAD_VERSION";
                case BridgeError.BadKind: return "BAD_KIND";
                case BridgeError.BadFlags: return "BAD_FLAGS";
                case BridgeError.BadSeq: return "BAD_SEQ";
                case BridgeError.BadLength: return "BAD_LENGTH";
                case BridgeError.TooLarge: return "TOO_LARGE";
                case BridgeError.BadPayload: return "BAD_PAYLOAD";
                case BridgeError.NotCarrier: return "NOT_CARRIER";
                case BridgeError.BadForm: return "BAD_FORM";
                case BridgeError.BadBase64: return "BAD_BASE64";
                case BridgeError.BadText: return "BAD_TEXT";
                case BridgeError.BadVarint: return "BAD_VARINT";
                case BridgeError.OpOverrun: return "OP_OVERRUN";
                case BridgeError.BadUtf8: return "BAD_UTF8";
                case BridgeError.Malformed: return "MALFORMED";
                case BridgeError.FragOrder: return "FRAG_ORDER";
                case BridgeError.ReassemblyLimit: return "REASSEMBLY_LIMIT";
                case BridgeError.SeqGap: return "SEQ_GAP";
                case BridgeError.BadAck: return "BAD_ACK";
                case BridgeError.CreditExceeded: return "CREDIT_EXCEEDED";
                default: return e.ToString();
            }
        }
    }
}
