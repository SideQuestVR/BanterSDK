// <mirror source="Assets/Systems/Networking/State/StatePathCodec.cs" sha256="7e9992c72f02b50d29b1a282993ba53ca6bab82a659f9ec0668e73ffb425e08c" mode="verbatim" />
using System;
using System.Text;
using Newtonsoft.Json.Linq;

namespace BS.LocalMultiplayer.State
{
    /// <summary>
    /// Maps arbitrary SDK space/user-state keys onto Packet Party room-state paths.
    ///
    /// The server's path grammar is strict — dotted segments each matching
    /// <c>^[A-Za-z0-9_\-:@]{1,64}$</c>, at most 256 chars and 64 segments — while SDK keys are
    /// arbitrary strings. Anything outside that grammar comes back as <c>invalid_path</c>.
    ///
    /// The mapping is a hybrid, deliberately:
    /// <list type="bullet">
    /// <item>A segment already inside the allowlist (minus <c>:</c>) passes through VERBATIM, so
    /// the common case — <c>videoUrl</c>, <c>playing</c>, <c>round-2</c> — is byte-identical on the
    /// wire and stays readable in the dashboard, the admin API and any server-side bot.</item>
    /// <item>Anything else becomes <c>':' + Base64Url(UTF8(key))</c>. Base64url's alphabet
    /// (<c>A-Za-z0-9-_</c>) is already inside the server allowlist, so no second-order escaping is
    /// needed, and the two branches can never collide because <c>:</c> is excluded from the
    /// verbatim set — decoding is a single first-character test.</item>
    /// </list>
    ///
    /// Normalisation was rejected: lower-casing or replacing illegal characters is not injective,
    /// so <c>my key</c>, <c>my-key</c> and <c>my_key</c> would collapse onto one path — and one of
    /// them could then be permanently protected, locking the others out for the room's lifetime.
    ///
    /// <c>.</c> is a real separator on BOTH the legacy and the new surface. If the legacy surface
    /// treated it as opaque, <c>SetPublicSpaceProps({"video.url": x})</c> and
    /// <c>SpaceStateSet("video.url", x)</c> would write different paths — a trap the first time
    /// anyone mixes them. As a separator everything round-trips, and the page keeps Banter's model
    /// of a flat map with dotted names.
    /// </summary>
    public static class StatePathCodec
    {
        /// <summary>Marks an escaped segment. Legal in a server segment, excluded from the verbatim set.</summary>
        public const char EscapePrefix = ':';

        public const int MaxSegmentChars = 64;
        public const int MaxPathChars = 256;
        public const int MaxSegments = 64;

        /// <summary>
        /// 47 UTF-8 bytes encode to 63 base64url chars, which plus the ':' prefix is exactly the
        /// 64-char segment limit.
        /// </summary>
        public const int MaxEscapedKeyBytes = 47;

        /// <summary>Verbatim alphabet: the server's allowlist minus ':' (reserved as the escape marker).</summary>
        private static bool IsVerbatimChar(char c) =>
            (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') ||
            c == '_' || c == '-' || c == '@';

        private static bool IsVerbatim(string segment)
        {
            if (string.IsNullOrEmpty(segment) || segment.Length > MaxSegmentChars) return false;
            foreach (char c in segment)
            {
                if (!IsVerbatimChar(c)) return false;
            }
            return true;
        }

        /// <summary>Encode one key with no dot handling. <paramref name="reason"/> is author-facing.</summary>
        public static bool TryEncodeSegment(string key, out string segment, out string reason)
        {
            segment = null;
            reason = null;

            if (string.IsNullOrEmpty(key))
            {
                reason = "a state key segment cannot be empty";
                return false;
            }

            if (IsVerbatim(key))
            {
                segment = key;
                return true;
            }

            // Over-long-but-legal keys are rejected rather than escaped: escaping only inflates them.
            if (key.Length > MaxSegmentChars && IsAllVerbatimChars(key))
            {
                reason = $"'{key}' is {key.Length} characters; the limit is {MaxSegmentChars} per dotted part";
                return false;
            }

            // UTF-8 encoding replaces a lone surrogate with U+FFFD, so "\uD800" and "\uDC00" would
            // produce the SAME segment -- two distinct keys collapsing onto one path, where one
            // could then be permanently protected and silently lock the other. Refuse instead.
            if (HasUnpairedSurrogate(key))
            {
                reason = $"'{key}' contains an unpaired surrogate and cannot be encoded unambiguously";
                return false;
            }

            int bytes = Encoding.UTF8.GetByteCount(key);
            if (bytes > MaxEscapedKeyBytes)
            {
                reason = $"'{key}' needs escaping (it uses characters outside A-Z a-z 0-9 _ - @) and is " +
                         $"{bytes} UTF-8 bytes; escaped keys are limited to {MaxEscapedKeyBytes}";
                return false;
            }

            segment = EscapePrefix + ToBase64Url(Encoding.UTF8.GetBytes(key));
            return true;
        }

        /// <summary>True when the string contains a surrogate that is not part of a valid pair.</summary>
        private static bool HasUnpairedSurrogate(string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1])) return true;
                    i++; // consume the pair
                }
                else if (char.IsLowSurrogate(c))
                {
                    return true; // a low surrogate with no high before it
                }
            }
            return false;
        }

        private static bool IsAllVerbatimChars(string key)
        {
            foreach (char c in key)
            {
                if (!IsVerbatimChar(c)) return false;
            }
            return true;
        }

        /// <summary>Reverse of <see cref="TryEncodeSegment"/>. False when the segment is malformed.</summary>
        public static bool TryDecodeSegment(string segment, out string key)
        {
            key = null;
            if (string.IsNullOrEmpty(segment)) return false;

            if (segment[0] != EscapePrefix)
            {
                key = segment;
                return true;
            }

            try
            {
                string decoded = Encoding.UTF8.GetString(FromBase64Url(segment.Substring(1)));
                // Re-encode and require an exact match. Without this the mapping is not injective:
                // non-canonical base64 and byte sequences that are not valid UTF-8 both decode to
                // something (invalid bytes fold onto U+FFFD), so several distinct server paths
                // would alias onto ONE SDK key -- and one of them could be permanently protected,
                // silently locking the others.
                if (!TryEncodeSegment(decoded, out string canonical, out _) || canonical != segment) return false;
                key = decoded;
                return true;
            }
            catch (Exception)
            {
                // A path we did not write (another room-state user, or a hand-edited value).
                return false;
            }
        }

        /// <summary>
        /// Encode a caller's dotted key into a server path: split on '.', encode each part, rejoin.
        /// </summary>
        public static bool TryEncodePath(string dottedKey, out string path, out string reason)
        {
            path = null;
            reason = null;

            if (string.IsNullOrEmpty(dottedKey))
            {
                reason = "a state key cannot be empty";
                return false;
            }

            string[] parts = dottedKey.Split('.');
            if (parts.Length > MaxSegments)
            {
                reason = $"'{dottedKey}' has {parts.Length} dotted parts; the limit is {MaxSegments}";
                return false;
            }

            var builder = new StringBuilder(dottedKey.Length + 8);
            for (int i = 0; i < parts.Length; i++)
            {
                if (!TryEncodeSegment(parts[i], out string segment, out reason)) return false;
                if (i > 0) builder.Append('.');
                builder.Append(segment);
            }

            if (builder.Length > MaxPathChars)
            {
                reason = $"'{dottedKey}' encodes to {builder.Length} characters; the limit is {MaxPathChars}";
                return false;
            }

            path = builder.ToString();
            return true;
        }

        /// <summary>
        /// Reverse of <see cref="TryEncodePath"/>. False for a path we could not have written —
        /// which is how the bridge filters out room state belonging to other systems.
        /// </summary>
        public static bool TryDecodePath(string path, out string dottedKey)
        {
            dottedKey = null;
            if (string.IsNullOrEmpty(path)) return false;

            string[] parts = path.Split('.');
            var builder = new StringBuilder(path.Length);
            for (int i = 0; i < parts.Length; i++)
            {
                if (!TryDecodeSegment(parts[i], out string key)) return false;
                if (i > 0) builder.Append('.');
                builder.Append(key);
            }

            dottedKey = builder.ToString();
            return true;
        }

        /// <summary>
        /// Encode every OBJECT MEMBER NAME in a value, because the server flattens a nested object
        /// into <c>path.member.member</c> — so each member name becomes a path segment and must
        /// satisfy the same grammar as the top-level key.
        ///
        /// Without this, <c>SpaceStateSet("game", {"my key": 1})</c> produces the illegal segment
        /// "my key". Worse, a replace sends delete+set non-atomically for a public write, so the
        /// delete lands and the set is rejected: the existing subtree is destroyed and nothing
        /// replaces it. Encoding here keeps arbitrary member names working and round-trips
        /// automatically, since reads decode every segment.
        /// </summary>
        public static bool TryEncodeMemberNames(JToken value, out JToken encoded, out string reason)
        {
            reason = null;
            encoded = null;

            if (value == null || value.Type != JTokenType.Object)
            {
                // Arrays are stored as opaque leaves by the server, so their contents need no
                // encoding; scalars likewise.
                encoded = value;
                return true;
            }

            var source = (JObject)value;
            var result = new JObject();
            foreach (var property in source.Properties())
            {
                if (!TryEncodeSegment(property.Name, out string segment, out reason)) return false;
                if (!TryEncodeMemberNames(property.Value, out JToken child, out reason)) return false;
                result[segment] = child;
            }
            encoded = result;
            return true;
        }

        // Base64url, unpadded: the standard alphabet with +/ swapped for -_ and '=' dropped.
        private static string ToBase64Url(byte[] bytes)
        {
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static byte[] FromBase64Url(string value)
        {
            string padded = value.Replace('-', '+').Replace('_', '/');
            switch (padded.Length % 4)
            {
                case 2: padded += "=="; break;
                case 3: padded += "="; break;
                case 1: throw new FormatException("invalid base64url length");
            }
            return Convert.FromBase64String(padded);
        }
    }
}
