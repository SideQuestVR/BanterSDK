using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace BS
{
    /// <summary>
    /// Builds the missing-world fallback page: a tiny HTML document, shipped in this package as a
    /// TextAsset, that the space browser is pointed at when a URL turns out not to be a world. It
    /// runs under the SDK JavaScript that Ora injects into every page, so it loads the fallback
    /// world.asset, places the explanation label and opens the user's address in an in-world
    /// browser exactly the way a real world would.
    ///
    /// Served as a base64 data: URL rather than a file so nothing outside this package (Ora's
    /// StreamingAssets, per-platform file paths) has to change. Base64 sidesteps
    /// <c>Uri.EscapeDataString</c>'s length limits and can never contain '#', which matters for
    /// the marker fragment below.
    /// </summary>
    public static class MissingWorldPage
    {
        /// <summary>Resources path (no extension) of the HTML template.</summary>
        public const string TemplateResourcePath = "MissingWorld/missing-world";

        /// <summary>
        /// Appended to the data URL. OraView's loadFailed handler redirects the browser to its own
        /// error.html unless the view's current URL contains "OraDefault/" (OraView.cs, the
        /// loadFailed listener). Carrying that marker in the fragment makes the guard short-circuit
        /// for the fallback page, so a stray failure report from the page we navigated away from
        /// cannot replace the fallback with the error page. The page itself never reads or changes
        /// its hash (a hash change is a navigation to Ora, which would reset the Unity scene).
        /// </summary>
        public const string OraDefaultMarkerFragment = "#OraDefault/";

        public const string DataUrlPrefix = "data:text/html;charset=utf-8;base64,";

        public const string OriginalUrlPlaceholder = "__ORIGINAL_URL__";
        public const string WorldAssetPlaceholder = "__WORLD_ASSET__";
        public const string MessagePlaceholder = "__MESSAGE__";
        public const string SpawnPlaceholder = "__SPAWN__";

        static readonly Regex LeftoverPlaceholder = new Regex("__[A-Z_]+__", RegexOptions.Compiled);

        /// <summary>
        /// A JavaScript string literal (double-quoted) for <paramref name="value"/>. Beyond the
        /// JSON escapes, '&lt;', '&gt;', '&amp;' and the U+2028/2029 line terminators are written
        /// as \uXXXX so that a user-typed URL can never close the inline &lt;script&gt; block or
        /// break the literal.
        /// </summary>
        public static string JsonString(string value)
        {
            var sb = new StringBuilder((value?.Length ?? 0) + 2);
            sb.Append('"');
            if (value != null)
            {
                foreach (var c in value)
                {
                    switch (c)
                    {
                        case '"': sb.Append("\\\""); break;
                        case '\\': sb.Append("\\\\"); break;
                        case '\n': sb.Append("\\n"); break;
                        case '\r': sb.Append("\\r"); break;
                        case '\t': sb.Append("\\t"); break;
                        case '<':
                        case '>':
                        case '&':
                        case '\u2028':
                        case '\u2029':
                            AppendUnicodeEscape(sb, c);
                            break;
                        default:
                            if (c < ' ')
                            {
                                AppendUnicodeEscape(sb, c);
                            }
                            else
                            {
                                sb.Append(c);
                            }
                            break;
                    }
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        static void AppendUnicodeEscape(StringBuilder sb, char c)
        {
            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Fill the template. <paramref name="spawn"/> is (x, y, z, yaw degrees), the same shape as
        /// the SDK's SpawnPoint setting. Throws if a placeholder survives, so a template edit that
        /// introduces a new one cannot ship a page with literal "__X__" in its script.
        /// </summary>
        public static string BuildHtml(string template, string originalUrl, string worldAssetUrl, string message, Vector4 spawn)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            var html = template
                .Replace(OriginalUrlPlaceholder, JsonString(originalUrl ?? ""))
                .Replace(WorldAssetPlaceholder, JsonString(worldAssetUrl ?? ""))
                .Replace(MessagePlaceholder, JsonString(message ?? ""))
                .Replace(SpawnPlaceholder, FormatSpawn(spawn));
            var leftover = LeftoverPlaceholder.Match(html);
            if (leftover.Success)
            {
                throw new InvalidOperationException("Missing-world template still contains placeholder " + leftover.Value);
            }
            return html;
        }

        static string FormatSpawn(Vector4 spawn)
        {
            var inv = CultureInfo.InvariantCulture;
            return "[" + spawn.x.ToString("R", inv) + "," + spawn.y.ToString("R", inv) + "," +
                   spawn.z.ToString("R", inv) + "," + spawn.w.ToString("R", inv) + "]";
        }

        public static string BuildDataUrl(string html)
        {
            if (html == null) throw new ArgumentNullException(nameof(html));
            return DataUrlPrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(html)) + OraDefaultMarkerFragment;
        }

        /// <summary>Main thread only (Resources.Load). Null, with an error logged, if the template asset is missing.</summary>
        public static string BuildUrl(string originalUrl, string worldAssetUrl, string message, Vector4 spawn)
        {
            var template = Resources.Load<TextAsset>(TemplateResourcePath);
            if (template == null)
            {
                LogLine.Err("[LOADING] Missing-world template not found at Resources/" + TemplateResourcePath + " — cannot build the fallback page.");
                return null;
            }
            return BuildDataUrl(BuildHtml(template.text, originalUrl, worldAssetUrl, message, spawn));
        }
    }
}
