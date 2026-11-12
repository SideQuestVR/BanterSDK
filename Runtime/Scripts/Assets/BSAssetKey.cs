using System;

namespace BS
{
    internal enum BSAssetKeyKind { Runtime, World, Kit, Library, Url, Browser }

    /// <summary>
    /// The name of an asset in a world: <c>world:</c>, <c>kit:</c>, <c>lib:</c>, <c>url:</c>, <c>browser:</c> or
    /// <c>runtime:</c>, a bare http(s) address, or an <c>asset_</c> id. <c>asset_browser_world</c> and
    /// <c>asset_browser_&lt;cid&gt;</c> are the older spelling of the browser keys, and read as the same key.
    /// </summary>
    internal readonly struct BSAssetKey
    {
        const string WorldScheme = "world:", KitScheme = "kit:", LibraryScheme = "lib:", UrlScheme = "url:",
            BrowserScheme = "browser:", RuntimeScheme = "runtime:";
        const string AssetIdPrefix = "asset_", LegacyBrowserPrefix = "asset_browser_";

        /// <summary>Longest key or page id a script can use: far past any real one, and every search reads them all.</summary>
        internal const int MaxLength = 4096;

        /// <summary>What a script hears when its address isn't a web address.</summary>
        internal const string WebAddressesOnly = "only http and https addresses load";

        internal readonly BSAssetKeyKind Kind;
        /// <summary>What follows the scheme, as written (with forward slashes in bundle and library paths).</summary>
        internal readonly string Path;
        /// <summary>The form two spellings of one asset share: the database's own key.</summary>
        internal readonly string Text;

        BSAssetKey(BSAssetKeyKind kind, string path, string text)
        {
            Kind = kind;
            Path = path;
            Text = text;
        }

        /// <summary>Bundle and library paths are case-insensitive to Unity, and lower case in a bundle.</summary>
        static BSAssetKey Folded(BSAssetKeyKind kind, string scheme, string path)
        {
            path = path.Replace('\\', '/');
            return new BSAssetKey(kind, path, scheme + path.ToLowerInvariant());
        }

        internal static bool TryParse(string text, out BSAssetKey key)
        {
            key = default;
            if (string.IsNullOrEmpty(text)) return false;

            if (text.StartsWith(LegacyBrowserPrefix, StringComparison.Ordinal))
                return Rest(text, LegacyBrowserPrefix, out var legacy) && Make(BSAssetKeyKind.Browser, legacy, BrowserScheme + legacy, out key);
            if (text.StartsWith(AssetIdPrefix, StringComparison.Ordinal))
                return Make(BSAssetKeyKind.Runtime, text, text, out key);

            if (Rest(text, BrowserScheme, out var rest)) return Make(BSAssetKeyKind.Browser, rest, BrowserScheme + rest, out key);
            if (Rest(text, RuntimeScheme, out rest)) return Make(BSAssetKeyKind.Runtime, rest, RuntimeScheme + rest, out key);
            if (Rest(text, UrlScheme, out rest)) return Make(BSAssetKeyKind.Url, rest, UrlScheme + rest, out key);
            if (Rest(text, WorldScheme, out rest)) { key = Folded(BSAssetKeyKind.World, WorldScheme, rest); return true; }
            if (Rest(text, KitScheme, out rest)) { key = Folded(BSAssetKeyKind.Kit, KitScheme, rest); return true; }
            if (Rest(text, LibraryScheme, out rest)) { key = Folded(BSAssetKeyKind.Library, LibraryScheme, rest); return true; }

            if (IsWebAddress(text)) return Make(BSAssetKeyKind.Url, text, UrlScheme + text, out key);
            return false;
        }

        static bool Rest(string text, string scheme, out string rest)
        {
            rest = null;
            if (!text.StartsWith(scheme, StringComparison.OrdinalIgnoreCase) || text.Length == scheme.Length) return false;
            rest = text.Substring(scheme.Length);
            return true;
        }

        static bool Make(BSAssetKeyKind kind, string path, string text, out BSAssetKey key)
        {
            key = new BSAssetKey(kind, path, text);
            return true;
        }

        /// <summary>
        /// What the database stores <paramref name="idOrKey"/> under. An id of no known form is its own key: C# callers
        /// have always been free to choose ids.
        /// </summary>
        internal static string Canonical(string idOrKey) => TryParse(idOrKey, out var key) ? key.Text : idOrKey;

        internal static bool IsWebAddress(string text) =>
            Uri.TryCreate(text, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

        /// <summary>Whether a page may name its own asset <paramref name="id"/>: an asset_ or runtime: id, not another source's key.</summary>
        internal static bool IsPageId(string id) =>
            id != null && id.Length <= MaxLength && TryParse(id, out var key) && key.Kind == BSAssetKeyKind.Runtime;

        /// <summary>A kit key's path inside its bundle: from <c>assets/</c> or <c>packages/</c>, past any bundle name.</summary>
        internal string KitPath
        {
            get
            {
                var lower = Path.ToLowerInvariant();
                var start = RootOf(lower, "assets/");
                var packages = RootOf(lower, "packages/");
                if (start < 0 || (packages >= 0 && packages < start)) start = packages;
                return start < 0 ? lower : lower.Substring(start);
            }
        }

        /// <summary>A kit key's bundle name, when it has one.</summary>
        internal string KitBundle
        {
            get
            {
                var path = KitPath;
                var lower = Path.ToLowerInvariant();
                return lower.Length > path.Length ? lower.Substring(0, lower.Length - path.Length - 1) : null;
            }
        }

        static int RootOf(string path, string root)
        {
            if (path.StartsWith(root, StringComparison.Ordinal)) return 0;
            var inner = path.IndexOf("/" + root, StringComparison.Ordinal);
            return inner < 0 ? -1 : inner + 1;
        }

        public override string ToString() => Text;
    }
}
