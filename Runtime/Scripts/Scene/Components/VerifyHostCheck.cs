using System;
using System.Collections;
using System.Text.RegularExpressions;
using UnityEngine;

namespace BS
{
    /// <summary>What a failed <see cref="VerifyHostCheck"/> does to the local player.</summary>
    public enum VerifyHostOutcome
    {
        /// <summary>Move the player to the teleport position, in this world.</summary>
        TeleportToPosition = 0,
        /// <summary>Show the error message and send the player to another world's address.</summary>
        SendToWorld = 1,
    }

    /// <summary>How <see cref="VerifyHostCheck"/> reads the URL it is given.</summary>
    public enum VerifyHostMatch
    {
        /// <summary>The URL is an address, and only its host (and any port) is compared with the page's.</summary>
        Domain = 0,
        /// <summary>The URL is a regular expression, run against the page's entire URL.</summary>
        RegularExpression = 1,
    }

    /// <summary>
    /// The check behind <see cref="BSVerifyHost"/> and the Verify Host node. Asks the space's page where it is running
    /// and, when that isn't the expected place, runs the chosen outcome for the local player.
    ///
    /// This deters copies, it doesn't prevent them: a page built to get past the check can still find a way round it.
    /// </summary>
    public static class VerifyHostCheck
    {
        /// <summary>The error a player sees on Send To World, until the creator writes their own.</summary>
        public const string DefaultErrorMessage = "This world is only available at its official address.";

        const string HostScript = "location.host";
        const string UrlScript = "location.href";
        const int ErrorToastMilliseconds = 8000;

        // A page chooses its own URL and this runs on the main thread, so an expression can't be left to run away on it.
        static readonly TimeSpan ExpressionTimeout = TimeSpan.FromMilliseconds(100);

        /// <summary>
        /// Runs the check as a coroutine. Start it on something that goes when the world does (the component, a graph's
        /// machine), so a late answer can't act on the next world. <paramref name="done"/> gets true when the page was
        /// in the wrong place, and false otherwise, including when the check couldn't tell: a failed read is no proof of
        /// a copy, so it never moves a player.
        /// </summary>
        public static IEnumerator Run(string url, VerifyHostMatch match, VerifyHostOutcome outcome, Vector3 teleportPosition,
                                      string worldUrl, string errorMessage, Action<bool> done = null)
        {
            var scene = BSScene.Instance();
            var isExpression = match == VerifyHostMatch.RegularExpression;

            yield return new WaitUntil(() => CanRun(scene));

            string answer = null;
            var view = scene.link?.pipe?.view;
            if (view == null)
            {
                answer = "";
            }
            else
            {
                // Always called, and not always on the main thread, so it only hands the answer over.
                view.EvaluateJS(isExpression ? UrlScript : HostScript, result => answer = result ?? "");
            }
            yield return new WaitUntil(() => answer != null);

            var page = isExpression ? PageUrlFrom(answer) : PageHostFrom(answer);
            var pageHost = isExpression ? HostOf(page) : page;
            if (pageHost.Length == 0)
            {
                Debug.LogWarning($"[BS] Verify Host couldn't read the page's address, got '{Shown(answer)}'.");
                done?.Invoke(false);
                yield break;
            }
            var isRightPage = IsRightPage(match, url, page);
            if (isRightPage == null)
            {
                Debug.LogWarning(isExpression
                    ? $"[BS] Verify Host needs a valid regular expression, got '{Shown(url)}'."
                    : $"[BS] Verify Host needs an address to check, got '{Shown(url)}'.");
                done?.Invoke(false);
                yield break;
            }
#if UNITY_EDITOR && !GREENFIELD_PROJECT
            // SDK Play Mode serves the world from localhost, which says nothing about where it will be published.
            if (IsLocalPage(pageHost))
            {
                Debug.Log($"[BS] Verify Host is skipped in Play Mode, which runs from {pageHost}.");
                done?.Invoke(false);
                yield break;
            }
#endif
            if (isRightPage == true)
            {
                done?.Invoke(false);
                yield break;
            }

            // The host only: a page's URL can carry tokens in its query, and Player.log can be uploaded.
            Debug.LogWarning(isExpression
                ? $"[BS] Verify Host: this world is running from {pageHost}, which doesn't match its expression."
                : $"[BS] Verify Host: this world is running from {pageHost}, not {HostOf(url)}.");
            RunOutcome(scene, outcome, teleportPosition, worldUrl, errorMessage, pageHost);
            done?.Invoke(true);
        }

        /// <summary>
        /// After the load, never during it: the client ignores a Go To URL while a space is loading, and arriving at the
        /// spawn point would undo a teleport. Not a test for UNITY_READY: a page's late DOM_READY overwrites that state,
        /// and the check would then wait for ever. A failed load never counts.
        /// </summary>
        internal static bool CanRun(BSScene scene) => scene.isReady && !scene.loading;

        /// <summary>
        /// Whether <paramref name="page"/> (the page's host for Domain, its entire URL for a regular expression) is the
        /// right place, or null when <paramref name="url"/> gives nothing to judge by. The pattern decides its own anchors,
        /// as any regular expression does.
        /// </summary>
        internal static bool? IsRightPage(VerifyHostMatch match, string url, string page)
        {
            if (match != VerifyHostMatch.RegularExpression)
            {
                var expected = HostOf(url);
                return expected.Length == 0 ? (bool?)null : expected == page;
            }
            if (string.IsNullOrWhiteSpace(url)) return null;
            try
            {
                return Regex.IsMatch(page, url, RegexOptions.None, ExpressionTimeout);
            }
            catch (ArgumentException)
            {
                return null; // not a valid expression
            }
            catch (RegexMatchTimeoutException)
            {
                return null;
            }
        }

        static void RunOutcome(BSScene scene, VerifyHostOutcome outcome, Vector3 teleportPosition, string worldUrl,
                               string errorMessage, string pageHost)
        {
            if (outcome == VerifyHostOutcome.TeleportToPosition)
            {
                scene.events.OnTeleport.Invoke(teleportPosition, Vector3.zero, true, false);
                return;
            }
            var address = WorldAddressOf(worldUrl);
            if (address.Length == 0)
            {
                Debug.LogWarning($"[BS] Verify Host needs a world to send the player to, got '{Shown(worldUrl)}'.");
                return;
            }
            // Sent to the host it is already on, the world would load, fail the check and send the player again, for ever.
            if (HostOf(address) == pageHost)
            {
                Debug.LogWarning($"[BS] Verify Host won't send the player to '{Shown(address)}': this world is already there.");
                return;
            }
            if (!string.IsNullOrWhiteSpace(errorMessage))
            {
                scene.events.OnToast.Invoke(errorMessage, ErrorToastMilliseconds, 0, Color.red);
            }
            scene.GoToUrl(address);
        }

        /// <summary>
        /// An address as <c>location.host</c> spells it: lower case, the port only when it isn't the default, no scheme
        /// or path. A bare host counts as https. Empty when it isn't an address.
        /// </summary>
        internal static string HostOf(string urlOrHost)
        {
            if (!Uri.TryCreate(WithScheme(urlOrHost), UriKind.Absolute, out var uri)) return "";
            return uri.IsDefaultPort ? uri.IdnHost : uri.IdnHost + ":" + uri.Port;
        }

        /// <summary>An address Go To URL accepts: http or https, a bare host counting as https. Empty when it can't be.</summary>
        internal static string WorldAddressOf(string urlOrHost)
        {
            var text = WithScheme(urlOrHost);
            var usable = Uri.TryCreate(text, UriKind.Absolute, out var uri)
                         && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
            return usable ? text : "";
        }

        static string WithScheme(string urlOrHost)
        {
            var text = urlOrHost?.Trim() ?? "";
            return text.Length == 0 || text.Contains("://") ? text : "https://" + text;
        }

        /// <summary>The host in an EvaluateJS result. Empty when there is no host to compare.</summary>
        internal static string PageHostFrom(string result) => Unquoted(result).ToLowerInvariant();

        /// <summary>
        /// The URL in an EvaluateJS result, as the page has it. Browsers percent-encode quotes and control characters in a
        /// URL, so a backslash in its query is all that JSON can have escaped. Empty when there is no URL.
        /// </summary>
        internal static string PageUrlFrom(string result) => Unquoted(result).Replace("\\\\", "\\");

        /// <summary>
        /// An EvaluateJS result, which is the value as JSON (so quoted), or "error: " and a reason when the script failed.
        /// </summary>
        static string Unquoted(string result)
        {
            if (string.IsNullOrEmpty(result) || result.StartsWith("error: ", StringComparison.Ordinal)) return "";
            return result.Trim().Trim('"');
        }

        /// <summary>True for a page served from this machine, as SDK Play Mode's is.</summary>
        internal static bool IsLocalPage(string pageHost) =>
            Uri.TryCreate("http://" + pageHost, UriKind.Absolute, out var uri) && uri.IsLoopback;

        // Capped: these come from world content, and Player.log can be uploaded.
        static string Shown(string text) => text == null ? "" : text.Length > 200 ? text.Substring(0, 200) + "…" : text;
    }
}
