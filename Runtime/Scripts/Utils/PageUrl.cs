using UnityEngine;

namespace BS
{
    /// <summary>
    /// What a page or world can ask the app to fetch: web addresses. The editor also takes local files, for testing.
    /// </summary>
    internal static class PageUrl
    {
        internal static bool MayLoad(string url, bool allowLocalFiles) => allowLocalFiles || BSAssetKey.IsWebAddress(url);

        /// <summary>True, with one warning, when <paramref name="url"/> can't be loaded. An empty url isn't refused.</summary>
        internal static bool Refuse(string url, string who)
        {
            if (string.IsNullOrEmpty(url) || MayLoad(url, Application.isEditor))
            {
                return false;
            }
            Debug.LogWarning($"[{who}] {BSAssetKey.WebAddressesOnly}");
            return true;
        }
    }
}
