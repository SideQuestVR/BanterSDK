using System;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

namespace BS
{
    /// <summary>
    /// What a page or world can ask the app to fetch: web addresses, and not ones on the player's own machine or
    /// network unless the world itself is served from there. The editor also takes local files and local addresses, for
    /// testing.
    /// </summary>
    internal static class PageUrl
    {
        internal const string OwnNetworkOnly = "addresses on the player's own machine or network don't load";

        internal static bool MayLoad(string url, bool allowLocalFiles) => allowLocalFiles || IsAllowedWebAddress(url);

        /// <summary>
        /// An http or https address the app may fetch for the current world. One on the player's own machine or network
        /// (see <see cref="IsPrivate(string)"/>) only when the world's own page is too.
        /// </summary>
        internal static bool IsAllowedWebAddress(string url) => Problem(url) == null;

#if UNITY_EDITOR
        const bool InEditor = true;
#else
        const bool InEditor = false;
#endif

        /// <summary>Why <paramref name="url"/> can't be fetched for the current world, or null when it can.</summary>
        internal static string Problem(string url) => Problem(url, BSScene.Current?.CurrentUrl, InEditor);

        /// <summary>The rule, with the world's page and the platform given: for tests.</summary>
        internal static string Problem(string url, string worldUrl, bool editor)
        {
            if (!BSAssetKey.IsWebAddress(url)) return BSAssetKey.WebAddressesOnly;
            if (editor) return null;
            return IsPrivate(url) && !IsPrivate(worldUrl) ? OwnNetworkOnly : null;
        }

        /// <summary>True, with one warning, when <paramref name="url"/> can't be loaded. An empty url isn't refused.</summary>
        internal static bool Refuse(string url, string who)
        {
            if (string.IsNullOrEmpty(url) || MayLoad(url, Application.isEditor))
            {
                return false;
            }
            Debug.LogWarning($"[{who}] {Problem(url) ?? BSAssetKey.WebAddressesOnly}");
            return true;
        }

        /// <summary>
        /// Whether <paramref name="url"/>'s host is the player's own machine or network: localhost, a .local name, or a
        /// loopback, link-local, private (RFC 1918), unique-local or unspecified address. A name that resolves to one
        /// isn't caught: that would mean a lookup per check.
        /// </summary>
        internal static bool IsPrivate(string url)
        {
            if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
            return IsPrivateHost(uri.DnsSafeHost);
        }

        internal static bool IsPrivateHost(string host)
        {
            if (string.IsNullOrEmpty(host)) return false;
            host = host.TrimEnd('.').ToLowerInvariant();
            if (host == "localhost" || host.EndsWith(".localhost", StringComparison.Ordinal) || host.EndsWith(".local", StringComparison.Ordinal)) return true;
            var percent = host.IndexOf('%');
            if (percent >= 0) host = host.Substring(0, percent);
            if (!IPAddress.TryParse(host, out var ip)) return false;
            if (ip.AddressFamily == AddressFamily.InterNetworkV6 && ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
            if (IPAddress.IsLoopback(ip)) return true;
            var b = ip.GetAddressBytes();
            if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            {
                return ip.Equals(IPAddress.IPv6Any) || ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || (b[0] & 0xfe) == 0xfc;
            }
            return b[0] == 0 || b[0] == 10 || b[0] == 127 || (b[0] == 169 && b[1] == 254) || (b[0] == 172 && (b[1] & 0xf0) == 16) || (b[0] == 192 && b[1] == 168);
        }
    }
}
