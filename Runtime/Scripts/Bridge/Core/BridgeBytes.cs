using System;
using System.Buffers.Binary;
using System.Text;

namespace BS.Bridge
{
    /// <summary>Growable little-endian byte writer (reuse it: Reset keeps the buffer).</summary>
    internal sealed class ByteWriter
    {
        byte[] buf;
        public int Length;

        public ByteWriter(int capacity = 256) { buf = new byte[Math.Max(16, capacity)]; }

        public byte[] Buffer => buf;
        public ReadOnlySpan<byte> Span => new ReadOnlySpan<byte>(buf, 0, Length);
        public void Reset() => Length = 0;

        void Ensure(int extra)
        {
            int need = Length + extra;
            if (need <= buf.Length) return;
            int size = buf.Length * 2;
            while (size < need) size *= 2;
            Array.Resize(ref buf, size);
        }

        public void U8(int v) { Ensure(1); buf[Length++] = (byte)v; }
        public void U16(int v) { Ensure(2); BinaryPrimitives.WriteUInt16LittleEndian(new Span<byte>(buf, Length, 2), (ushort)v); Length += 2; }
        public void U32(uint v) { Ensure(4); BinaryPrimitives.WriteUInt32LittleEndian(new Span<byte>(buf, Length, 4), v); Length += 4; }
        public void F32(float v) { Ensure(4); BinaryPrimitives.WriteInt32LittleEndian(new Span<byte>(buf, Length, 4), BitConverter.SingleToInt32Bits(v)); Length += 4; }
        public void F64(double v) { Ensure(8); BinaryPrimitives.WriteInt64LittleEndian(new Span<byte>(buf, Length, 8), BitConverter.DoubleToInt64Bits(v)); Length += 8; }
        public void Bytes(ReadOnlySpan<byte> b) { Ensure(b.Length); b.CopyTo(new Span<byte>(buf, Length, b.Length)); Length += b.Length; }
        public void Varint(uint v)
        {
            Ensure(5);
            while (v >= 0x80) { buf[Length++] = (byte)(v | 0x80); v >>= 7; }
            buf[Length++] = (byte)v;
        }
        /// <summary>varint byte length + UTF-8 bytes.</summary>
        public void Str(string s)
        {
            int n = BridgeUtf8.Length(s);
            Varint((uint)n);
            Ensure(n);
            BridgeUtf8.Encode(s, new Span<byte>(buf, Length, n));
            Length += n;
        }
        public byte[] ToArray() { var a = new byte[Length]; System.Buffer.BlockCopy(buf, 0, a, 0, Length); return a; }
    }

    /// <summary>
    /// Strict base64 (standard alphabet, padded, canonical: no whitespace, zero unused bits). Hand-written rather
    /// than Convert.FromBase64String, which skips whitespace, so both ends accept exactly the same strings.
    /// </summary>
    internal static class BridgeBase64
    {
        const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
        static readonly sbyte[] Inverse = BuildInverse();

        static sbyte[] BuildInverse()
        {
            var t = new sbyte[128];
            for (int i = 0; i < t.Length; i++) t[i] = -1;
            for (int i = 0; i < 64; i++) t[Alphabet[i]] = (sbyte)i;
            return t;
        }

        public static int EncodedLength(int bytes) => (bytes + 2) / 3 * 4;

        /// <summary>Writes the encoding of <paramref name="b"/> into <paramref name="dst"/> (EncodedLength chars).</summary>
        public static void Encode(ReadOnlySpan<byte> b, Span<char> dst)
        {
            int i = 0, o = 0;
            for (; i + 2 < b.Length; i += 3)
            {
                int n = (b[i] << 16) | (b[i + 1] << 8) | b[i + 2];
                dst[o++] = Alphabet[n >> 18];
                dst[o++] = Alphabet[(n >> 12) & 63];
                dst[o++] = Alphabet[(n >> 6) & 63];
                dst[o++] = Alphabet[n & 63];
            }
            int rest = b.Length - i;
            if (rest == 1)
            {
                int n = b[i] << 16;
                dst[o++] = Alphabet[n >> 18];
                dst[o++] = Alphabet[(n >> 12) & 63];
                dst[o++] = '=';
                dst[o] = '=';
            }
            else if (rest == 2)
            {
                int n = (b[i] << 16) | (b[i + 1] << 8);
                dst[o++] = Alphabet[n >> 18];
                dst[o++] = Alphabet[(n >> 12) & 63];
                dst[o++] = Alphabet[(n >> 6) & 63];
                dst[o] = '=';
            }
        }

        public static string Encode(ReadOnlySpan<byte> b)
        {
            var chars = new char[EncodedLength(b.Length)];
            Encode(b, chars);
            return new string(chars);
        }

        /// <summary>Decoded length of a canonical encoding, or -1 when the length/padding is impossible.</summary>
        public static int DecodedLength(ReadOnlySpan<char> s)
        {
            if (s.Length % 4 != 0) return -1;
            if (s.Length == 0) return 0;
            int pad = 0;
            if (s[s.Length - 1] == '=') pad = s[s.Length - 2] == '=' ? 2 : 1;
            return s.Length / 4 * 3 - pad;
        }

        /// <summary>Strict decode into dst (exactly DecodedLength bytes). False on anything non-canonical.</summary>
        public static bool TryDecode(ReadOnlySpan<char> s, Span<byte> dst)
        {
            int len = DecodedLength(s);
            if (len < 0 || dst.Length < len) return false;
            if (s.Length == 0) return true;
            int pad = s.Length / 4 * 3 - len;
            int o = 0;
            for (int i = 0; i < s.Length; i += 4)
            {
                bool last = i + 4 == s.Length;
                int c0 = Sym(s[i]), c1 = Sym(s[i + 1]);
                if (c0 < 0 || c1 < 0) return false;
                if (last && pad == 2)
                {
                    if ((c1 & 15) != 0) return false;
                    dst[o] = (byte)((c0 << 2) | (c1 >> 4));
                    return true;
                }
                int c2 = Sym(s[i + 2]);
                if (c2 < 0) return false;
                if (last && pad == 1)
                {
                    if ((c2 & 3) != 0) return false;
                    dst[o++] = (byte)((c0 << 2) | (c1 >> 4));
                    dst[o] = (byte)(((c1 & 15) << 4) | (c2 >> 2));
                    return true;
                }
                int c3 = Sym(s[i + 3]);
                if (c3 < 0) return false;
                dst[o++] = (byte)((c0 << 2) | (c1 >> 4));
                dst[o++] = (byte)(((c1 & 15) << 4) | (c2 >> 2));
                dst[o++] = (byte)(((c2 & 3) << 6) | c3);
            }
            return true;
        }

        public static byte[] TryDecode(string s, int start, int length)
        {
            var span = s.AsSpan(start, length);
            int n = DecodedLength(span);
            if (n < 0) return null;
            var dst = new byte[n];
            return TryDecode(span, dst) ? dst : null;
        }

        static int Sym(char c) => c < 128 ? Inverse[c] : -1;
    }

    /// <summary>
    /// UTF-8 the same way on both ends: encoding turns lone surrogates into U+FFFD (like TextEncoder and the UTF-8
    /// transports); decoding is strict and never throws (no overlongs, surrogates, &gt; U+10FFFF or truncation).
    /// </summary>
    internal static class BridgeUtf8
    {
        public static int Length(string s)
        {
            int n = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c < 0x80) n += 1;
                else if (c < 0x800) n += 2;
                else if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) { n += 4; i++; }
                else n += 3;
            }
            return n;
        }

        public static void Encode(string s, Span<byte> o)
        {
            int p = 0;
            for (int i = 0; i < s.Length; i++)
            {
                int c = s[i];
                if (c < 0x80) { o[p++] = (byte)c; continue; }
                if (c < 0x800) { o[p++] = (byte)(0xC0 | (c >> 6)); o[p++] = (byte)(0x80 | (c & 63)); continue; }
                if (c >= 0xD800 && c <= 0xDFFF)
                {
                    if (c <= 0xDBFF && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                    {
                        int cp = 0x10000 + ((c - 0xD800) << 10) + (s[++i] - 0xDC00);
                        o[p++] = (byte)(0xF0 | (cp >> 18));
                        o[p++] = (byte)(0x80 | ((cp >> 12) & 63));
                        o[p++] = (byte)(0x80 | ((cp >> 6) & 63));
                        o[p++] = (byte)(0x80 | (cp & 63));
                        continue;
                    }
                    c = 0xFFFD;
                }
                o[p++] = (byte)(0xE0 | (c >> 12));
                o[p++] = (byte)(0x80 | ((c >> 6) & 63));
                o[p++] = (byte)(0x80 | (c & 63));
            }
        }

        public static byte[] Encode(string s)
        {
            var b = new byte[Length(s)];
            Encode(s, b);
            return b;
        }

        /// <summary>Number of UTF-16 units the bytes decode to, or -1 when they are not valid UTF-8.</summary>
        public static int ValidateAndCount(ReadOnlySpan<byte> b)
        {
            int units = 0, i = 0;
            while (i < b.Length)
            {
                int c = b[i];
                if (c < 0x80) { i++; units++; continue; }
                if (c >= 0xC2 && c <= 0xDF)
                {
                    if (i + 1 >= b.Length || !Cont(b[i + 1])) return -1;
                    i += 2; units++; continue;
                }
                if (c >= 0xE0 && c <= 0xEF)
                {
                    if (i + 2 >= b.Length || !Cont(b[i + 1]) || !Cont(b[i + 2])) return -1;
                    int cp = ((c & 15) << 12) | ((b[i + 1] & 63) << 6) | (b[i + 2] & 63);
                    if (cp < 0x800 || (cp >= 0xD800 && cp <= 0xDFFF)) return -1;
                    i += 3; units++; continue;
                }
                if (c >= 0xF0 && c <= 0xF4)
                {
                    if (i + 3 >= b.Length || !Cont(b[i + 1]) || !Cont(b[i + 2]) || !Cont(b[i + 3])) return -1;
                    int cp = ((c & 7) << 18) | ((b[i + 1] & 63) << 12) | ((b[i + 2] & 63) << 6) | (b[i + 3] & 63);
                    if (cp < 0x10000 || cp > 0x10FFFF) return -1;
                    i += 4; units += 2; continue;
                }
                return -1;
            }
            return units;
        }

        /// <summary>Strict decode; null when the bytes are not valid UTF-8.</summary>
        public static string Decode(ReadOnlySpan<byte> b)
        {
            if (ValidateAndCount(b) < 0) return null;
            // Validated above, so the BCL decoder can't substitute anything.
            return b.Length == 0 ? string.Empty : Encoding.UTF8.GetString(b);
        }

        static bool Cont(int c) => (c & 0xC0) == 0x80;
    }

    internal static class BridgeHex
    {
        public static string ToHex(ReadOnlySpan<byte> b)
        {
            var c = new char[b.Length * 2];
            const string digits = "0123456789abcdef";
            for (int i = 0; i < b.Length; i++) { c[i * 2] = digits[b[i] >> 4]; c[i * 2 + 1] = digits[b[i] & 15]; }
            return new string(c);
        }

        public static byte[] FromHex(string h)
        {
            var b = new byte[h.Length / 2];
            for (int i = 0; i < b.Length; i++) b[i] = (byte)((Nib(h[i * 2]) << 4) | Nib(h[i * 2 + 1]));
            return b;
        }

        static int Nib(char c) => c <= '9' ? c - '0' : (c | 0x20) - 'a' + 10;
    }
}
