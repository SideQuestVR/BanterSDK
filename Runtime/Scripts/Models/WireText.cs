using System.Text;

namespace BS
{
    /// <summary>
    /// Free text on the JS-Unity string bus. The bus splits on the <see cref="MessageDelimiters"/>
    /// characters without escaping, so any field that can carry a creator's or a user's text goes
    /// through <see cref="Encode"/> on one side and <see cref="Decode"/> on the other.
    ///
    /// Encoding happens only when it's needed. Text with no delimiter that doesn't start with a quote
    /// travels unchanged, which is almost all of it. Anything else travels as a JSON string literal
    /// with each delimiter written as \uXXXX, so it no longer contains one; the receiver recognises
    /// it by the leading quote. That rule is unambiguous because raw text never starts with a quote.
    ///
    /// The JSON string codec is hand-written rather than Newtonsoft: a JsonTextReader cost about
    /// 4 µs a value on the editor's Mono, and text without a backslash decodes here by dropping the
    /// quotes.
    ///
    /// Mirror of Injection~/src/game/wire-text.ts; keep the two in step.
    /// </summary>
    public static class WireText
    {
        /// <summary>
        /// Text as it should travel on the bus. Returns the same instance when nothing needs
        /// encoding.
        ///
        /// Unlike the JS side, this also encodes text with a line break: Unity-to-page delivery
        /// hasn't been verified line-break-safe on every platform (Windows is), and the escape
        /// costs nothing. The decoder doesn't care what triggered the encoding.
        /// </summary>
        public static string Encode(string value)
        {
            if (string.IsNullOrEmpty(value)) return value ?? string.Empty;
            if (value[0] != '"' && !NeedsEncoding(value)) return value;

            var builder = new StringBuilder(value.Length + 16);
            builder.Append('"');
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    default:
                        if (c < ' ' || IsDelimiter(c)) AppendUnicodeEscape(builder, c);
                        else builder.Append(c);
                        break;
                }
            }
            builder.Append('"');
            return builder.ToString();
        }

        /// <summary>
        /// The text an <see cref="Encode"/> (or the JS encodeWireText, i.e. JSON.stringify)
        /// produced. Anything that isn't an encoded value, such as raw text, comes back unchanged.
        /// </summary>
        public static string Decode(string value)
        {
            if (string.IsNullOrEmpty(value) || value[0] != '"') return value;
            int last = value.Length - 1;
            if (last < 1 || value[last] != '"') return value;

            int firstEscape = value.IndexOf('\\', 1, last - 1);
            if (firstEscape < 0)
            {
                // No escapes: only an inner quote would make it something other than one string.
                return value.IndexOf('"', 1, last - 1) < 0 ? value.Substring(1, last - 1) : value;
            }

            var builder = new StringBuilder(last);
            builder.Append(value, 1, firstEscape - 1);
            for (int i = firstEscape; i < last; i++)
            {
                char c = value[i];
                if (c == '"') return value;
                if (c != '\\')
                {
                    builder.Append(c);
                    continue;
                }
                if (++i >= last) return value;
                switch (value[i])
                {
                    case '"': builder.Append('"'); break;
                    case '\\': builder.Append('\\'); break;
                    case '/': builder.Append('/'); break;
                    case 'n': builder.Append('\n'); break;
                    case 'r': builder.Append('\r'); break;
                    case 't': builder.Append('\t'); break;
                    case 'b': builder.Append('\b'); break;
                    case 'f': builder.Append('\f'); break;
                    case 'u':
                        if (i + 4 >= last || !TryParseHex4(value, i + 1, out var code)) return value;
                        builder.Append(code);
                        i += 4;
                        break;
                    default:
                        return value;
                }
            }
            return builder.ToString();
        }

        /// <summary>
        /// Writes each delimiter in a JSON document as a \uXXXX escape. Delimiters can only occur
        /// inside JSON string literals, where the escape decodes to the same character, so the
        /// result is still valid JSON and parses to the same value. Returns the same instance when
        /// there is no delimiter.
        /// </summary>
        public static string EscapeDelimiters(string json)
        {
            if (string.IsNullOrEmpty(json) || !HasDelimiter(json)) return json;
            var builder = new StringBuilder(json.Length + 16);
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (IsDelimiter(c)) AppendUnicodeEscape(builder, c);
                else builder.Append(c);
            }
            return builder.ToString();
        }

        /// <summary>True when <paramref name="value"/> holds a character the bus splits on.</summary>
        public static bool HasDelimiter(string value)
        {
            if (value == null) return false;
            for (int i = 0; i < value.Length; i++)
            {
                if (IsDelimiter(value[i])) return true;
            }
            return false;
        }

        static bool NeedsEncoding(string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '\n' || c == '\r' || IsDelimiter(c)) return true;
            }
            return false;
        }

        // ¶ PRIMARY, § SECONDARY, | TERTIARY, ‽ BATCH, ¤ WINDOW. Every one but | is at or above
        // U+00A4, so ordinary ASCII text costs two comparisons a character.
        static bool IsDelimiter(char c) =>
            c == '|' || (c >= '¤' && (c == '¤' || c == '§' || c == '¶' || c == '‽'));

        const string HexDigits = "0123456789abcdef";

        static void AppendUnicodeEscape(StringBuilder builder, char c)
        {
            builder.Append('\\').Append('u')
                .Append(HexDigits[(c >> 12) & 0xF]).Append(HexDigits[(c >> 8) & 0xF])
                .Append(HexDigits[(c >> 4) & 0xF]).Append(HexDigits[c & 0xF]);
        }

        static bool TryParseHex4(string text, int start, out char value)
        {
            int code = 0;
            for (int i = start; i < start + 4; i++)
            {
                char h = text[i];
                int digit = h >= '0' && h <= '9' ? h - '0'
                    : h >= 'a' && h <= 'f' ? h - 'a' + 10
                    : h >= 'A' && h <= 'F' ? h - 'A' + 10
                    : -1;
                if (digit < 0)
                {
                    value = '\0';
                    return false;
                }
                code = (code << 4) | digit;
            }
            value = (char)code;
            return true;
        }
    }
}
