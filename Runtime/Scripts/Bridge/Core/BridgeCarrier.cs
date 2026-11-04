using System;
using System.Buffers;
using System.Collections.Generic;

namespace BS.Bridge
{
    /// <summary>A decoded carrier string: a binary frame (payload bytes) or a text-form DATA frame (body chars).</summary>
    internal struct CarrierFrame
    {
        public FrameHeader Header;
        public bool IsText;
        /// <summary>Binary form: the decoded frame bytes; the payload starts at HeaderBytes.</summary>
        public byte[] Frame;
        /// <summary>Text form: the carrier string itself; the body is Source[BodyStart..].</summary>
        public string Source;
        public int BodyStart;

        public ReadOnlySpan<byte> Payload => new ReadOnlySpan<byte>(Frame, BridgeWire.HeaderBytes, Frame.Length - BridgeWire.HeaderBytes);
        public string Body => Source.Substring(BodyStart);
    }

    /// <summary>
    /// ORAB frames inside today's Ora custom-message/default-message strings (no Ora change for M1):
    /// <c>"\u0002B" + base64(frame)</c>, or <c>"\u0002T" + base64(24-byte header) + body</c> where the body is
    /// <c>&lt;len&gt;:&lt;text&gt;</c> entries, each a LEGACY_TEXT op carried raw. No legacy producer starts a string
    /// with U+0002 (proved against APICommands/UICommands/MessageDelimiters by both test suites). A string that starts
    /// with U+0002 is never legacy. Check it ordinally, before any culture-sensitive StartsWith: those ignore U+0002.
    /// Mirror of carrier.ts.
    /// </summary>
    internal static class BridgeCarrier
    {
        public const char Mark = '\u0002';
        public const char FormBinary = 'B';
        public const char FormText = 'T';
        public const int TextHeaderChars = 32;
        static readonly int MaxBinaryChars = 2 + (BridgeWire.HeaderBytes + BridgeWire.MaxFramePayload + 2) / 3 * 4;

        public static bool IsCarrier(string s) => s != null && s.Length > 0 && s[0] == Mark;

        public static string EncodeBinary(ReadOnlySpan<byte> frame)
        {
            var chars = new char[2 + BridgeBase64.EncodedLength(frame.Length)];
            chars[0] = Mark; chars[1] = FormBinary;
            BridgeBase64.Encode(frame, new Span<char>(chars, 2, chars.Length - 2));
            return new string(chars);
        }

        public static string EncodeBinary(FrameHeader h, ReadOnlySpan<byte> payload) => EncodeBinary(BridgeFrame.Encode(h, payload));

        // The encoders below write the carrier string once (string.Create), with no intermediate frame, char[] or body
        // string: Flush allocates what it posts and little else. Their output is identical to the ones above.

        struct BinaryFrameState { public FrameHeader Header; public byte[] Bytes; public int Offset; public int Count; }

        static readonly SpanAction<char, BinaryFrameState> writeBinaryFrame = (span, st) =>
        {
            span[0] = Mark; span[1] = FormBinary;
            Span<byte> header = stackalloc byte[BridgeWire.HeaderBytes];
            BridgeFrame.WriteHeader(header, st.Header);
            // 24 header bytes are whole base64 groups, so base64(header) + base64(payload) = base64(header + payload).
            BridgeBase64.Encode(header, span.Slice(2, TextHeaderChars));
            BridgeBase64.Encode(new ReadOnlySpan<byte>(st.Bytes, st.Offset, st.Count), span.Slice(2 + TextHeaderChars));
        };

        /// <summary>A binary carrier for header + payload bytes[offset..+count], in one allocation.</summary>
        public static string EncodeBinary(FrameHeader h, byte[] bytes, int offset, int count)
        {
            h.Length = count;
            var st = new BinaryFrameState { Header = h, Bytes = bytes, Offset = offset, Count = count };
            return string.Create(2 + TextHeaderChars + BridgeBase64.EncodedLength(count), st, writeBinaryFrame);
        }

        struct TextFrameState { public FrameHeader Header; public string Body; public List<OutEntry> Entries; public TextCursor Start; }

        static readonly SpanAction<char, TextFrameState> writeTextFrame = (span, st) =>
        {
            span[0] = Mark; span[1] = FormText;
            Span<byte> header = stackalloc byte[BridgeWire.HeaderBytes];
            BridgeFrame.WriteHeader(header, st.Header);
            BridgeBase64.Encode(header, span.Slice(2, TextHeaderChars));
            var body = span.Slice(2 + TextHeaderChars);
            if (st.Body != null) st.Body.AsSpan().CopyTo(body);
            else TextBody.Copy(st.Entries, st.Start, body);
        };

        /// <summary>h.Kind must be DATA; TEXT is added and the length is taken from the body.</summary>
        public static string EncodeText(FrameHeader h, string body)
        {
            h.Flags |= FrameFlags.Text;
            h.Length = body.Length;
            return string.Create(2 + TextHeaderChars + body.Length, new TextFrameState { Header = h, Body = body }, writeTextFrame);
        }

        /// <summary>
        /// A text carrier whose body is chars [start, start + h.Length) of entries' "&lt;len&gt;:&lt;text&gt;" concatenation,
        /// written straight from the entries (the body is never built).
        /// </summary>
        public static string EncodeText(FrameHeader h, List<OutEntry> entries, TextCursor start)
        {
            h.Flags |= FrameFlags.Text;
            return string.Create(2 + TextHeaderChars + h.Length, new TextFrameState { Header = h, Entries = entries, Start = start }, writeTextFrame);
        }

        public static BridgeError TryDecode(string s, out CarrierFrame f)
        {
            f = default;
            if (!IsCarrier(s)) return BridgeError.NotCarrier;
            if (s.Length < 2) return BridgeError.BadForm;
            char form = s[1];
            if (form == FormBinary)
            {
                if (s.Length > MaxBinaryChars) return BridgeError.TooLarge;
                var bytes = BridgeBase64.TryDecode(s, 2, s.Length - 2);
                if (bytes == null) return BridgeError.BadBase64;
                var e = BridgeFrame.TryDecode(bytes, out f.Header, out _);
                if (e != BridgeError.None) return e;
                f.Frame = bytes;
                return BridgeError.None;
            }
            if (form == FormText)
            {
                if (s.Length < 2 + TextHeaderChars) return BridgeError.Truncated;
                Span<byte> hb = stackalloc byte[BridgeWire.HeaderBytes];
                var hc = s.AsSpan(2, TextHeaderChars);
                if (BridgeBase64.DecodedLength(hc) != BridgeWire.HeaderBytes || !BridgeBase64.TryDecode(hc, hb)) return BridgeError.BadBase64;
                var e = BridgeFrame.TryReadHeader(hb, true, out f.Header);
                if (e != BridgeError.None) return e;
                if (f.Header.Kind != FrameKind.Data) return BridgeError.BadKind;
                if (s.Length - 2 - TextHeaderChars != f.Header.Length) return BridgeError.BadLength;
                f.IsText = true;
                f.Source = s;
                f.BodyStart = 2 + TextHeaderChars;
                return BridgeError.None;
            }
            return BridgeError.BadForm;
        }

        // ------------------------------------------------------------------------------------------------------
        // Text body entries: <decimal UTF-16 length> ":" <text>, canonical decimal (no leading zeros), <= 9 digits.

        public static int EntrySize(string text) => DecimalDigits(text.Length) + 1 + text.Length;

        public static int DecimalDigits(int n)
        {
            int d = 1;
            while (n >= 10) { n /= 10; d++; }
            return d;
        }

        /// <summary>Appends one entry into buf at pos; returns the new position.</summary>
        public static int WriteEntry(char[] buf, int pos, string text) => WriteEntry(buf.AsSpan(), pos, text);

        public static int WriteEntry(Span<char> buf, int pos, string text)
        {
            int len = text.Length, digits = DecimalDigits(len);
            for (int d = pos + digits - 1; d >= pos; d--) { buf[d] = (char)('0' + len % 10); len /= 10; }
            pos += digits;
            buf[pos++] = ':';
            text.AsSpan().CopyTo(buf.Slice(pos));
            return pos + text.Length;
        }

        public static string PackEntries(IReadOnlyList<string> texts)
        {
            int total = 0;
            for (int i = 0; i < texts.Count; i++) total += EntrySize(texts[i]);
            var buf = new char[total];
            int pos = 0;
            for (int i = 0; i < texts.Count; i++) pos = WriteEntry(buf, pos, texts[i]);
            return new string(buf);
        }

        /// <summary>Strict; false (with the entries read so far left in <paramref name="into"/>) on any damage.</summary>
        public static bool UnpackEntries(string s, int start, int end, List<string> into)
        {
            int pos = start;
            while (pos < end)
            {
                int len = 0, digits = 0;
                char first = s[pos], c = '\0';
                while (pos < end && (c = s[pos]) >= '0' && c <= '9')
                {
                    if (++digits > 9) return false;
                    len = len * 10 + (c - '0');
                    pos++;
                }
                if (digits == 0 || (digits > 1 && first == '0') || pos >= end || c != ':') return false;
                pos++;
                if (len > end - pos) return false;
                into.Add(s.Substring(pos, len));
                pos += len;
            }
            return true;
        }

        /// <summary>
        /// The same strict parse over a message still in its fragments (views into the carrier strings): each entry is
        /// copied once, straight into its string, with no joined body in between.
        /// </summary>
        public static bool UnpackEntries(List<Segment> parts, List<string> into)
        {
            var c = new EntryCursor(parts);
            while (!c.AtEnd)
            {
                int len = 0, digits = 0;
                char first = c.Current, ch = '\0';
                while (!c.AtEnd && (ch = c.Current) >= '0' && ch <= '9')
                {
                    if (++digits > 9) return false;
                    len = len * 10 + (ch - '0');
                    c.Advance(1);
                }
                if (digits == 0 || (digits > 1 && first == '0') || c.AtEnd || c.Current != ':') return false;
                c.Advance(1);
                if (len > c.Remaining) return false;
                into.Add(c.Take(len));
            }
            return true;
        }

        sealed class EntryCursor
        {
            readonly List<Segment> parts;
            int si, pos;
            long remaining;

            public EntryCursor(List<Segment> parts)
            {
                this.parts = parts;
                foreach (var p in parts) remaining += p.Length;
                pos = parts.Count > 0 ? parts[0].Offset : 0;
                Skip();
            }

            public bool AtEnd => remaining == 0;
            public long Remaining => remaining;
            public char Current => parts[si].Text[pos];

            void Skip()
            {
                while (si < parts.Count && pos >= parts[si].Offset + parts[si].Length)
                {
                    si++;
                    if (si < parts.Count) pos = parts[si].Offset;
                }
            }

            public void Advance(int n)
            {
                while (n > 0)
                {
                    int here = Math.Min(n, parts[si].Offset + parts[si].Length - pos);
                    pos += here; n -= here; remaining -= here;
                    Skip();
                }
            }

            public string Take(int len)
            {
                if (len == 0) return string.Empty;
                int end = parts[si].Offset + parts[si].Length;
                if (end - pos >= len)
                {
                    var s = parts[si].Text.Substring(pos, len);
                    Advance(len);
                    return s;
                }
                var result = string.Create(len, (parts, si, pos), (span, st) =>
                {
                    int o = 0, i = st.si, p = st.pos;
                    while (o < span.Length)
                    {
                        var seg = st.parts[i];
                        int n = Math.Min(span.Length - o, seg.Offset + seg.Length - p);
                        seg.Text.AsSpan(p, n).CopyTo(span.Slice(o));
                        o += n;
                        i++;
                        if (i < st.parts.Count) p = st.parts[i].Offset;
                    }
                });
                Advance(len);
                return result;
            }
        }

        /// <summary>
        /// Reads only a carrier's header (for "B", base64 of the frame's first 24 bytes; for "T", its header block),
        /// so a frame for another epoch or a host-only kind can be refused without decoding its body.
        /// </summary>
        public static bool TryPeekHeader(string s, out FrameHeader h, out bool isText)
        {
            h = default;
            isText = false;
            if (!IsCarrier(s) || s.Length < 2 + TextHeaderChars) return false;
            char form = s[1];
            if (form != FormBinary && form != FormText) return false;
            isText = form == FormText;
            Span<byte> hb = stackalloc byte[BridgeWire.HeaderBytes];
            var hc = s.AsSpan(2, TextHeaderChars);
            if (BridgeBase64.DecodedLength(hc) != BridgeWire.HeaderBytes || !BridgeBase64.TryDecode(hc, hb)) return false;
            return BridgeFrame.TryReadHeader(hb, isText, out h) == BridgeError.None;
        }

        /// <summary>Cuts a body into pieces of at most size units, never between the halves of a surrogate pair.</summary>
        public static List<string> SplitText(string body, int size)
        {
            var out_ = new List<string>();
            int pos = 0;
            while (pos < body.Length)
            {
                int cut = Math.Min(pos + size, body.Length);
                if (cut < body.Length && cut - pos > 1 && char.IsHighSurrogate(body[cut - 1]) && char.IsLowSurrogate(body[cut])) cut--;
                out_.Add(body.Substring(pos, cut - pos));
                pos = cut;
            }
            return out_;
        }
    }

    /// <summary>A position in a text body that is never built: entry index, and offset within its "&lt;len&gt;:&lt;text&gt;".</summary>
    internal struct TextCursor
    {
        public int Entry;
        public int Offset;
    }

    /// <summary>
    /// Reads a text body as the concatenation of its entries' "&lt;len&gt;:&lt;text&gt;" forms without building it, so a
    /// fragment can be written straight into its carrier (BridgeLink sizes the fragments, OutFrame writes them).
    /// </summary>
    internal static class TextBody
    {
        public static int Size(List<OutEntry> entries)
        {
            int total = 0;
            foreach (var e in entries) total += BridgeCarrier.EntrySize(e.Text);
            return total;
        }

        /// <summary>The cursor n chars further on.</summary>
        public static TextCursor Advance(List<OutEntry> entries, TextCursor c, int n)
        {
            while (n > 0)
            {
                int rest = BridgeCarrier.EntrySize(entries[c.Entry].Text) - c.Offset;
                if (n < rest) { c.Offset += n; return c; }
                n -= rest;
                c.Entry++;
                c.Offset = 0;
            }
            return c;
        }

        public static char CharAt(List<OutEntry> entries, TextCursor c)
        {
            var t = entries[c.Entry].Text;
            int digits = BridgeCarrier.DecimalDigits(t.Length);
            if (c.Offset > digits) return t[c.Offset - digits - 1];
            if (c.Offset == digits) return ':';
            int len = t.Length;
            for (int d = digits - 1; d > c.Offset; d--) len /= 10;
            return (char)('0' + len % 10);
        }

        /// <summary>Writes into.Length chars of the body from c.</summary>
        public static void Copy(List<OutEntry> entries, TextCursor c, Span<char> into)
        {
            Span<char> prefix = stackalloc char[11];
            int o = 0;
            while (o < into.Length)
            {
                var t = entries[c.Entry].Text;
                int digits = BridgeCarrier.DecimalDigits(t.Length);
                if (c.Offset <= digits)
                {
                    int len = t.Length;
                    for (int d = digits - 1; d >= 0; d--) { prefix[d] = (char)('0' + len % 10); len /= 10; }
                    prefix[digits] = ':';
                    int n = Math.Min(digits + 1 - c.Offset, into.Length - o);
                    prefix.Slice(c.Offset, n).CopyTo(into.Slice(o));
                    o += n;
                    c.Offset += n;
                    if (o == into.Length) return;
                }
                int ti = c.Offset - digits - 1;
                int m = Math.Min(t.Length - ti, into.Length - o);
                t.AsSpan(ti, m).CopyTo(into.Slice(o));
                o += m;
                c.Offset += m;
                if (c.Offset == digits + 1 + t.Length) { c.Entry++; c.Offset = 0; }
            }
        }
    }

    /// <summary>
    /// The frozen 4.0.25 batch format: marker, then &lt;len&gt;:&lt;message&gt; per message (page "¶§|", Unity "‽").
    /// Kept for LEGACY mode only (kill switch, opt-out, fallback) and deleted at M7; byte-identical to WireFrame.cs.
    /// </summary>
    internal static class BridgeLegacyBatch
    {
        public const string PageMarker = "¶§|";
        public const string UnityMarker = "‽";
        const int MaxLengthDigits = 9;

        struct PackState { public string Marker; public IReadOnlyList<string> Messages; public int Start; public int Count; }

        static readonly SpanAction<char, PackState> writePack = (span, st) =>
        {
            st.Marker.AsSpan().CopyTo(span);
            int pos = st.Marker.Length;
            for (int i = st.Start; i < st.Start + st.Count; i++) pos = BridgeCarrier.WriteEntry(span, pos, st.Messages[i] ?? string.Empty);
        };

        public static string Pack(string marker, IReadOnlyList<string> messages, int start, int count)
        {
            int total = marker.Length;
            for (int i = start; i < start + count; i++) total += BridgeCarrier.EntrySize(messages[i] ?? string.Empty);
            return string.Create(total, new PackState { Marker = marker, Messages = messages, Start = start, Count = count }, writePack);
        }

        public static string Pack(string marker, IReadOnlyList<string> messages) => Pack(marker, messages, 0, messages.Count);

        /// <summary>Same leniency as WireFrame.Unpack (leading zeros accepted).</summary>
        public static bool Unpack(string frame, int start, List<string> into)
        {
            int position = start, end = frame.Length;
            while (position < end)
            {
                int length = 0, digits = 0;
                while (position < end && frame[position] >= '0' && frame[position] <= '9')
                {
                    if (++digits > MaxLengthDigits) return false;
                    length = length * 10 + (frame[position] - '0');
                    position++;
                }
                if (digits == 0 || position >= end || frame[position] != ':') return false;
                position++;
                if (length > end - position) return false;
                into.Add(frame.Substring(position, length));
                position += length;
            }
            return true;
        }
    }
}
