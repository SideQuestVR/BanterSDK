// <mirror source="Assets/Systems/Networking/State/SdkWireCodec.cs" sha256="1c1d3ec92a1d7fe3e402487f0486f540d5ce12070fd7a1f7aa487ed943144517" mode="verbatim" />
using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BS.LocalMultiplayer.State
{
    /// <summary>
    /// Value/­string conversions between Packet Party room state and the SDK's string bus.
    ///
    /// Two directions with deliberately different rules:
    /// <list type="bullet">
    /// <item><b>Legacy surface</b> (<c>fss!</c> / <c>spc!</c> / <c>upc!</c>) is string-only and
    /// unescaped, so delimiters are stripped on the way OUT to the page.</item>
    /// <item><b>New JSON surface</b> travels as base64, so nothing needs stripping and real JSON
    /// survives intact.</item>
    /// </list>
    ///
    /// The critical rule: <b>never sanitise on the way IN.</b> The previous bridge stripped
    /// delimiters in its enqueue path, so the mangled value was what got PERSISTED — corrupting it
    /// for every other client, dashboard and future JS-direct reader, irrecoverably. Sanitising is
    /// a presentation concern for one legacy transport, not a storage concern.
    /// </summary>
    public static class SdkWireCodec
    {
        /// <summary>
        /// BS.MessageDelimiters PRIMARY/SECONDARY/TERTIARY/BATCH/WINDOW. The pipe splits on these,
        /// so a legacy value carrying one would corrupt the frame.
        /// <c>:</c> (REQUEST_ID) is deliberately absent — it is the path-escape marker and is legal
        /// mid-message.
        /// </summary>
        private static readonly char[] Delimiters = { '¶', '§', '|', '‽', '¤' };

        /// <summary>Server cap: 16 KiB of UTF-8, measured on the encoded form.</summary>
        public const int MaxValueBytes = 16 * 1024;

        /// <summary>Reserved names: never written to the server or accepted from the page. Only ss_err reaches the page.</summary>
        public const string RevisionKey = "ss_rev";
        public const string ProtectedKeysKey = "ss_protected";
        public const string CanProtectKey = "ss_can_protect";
        public const string ErrorKey = "ss_err";

        private static readonly HashSet<string> Sentinels = new HashSet<string>(StringComparer.Ordinal)
        {
            RevisionKey, ProtectedKeysKey, CanProtectKey, ErrorKey
        };

        /// <summary>True for a reserved sentinel name, which must never round-trip to the server.</summary>
        public static bool IsSentinel(string key) => key != null && Sentinels.Contains(key);

        /// <summary>
        /// Render a stored value for the legacy string-only surface. Byte-identical to the previous
        /// bridge's behaviour for strings — that identity is the backwards-compatibility guarantee.
        /// A JSON leaf renders as compact JSON so a page can still <c>JSON.parse</c> it, rather than
        /// degrading to "[object Object]".
        /// </summary>
        public static string Render(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Undefined)
                return string.Empty;
            if (token.Type == JTokenType.String)
                return token.Value<string>() ?? string.Empty;
            return token.ToString(Formatting.None);
        }

        /// <summary>
        /// Parse a legacy string into a stored value. Legacy writes stay strings: auto-detecting
        /// JSON here would silently change behaviour for pages that legitimately store
        /// JSON-looking text. The new API is where real JSON enters.
        /// </summary>
        public static JToken ParseLegacy(string value) => JValue.CreateString(value ?? string.Empty);

        /// <summary>
        /// Strip bus delimiters. <b>Outbound legacy frames only</b> — see the class remarks.
        /// </summary>
        public static string San(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            if (value.IndexOfAny(Delimiters) < 0) return value;

            var builder = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                if (Array.IndexOf(Delimiters, c) < 0) builder.Append(c);
            }
            return builder.ToString();
        }

        /// <summary>True when a value would exceed the server's per-value cap.</summary>
        public static bool ExceedsValueLimit(JToken token, out int bytes)
        {
            bytes = Encoding.UTF8.GetByteCount(token == null ? "null" : token.ToString(Formatting.None));
            return bytes > MaxValueBytes;
        }

        /// <summary>UTF-8 JSON as base64 — the new API's delimiter-proof envelope.</summary>
        public static string ToBase64Json(JToken token)
        {
            string json = (token ?? JValue.CreateNull()).ToString(Formatting.None);
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        }

        /// <summary>Reverse of <see cref="ToBase64Json"/>. Null when the payload is malformed.</summary>
        public static JToken FromBase64Json(string base64)
        {
            if (string.IsNullOrEmpty(base64)) return null;
            try
            {
                return JToken.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(base64)));
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
