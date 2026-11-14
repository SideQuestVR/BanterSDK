using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;
using Object = UnityEngine.Object;

namespace BS
{
    [Serializable]
    public class Community
    {
        public string communities_id;
        public string name;
        public string icon;
    }

    /// <summary>The fields a portal needs from GET /v2/worlds/by-url. The images are CDN-relative paths.</summary>
    [Serializable]
    internal class SpaceWorld
    {
        public string worlds_id;
        public string name;
        public string image_url;
        public string banner_url;
    }

    [Serializable]
    public class MetaData
    {
        public string author;
        public string date;
        public string description;
        public string image;
        public string logo;
        public string video;
        public string publisher;
        public string title;
        public string url;
    }

    [Serializable]
    public class SqAvatar
    {
        public long avatars_id;
        public long high_avatar_files_id;
        public long low_avatar_files_id;
        public string created_at;
        public string last_modified;
        public string version;
        public bool is_public;
        public long? preview_image;
        public bool is_selected;
        public long author_users_id;
        public string name;
    }

    [Serializable]
    public class UserAvatar
    {
        public long user_avatars_id;
        public long avatars_id;
        public long clone_from_user_id;
        public long high_avatar_files_id;
        public long low_avatar_files_id;
        public string created_at;
        public string last_modified;
        public string version;
        public bool is_public;
        public long? preview_image;
        public bool is_selected;
        public long author_users_id;
        public string name;
    }

    public enum EnvType
    {
        PROD,
        TEST,
        WIP,
        LOCAL
    }
    public enum UrlType
    {
        API,
        CDN,
        WS
    }
    public class Get : MonoBehaviour
    {
        public static string GetUrl(EnvType envType, UrlType urlType)
        {

            switch (envType)
            {
                case EnvType.PROD:
                    switch (urlType)
                    {
                        case UrlType.API:
                            return "https://api.sidequestvr.com";
                        case UrlType.CDN:
                            return "https://cdn.sidequestvr.com";
                        case UrlType.WS:
                            return "wss://ws.sidequestvr.com";
                    }
                    break;
                case EnvType.TEST:
                    switch (urlType)
                    {
                        case UrlType.API:
                            return "https://api.sidetestvr.com";
                        case UrlType.CDN:
                            return "https://cdn.sidetestvr.com";
                        case UrlType.WS:
                            return "wss://ws.sidetestvr.com";
                    }
                    break;
                case EnvType.WIP:
                    switch (urlType)
                    {
                        case UrlType.API:
                            return "https://api.friedquest.com";
                        case UrlType.CDN:
                            return "https://cdn.friedquest.com";
                        case UrlType.WS:
                            return "wss://ws.friedquest.com";
                    }
                    break;
                case EnvType.LOCAL:
                    switch (urlType)
                    {
                        case UrlType.API:
                            return "http://localhost:3000";
                        case UrlType.CDN:
                            return "http://localhost:3001";
                        case UrlType.WS:
                            return "ws://localhost:3008";
                    }
                    break;
            }
            return null;
        }
        private static Regex ExtExtractor = new Regex("\\.(\\w{3,4})($|\\?)");
        static ConcurrentDictionary<string, UnityEngine.Object> objectCache = new ConcurrentDictionary<string, UnityEngine.Object>();

        // UnityWebRequest.timeout defaults to 0, which means "wait forever". A half-open socket
        // (flaky wifi, captive portal, stalled CDN) therefore never completes, and because a
        // space's load is gated on every component reporting loaded, one stuck request hangs the
        // whole world load with no error and no recovery.
        //
        // These are TOTAL request deadlines, so they are only safe on small payloads. Large
        // downloads (asset bundles, audio) must not use a total deadline — a slow-but-healthy
        // connection would be killed mid-download. Those use the stall detector below instead.
        const int SMALL_REQUEST_TIMEOUT_SECONDS = 30;

        /// <summary>Seconds a large download may make zero progress before we treat it as stalled.</summary>
        const float LARGE_DOWNLOAD_STALL_SECONDS = 60f;

        public static void Clear()
        {
            foreach (var obj in objectCache)
            {
                if (obj.Value != null)
                {
                    GameObject.Destroy(obj.Value);
                }
            }
            objectCache.Clear();
            // A download still in flight will add one orphan entry when it lands; harmless.
            textureDownloads.Clear();
        }

        /// <summary>
        /// Lets go of one cached download without destroying it: something else may hold the object, and Unity reclaims it
        /// once nothing does. The next request downloads it again. A newer download under the URL stays.
        /// </summary>
        internal static void Forget(string url, UnityEngine.Object asset)
        {
            ((ICollection<KeyValuePair<string, UnityEngine.Object>>)objectCache).Remove(new KeyValuePair<string, UnityEngine.Object>(url, asset));
        }

        /// <summary>
        /// Texture downloads in flight, keyed like <see cref="objectCache"/>. Every BSMaterial that
        /// shares a texture asks for it in the same frame, and the result cache alone cannot help
        /// them: each would miss, download, and TryAdd — with all but one copy leaked. Main thread only.
        /// </summary>
        static readonly Dictionary<string, Task<Texture2D>> textureDownloads = new Dictionary<string, Task<Texture2D>>();

        public static Task<Texture2D> Texture(string url)
        {
            return Texture(url, false, false);
        }

        /// <summary>
        /// Downloads (or returns the cached) texture at <paramref name="url"/>.
        /// </summary>
        /// <param name="linear">
        /// Create the texture as linear data rather than sRGB colour. Required for normal, roughness
        /// and AO maps, which are otherwise gamma-decoded on sampling and come out wrong.
        /// </param>
        /// <param name="mipmaps">Generate a mip chain. Tiled textures at VR grazing angles shimmer without one.</param>
        /// <remarks>
        /// The cache key includes both flags, so the same URL can legitimately be held twice (as an
        /// sRGB albedo and as a linear mask, say). Neither flag is expressible through
        /// <c>DownloadHandlerTexture</c>, which always yields an sRGB texture with no mips, so those
        /// requests fetch bytes and decode through <c>Texture2D.LoadImage</c> instead.
        /// </remarks>
        public static async Task<Texture2D> Texture(string url, bool linear, bool mipmaps)
        {
            var key = (linear || mipmaps) ? url + "|" + (linear ? "linear" : "srgb") + (mipmaps ? "|mips" : "") : url;
            LogLine.Do("Checking cache for texture: " + key);
            if (objectCache.TryGetValue(key, out Object value))
            {
                // Sometimes the cached object gets destroyed explicitly elsewhere (MipMaps.Do), so we need to check for null
                if (value == null)
                {
                    objectCache.TryRemove(key, out _);
                }
                else return (Texture2D)value;
            }
            if (textureDownloads.TryGetValue(key, out var inFlight))
            {
                return await inFlight;
            }
            var download = DownloadTexture(url, key, linear, mipmaps);
            textureDownloads[key] = download;
            try
            {
                return await download;
            }
            finally
            {
                textureDownloads.Remove(key);
            }
        }

        static async Task<Texture2D> DownloadTexture(string url, string key, bool linear, bool mipmaps)
        {
            Texture2D texture;
            if (!linear && !mipmaps)
            {
                using (UnityWebRequest uwr = UnityWebRequestTexture.GetTexture(url))
                {
                    await SendWithStallDetection(uwr, url);
                    texture = DownloadHandlerTexture.GetContent(uwr);
                }
            }
            else
            {
                byte[] bytes;
                using (UnityWebRequest uwr = UnityWebRequest.Get(url))
                {
                    await SendWithStallDetection(uwr, url);
                    bytes = uwr.downloadHandler.data;
                }
                // LoadImage resizes and reformats the texture to the image; only the mip-chain and
                // linear flags of the placeholder survive, which is exactly what we want from it.
                texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipmaps, linear);
                if (!texture.LoadImage(bytes, true))
                {
                    Object.Destroy(texture);
                    throw new Exception("Not a loadable image: " + url);
                }
            }
            LogLine.Do("Adding texture to cache: " + key);
            objectCache.TryAdd(key, texture);
            return texture;
        }

        /// <summary>
        /// Sends the request and fails only when the transfer makes no progress for
        /// <see cref="LARGE_DOWNLOAD_STALL_SECONDS"/>. A total deadline (the old 30 s) killed healthy
        /// downloads of large textures — a 4K normal map is 20–40 MB — on slow headset wifi.
        /// </summary>
        static async Task SendWithStallDetection(UnityWebRequest uwr, string url)
        {
            _ = uwr.SendWebRequest();
            var lastBytes = uwr.downloadedBytes;
            var lastProgressAt = Time.realtimeSinceStartup;
            while (!uwr.isDone)
            {
                if (uwr.downloadedBytes != lastBytes)
                {
                    lastBytes = uwr.downloadedBytes;
                    lastProgressAt = Time.realtimeSinceStartup;
                }
                else if (Time.realtimeSinceStartup - lastProgressAt > LARGE_DOWNLOAD_STALL_SECONDS)
                {
                    uwr.Abort();
                    throw new Exception($"Texture download stalled: no data for {LARGE_DOWNLOAD_STALL_SECONDS}s after {lastBytes} bytes from {url}");
                }
                await new WaitForSecondsRealtime(.1f);
            }
            if (uwr.result != UnityWebRequest.Result.Success)
            {
                throw new Exception(uwr.error);
            }
        }
        /// <summary>The page's meta image (og:image) for <paramref name="url"/>, or null.</summary>
        public static async Task<string> PageImage(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            if (url.Contains("?"))
                url = url.Split('?')[0];
            try
            {
                var text = await Text(GetUrl(EnvType.PROD, UrlType.API) + "/v2/urlmetadata?url=" + UnityWebRequest.EscapeURL(url));
                var meta = JsonUtility.FromJson<MetaData>(text);
                return meta != null && !string.IsNullOrEmpty(meta.image) ? meta.image : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// The name and image for the space at <paramref name="url"/>: the world that lives there, else the group
        /// that owns the address, else the page's own meta tags. Null when nothing answers.
        /// </summary>
        public static async Task<Community> SpaceMeta(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            if (url.Contains("?"))
                url = url.Split('?')[0];
            var api = GetUrl(EnvType.PROD, UrlType.API);
            var escaped = UnityWebRequest.EscapeURL(url);

            // Worlds no longer belong to groups, so the world itself is the first place to look.
            try
            {
                var world = JsonUtility.FromJson<SpaceWorld>(await Text(api + "/v2/worlds/by-url?space_url=" + escaped));
                if (world != null && !string.IsNullOrEmpty(world.name))
                    return new Community
                    {
                        name = world.name,
                        icon = Cdn(string.IsNullOrEmpty(world.image_url) ? world.banner_url : world.image_url),
                    };
            }
            catch (Exception) { }

            try
            {
                // TODO: Grab Event if Event is live?
                // This cant really work as the events can have any url now? So the space may not be the same as the event destination?
                // https://api.sidetestvr.com/v2/communities/557/events
                var community = JsonUtility.FromJson<Community>(await Text(api + "/v2/communities/space-info?space_url=" + escaped));
                if (community != null && !string.IsNullOrEmpty(community.name))
                    return community;
            }
            catch (Exception) { }

            try
            {
                var space = JsonUtility.FromJson<MetaData>(await Text(api + "/v2/urlmetadata?url=" + escaped));
                return space == null ? null : new Community { name = space.title, icon = space.image };
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>A stored CDN-relative path as a full CDN url. Full urls pass through; legacy "/assets/..." paths 404 on the CDN, so they give null.</summary>
        internal static string Cdn(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return path;
            if (path.StartsWith("/assets/", StringComparison.OrdinalIgnoreCase)) return null;
            return GetUrl(EnvType.PROD, UrlType.CDN) + "/" + path.TrimStart('/');
        }
        public static async Task<SqAvatar> AvatarDetails(long avatarId)
        {
            try
            {
                var text = await Text(GetUrl(EnvType.PROD, UrlType.API) + $"/v2/avatars/{avatarId}");
                return JsonConvert.DeserializeObject<SqAvatar>(text);
            }
            catch (Exception)
            {
                return null;
            }
        }
        public static async Task<UserAvatar> UserAvatar(long userId, long userAvatarId)
        {
            try
            {
                var text = await Text(GetUrl(EnvType.PROD, UrlType.API) + $"/v2/users/{userId}/avatars");
                List<UserAvatar> avatars = JsonConvert.DeserializeObject<List<UserAvatar>>(text);
                foreach (UserAvatar a in avatars)
                {
                    if (a.user_avatars_id == userAvatarId)
                        return a;
                }

                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }
        /// <summary>
        /// How far <paramref name="request"/> has got, 0 to 1. Without a Content-Length (a chunked or compressed
        /// response) UnityWebRequest's own progress doesn't move, so it is then estimated from the bytes received: below
        /// 1, and moving whenever bytes arrive, which is what the space's load gate watches for (BSLoadGate).
        /// </summary>
        internal static float DownloadProgress(UnityWebRequest request)
        {
            var length = request.GetResponseHeader("Content-Length");
            var lengthKnown = long.TryParse(length, out var bytes) && bytes > 0;
            return DownloadProgress(request.downloadProgress, request.downloadedBytes, lengthKnown);
        }

        /// <summary>The estimate behind <see cref="DownloadProgress(UnityWebRequest)"/>: half way at 4 MiB with no length.</summary>
        internal static float DownloadProgress(float downloadProgress, ulong downloadedBytes, bool lengthKnown)
        {
            if (lengthKnown) return Mathf.Clamp01(downloadProgress);
            const double HalfWayBytes = 4 * 1024 * 1024;
            return (float)(downloadedBytes / (downloadedBytes + HalfWayBytes));
        }

        public static Task<byte[]> Bytes(string url) => Bytes(url, null);

        /// <summary>
        /// <see cref="Bytes(string)"/>, telling <paramref name="progress"/> (main thread) how much of it has arrived,
        /// 0 to 1, about ten times a second. Still a total deadline of SMALL_REQUEST_TIMEOUT_SECONDS.
        /// </summary>
        public static async Task<byte[]> Bytes(string url, Action<float> progress)
        {
            // UnityWebRequest fails "" with just "Malformed URL" (and sends "  " to https://localhost/).
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new ArgumentException("Get.Bytes needs a URL, and was given an empty one.", nameof(url));
            }
            using (UnityWebRequest uwr = UnityWebRequest.Get(url))
            {
                uwr.timeout = SMALL_REQUEST_TIMEOUT_SECONDS;
                var request = uwr.SendWebRequest();
                if (progress == null)
                {
                    await request;
                }
                else
                {
                    while (!request.isDone)
                    {
                        progress(DownloadProgress(uwr));
                        await new WaitForSecondsRealtime(.1f);
                    }
                }
                if (uwr.result != UnityWebRequest.Result.Success)
                {
                    throw new System.Exception(uwr.error);
                }
                else
                {
                    progress?.Invoke(1f);
                    return uwr.downloadHandler.data;
                }
            }
        }
        public static async Task<T> Json<T>(string url)
        {
            UnityWebRequest uwr = UnityWebRequest.Get(url);
            uwr.timeout = SMALL_REQUEST_TIMEOUT_SECONDS;
            await uwr.SendWebRequest();
            if (uwr.result != UnityWebRequest.Result.Success)
            {
                throw new System.Exception(uwr.error);
            }
            else
            {
                return JsonUtility.FromJson<T>(uwr.downloadHandler.text);
            }
        }
        public static async Task<string> Text(string url)
        {
            UnityWebRequest uwr = UnityWebRequest.Get(url);
            uwr.timeout = SMALL_REQUEST_TIMEOUT_SECONDS;
            await uwr.SendWebRequest();

            if (uwr.result != UnityWebRequest.Result.Success)
            {
                throw new System.Exception(uwr.error);
            }
            else
            {
                return uwr.downloadHandler.text;
            }
        }

        public static async Task<AssetBundle> AssetBundle(string url, Action<float> progress = null)
        {
            // Checked as a kit while the pre-load content check is on. BSAssetBundle says what it loads itself, and
            // passes the cancellation its Unload uses (the internal overload below).
            return await AssetBundle(url, progress, System.Threading.CancellationToken.None, BasisPreLoadCheck.Kind.Kit);
        }

        /// <summary>
        /// <see cref="AssetBundle(string, Action{float})"/> for a bundle of
        /// <paramref name="checkKind"/>: while the pre-load content check is on (<see cref="BS.Security.BundlePreloadHooks.Active"/>,
        /// Greenfield) it is downloaded into the app's raw-bundle cache, checked from the file before Unity sees it, and
        /// loaded with LoadFromFileAsync; otherwise (or <see cref="BasisPreLoadCheck.Kind.Unchecked"/>) through Unity's
        /// bundle cache, as before.
        /// </summary>
        /// <param name="cancellation">Stops the download (it throws OperationCanceledException), as when its bundle is unloaded first.</param>
        internal static async Task<AssetBundle> AssetBundle(string url, Action<float> progress,
            System.Threading.CancellationToken cancellation, BasisPreLoadCheck.Kind checkKind)
        {
            checkKind = BS.Security.BundlePreloadHooks.EffectiveKind(checkKind);
            var hash = new Hash128();
            hash.Append(url);
            string lastModified, etag, contentLength;
            using (UnityWebRequest head = UnityWebRequest.Head(url))
            {
                head.timeout = SMALL_REQUEST_TIMEOUT_SECONDS;
                await head.SendWebRequest();
                // GetResponseHeader rather than indexing the GetResponseHeaders() dictionary:
                // that dictionary compares keys ordinally, while HTTP field names are
                // case-insensitive and HTTP/2 and HTTP/3 require them lowercase on the wire.
                // Looking up "Last-Modified" in it therefore misses entirely against an h2 CDN,
                // which silently reduces the cache key to the URL alone — and since that never
                // changes, Unity's bundle cache can never be invalidated and a bundle
                // republished to the same URL is never re-downloaded. The accessor is
                // case-insensitive by contract, so it sees the header whatever the wire casing.
                // A failed request simply yields nulls here rather than throwing.
                lastModified = head.GetResponseHeader("Last-Modified");
                etag = head.GetResponseHeader("ETag");
                contentLength = head.GetResponseHeader("Content-Length");

                // Both are appended when both are present, in the original order, so a client
                // whose header casing already matched keeps the exact hash it had. Preferring one
                // would change every existing key and re-download every cached bundle once.
                if (!string.IsNullOrWhiteSpace(lastModified))
                {
                    hash.Append(lastModified);
                }
                if (!string.IsNullOrWhiteSpace(etag))
                {
                    hash.Append(etag);
                }

                if (string.IsNullOrWhiteSpace(lastModified) && string.IsNullOrWhiteSpace(etag))
                {
                    // Every route to this point leaves the bundle keyed by URL alone and so pinned
                    // in Unity's cache indefinitely. The previous log fired only when the header
                    // dictionary was exactly null, hiding both the "responded, but with no
                    // validators" and "responded in a casing we failed to match" cases — which is
                    // precisely how this stays invisible.
                    LogLine.Do("No cache validator for " + url + " (result=" + head.result +
                               ", HTTP " + head.responseCode +
                               (string.IsNullOrEmpty(head.error) ? "" : ", " + head.error) +
                               ") — versioning bundle by URL only; a republish to this URL will not be picked up.");
                }
            }
            if (checkKind != BasisPreLoadCheck.Kind.Unchecked && BS.Security.BundlePreloadHooks.Active)
            {
                var key = BS.Security.BundleFileCache.KeyFor(url, lastModified, etag);
                long.TryParse(contentLength, out var expectedBytes);
                return await CheckedAssetBundle(url, key, expectedBytes, progress, cancellation, checkKind);
            }
            using (UnityWebRequest web = UnityWebRequestAssetBundle.GetAssetBundle(url, hash))
            {
                progress?.Invoke(0f);
                _ = web.SendWebRequest();

                // Deliberately NOT web.timeout: that is a total deadline and would kill a healthy
                // but slow download of a large bundle. Instead fail only when the transfer makes no
                // progress at all for a sustained period. Realtime clock + WaitForSecondsRealtime so
                // a space that set Time.timeScale = 0 cannot freeze this loop. The same watch as the encrypted (.bee)
                // path's, which also times the silence again after a pause (headset off) instead of failing at once.
                var stall = new BasisIOManagement.DownloadStallWatch(LARGE_DOWNLOAD_STALL_SECONDS, Time.realtimeSinceStartupAsDouble);

                while (!web.isDone)
                {
                    progress?.Invoke(web.downloadProgress);

                    if (cancellation.IsCancellationRequested)
                    {
                        web.Abort();
                        throw new OperationCanceledException("Asset bundle download cancelled: " + url, cancellation);
                    }
                    if (stall.IsStalled(web.downloadedBytes, Time.realtimeSinceStartupAsDouble))
                    {
                        web.Abort();
                        throw new Exception($"Asset bundle download stalled: no data for " +
                                            $"{LARGE_DOWNLOAD_STALL_SECONDS}s after {stall.LastBytes} bytes from {url}");
                    }

                    await new WaitForSecondsRealtime(.1f);
                }
                progress?.Invoke(1f);
                if (web.result != UnityWebRequest.Result.Success)
                {
                    throw new Exception(web.error);
                }
                else
                {
                    var bundle = DownloadHandlerAssetBundle.GetContent(web);
                    if (bundle != null)
                    {
                        return bundle;
                    }
                    else
                    {
                        throw new Exception("Unable to download asset bundle from " + url);
                    }
                }
            }
        }

        /// <summary>
        /// The checked raw path (<see cref="BS.Security.BundleFileLoader"/>). The cache entry is keyed by a SHA-256 of the URL
        /// and its HTTP validators (a republish is downloaded again); its verdict is kept with it. No managed copy of the
        /// bundle is held: the check reads the entry's file on demand (an LZMA download is first recompressed to LZ4 on disk,
        /// as Unity's cache did), and Unity loads that same file, which no download rewrites and nothing deletes while its
        /// bundle may be loaded. Under Enforce a refusal throws, with the message the load fails with.
        /// </summary>
        static async Task<AssetBundle> CheckedAssetBundle(string url, string key, long expectedBytes, Action<float> progress,
            System.Threading.CancellationToken cancellation, BasisPreLoadCheck.Kind checkKind)
        {
            BS.Security.BundlePreloadHooks.PruneUnloaded(); // files of bundles unloaded since may be trimmed now
            var loaded = await BS.Security.BundleFileLoader.LoadAsync(url, key, expectedBytes, BS.Security.BundlePreloadHooks.OptionsFor(checkKind),
                checkKind == BasisPreLoadCheck.Kind.Kit, progress, cancellation, RawBundleSteps);
            BS.Security.BundlePreloadHooks.TrackLoaded(loaded.Entry, loaded.Bundle);
            _ = Task.Run(() => BS.Security.BundleFileCache.Trim());
            return loaded.Bundle;
        }

        /// <summary>Unity's side of the checked raw path: download to a file, recompress LZMA to LZ4, load from the file.</summary>
        static readonly BS.Security.BundleFileLoader.Steps<AssetBundle> RawBundleSteps = new BS.Security.BundleFileLoader.Steps<AssetBundle>
        {
            Download = DownloadToFile,
            Prepare = BS.Security.BundlePreloadHooks.PrepareDownloadAsync,
            Load = async file => await UnityEngine.AssetBundle.LoadFromFileAsync(file),
        };

        /// <summary>
        /// Downloads <paramref name="url"/> to <paramref name="path"/> under the same rules as the bundle download above:
        /// no total deadline, but a stall of <see cref="LARGE_DOWNLOAD_STALL_SECONDS"/> fails it, and the token cancels it.
        /// Nothing is left at <paramref name="path"/> when it fails.
        /// </summary>
        static async Task DownloadToFile(string url, string path, Action<float> progress, System.Threading.CancellationToken cancellation)
        {
            using (var web = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET))
            {
                web.downloadHandler = new DownloadHandlerFile(path) { removeFileOnAbort = true };
                progress?.Invoke(0f);
                _ = web.SendWebRequest();
                var stall = new BasisIOManagement.DownloadStallWatch(LARGE_DOWNLOAD_STALL_SECONDS, Time.realtimeSinceStartupAsDouble);
                while (!web.isDone)
                {
                    progress?.Invoke(web.downloadProgress);
                    if (cancellation.IsCancellationRequested)
                    {
                        web.Abort();
                        throw new OperationCanceledException("Asset bundle download cancelled: " + url, cancellation);
                    }
                    if (stall.IsStalled(web.downloadedBytes, Time.realtimeSinceStartupAsDouble))
                    {
                        web.Abort();
                        throw new Exception($"Asset bundle download stalled: no data for " +
                                            $"{LARGE_DOWNLOAD_STALL_SECONDS}s after {stall.LastBytes} bytes from {url}");
                    }
                    await new WaitForSecondsRealtime(.1f);
                }
                if (web.result != UnityWebRequest.Result.Success)
                {
                    var error = web.error;
                    web.downloadHandler.Dispose(); // closes the file before it is deleted
                    try
                    {
                        if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
                    }
                    catch (Exception)
                    {
                        // a stale partial download is trimmed later
                    }
                    throw new Exception(error);
                }
            }
        }

        /// <summary>
        /// Fetches the first <paramref name="count"/> bytes of <paramref name="url"/> via an HTTP Range
        /// request — used to sniff a bundle's format (raw Unity AssetBundle vs encrypted Basis .bee)
        /// without pulling the whole file. Returns null on failure. Assumes the host honours Range (the
        /// SideQuest CDN does — Basis' own connector prefetch relies on it); a host that ignores it would
        /// return the full body, which we still slice down to the header.
        /// </summary>
        public static Task<byte[]> PeekHeader(string url, int count) => PeekHeader(url, count, SMALL_REQUEST_TIMEOUT_SECONDS);

        /// <summary><see cref="PeekHeader(string, int)"/> with its own timeout (a scene bundle's sniff retries a short one).</summary>
        internal static async Task<byte[]> PeekHeader(string url, int count, int timeoutSeconds)
        {
            using (UnityWebRequest uwr = UnityWebRequest.Get(url))
            {
                uwr.timeout = timeoutSeconds;
                uwr.SetRequestHeader("Range", "bytes=0-" + (count - 1));
                await uwr.SendWebRequest();
                if (uwr.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"[Greenfield/BEE] PeekHeader failed for {url}: {uwr.result} (HTTP {uwr.responseCode}) {uwr.error}");
                    return null;
                }
                byte[] data = uwr.downloadHandler.data;
                if (data == null || data.Length == 0)
                    return null;
                if (data.Length <= count)
                    return data;
                byte[] head = new byte[count];
                Array.Copy(data, head, count);
                return head;
            }
        }

        /// <summary>
        /// Downloads (or reads from disk), decrypts and loads an encrypted Basis <c>.bee</c> bundle,
        /// returning a plain <see cref="AssetBundle"/> the caller owns and unloads — the encrypted
        /// counterpart to <see cref="AssetBundle(string, Action{float})"/>.
        ///
        /// Reuses Basis' full download/cache/decrypt/section-split pipeline via
        /// <c>BasisBeeManagement.HandleBundleAndMetaLoading</c>, but with an unregistered wrapper: that
        /// method only fills <c>wrapper.AssetBundle</c>, it never touches Basis' LoadedBundles registry
        /// or scene-unload bookkeeping (those live in the LoadSceneBundle callers we deliberately skip).
        /// So the bundle's lifecycle is ours, exactly like the raw <c>.banter</c> path — Basis just does
        /// the crypto and disk caching. <paramref name="url"/> may be http(s) or a local path / file://.
        ///
        /// <paramref name="versionTag"/> is an opaque content version for whatever currently lives at
        /// <paramref name="url"/> — see <c>BasisContentVersion.ResolveRequestedTagAsync</c>. Null or empty
        /// leaves the disk cache authoritative, which is what a bundle at a per-upload url wants.
        /// </summary>
        public static async Task<AssetBundle> EncryptedAssetBundle(string url, string password, Action<float> progress = null, string versionTag = null)
        {
            return (await EncryptedAssetBundleWithSection(url, password, progress, versionTag)).bundle;
        }

        /// <summary>
        /// <see cref="EncryptedAssetBundle"/>, plus the connector section that was loaded for this
        /// platform (its build metadata, e.g. <c>RenderingPath</c>). The section is null if the
        /// connector didn't come through, which callers treat like an older bundle.
        /// </summary>
        public static async Task<(AssetBundle bundle, BasisBundleGenerated section)> EncryptedAssetBundleWithSection(
            string url, string password, Action<float> progress = null, string versionTag = null)
        {
            // Checked as a world while the pre-load content check is on. BSAssetBundle says what it loads itself, and
            // passes the cancellation its Unload uses (the internal overload below).
            return await EncryptedAssetBundleWithSection(url, password, progress, versionTag,
                System.Threading.CancellationToken.None, BasisPreLoadCheck.Kind.World);
        }

        /// <summary>
        /// <see cref="EncryptedAssetBundleWithSection(string, string, Action{float}, string)"/>
        /// for a bundle of <paramref name="checkKind"/>: Basis checks the decrypted bytes before it loads them while the
        /// pre-load content check is on (<see cref="BS.Security.BundlePreloadHooks"/>); a refusal throws
        /// <see cref="BasisBundleRefusedException"/>.
        /// </summary>
        /// <param name="cancellation">Handed to Basis, whose downloads stop for it (the load then throws).</param>
        internal static async Task<(AssetBundle bundle, BasisBundleGenerated section)> EncryptedAssetBundleWithSection(
            string url, string password, Action<float> progress, string versionTag,
            System.Threading.CancellationToken cancellation, BasisPreLoadCheck.Kind checkKind)
        {
            checkKind = BS.Security.BundlePreloadHooks.EffectiveKind(checkKind);
            await BasisLoadHandler.EnsureInitializationComplete();

            var loadable = new BasisLoadableBundle { UnlockPassword = password };
            loadable.BasisRemoteBundleEncrypted.RemoteBeeFileLocation = url;
            // Without this the cache is keyed by url alone, so content re-published to a static url
            // is never re-fetched. Empty is the pre-existing behavior and stays a valid answer.
            loadable.BasisRemoteBundleEncrypted.RemoteVersionTag = versionTag;

            var wrapper = new BasisTrackedBundleWrapper { AssetBundle = null, LoadableBundle = loadable };
            var report = new BasisProgressReport();
            if (progress != null)
                report.OnProgressReport += (_, percent, __) => progress(percent / 100f);

            // The token only cancels (an unloaded bundle's download): a stalled download fails inside Basis instead
            // (BasisIOManagement.DownloadStallSeconds, the same 60 s rule as AssetBundle's), and that failure fails the
            // space's load like any bundle error.
            await BasisBeeManagement.HandleBundleAndMetaLoading(wrapper, report, cancellation, checkKind: checkKind);

            if (wrapper.AssetBundle == null)
                throw new Exception("Unable to decrypt/load encrypted bundle from " + url);

            BasisBundleGenerated section = null;
            var connector = wrapper.LoadableBundle.BasisBundleConnector;
            if (connector?.BasisBundleGenerated != null)
                connector.GetPlatform(out section);
            // Answers "which build is this world on?" from any player's log. The host only: a url's query can hold tokens.
            var host = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : "?";
            Debug.Log($"[Greenfield/BEE] {host}: build {connector?.UniqueVersion ?? "?"}, built {connector?.DateOfCreation ?? "?"} UTC.");
            return (wrapper.AssetBundle, section);
        }

        public static async Task<AudioClip> Audio(string url, Action<float> progress = null)
        {
            if (objectCache.TryGetValue(url, out Object value))
            {
                return (AudioClip)value;
            }
            var m = ExtExtractor.Match(url);
            if (!m.Success || m.Groups.Count < 2)
            {
                throw new System.Exception("Couldn't determine audio type from extension in url");
            }
            var cap = m.Groups[1];
            AudioType aType;
            switch (cap.Value.ToLower())
            {
                case "mp3":
                    aType = AudioType.MPEG;
                    break;
                case "wav":
                    aType = AudioType.WAV;
                    break;
                case "ogg":
                    aType = AudioType.OGGVORBIS;
                    break;
                default:
                    throw new System.Exception("Couldn't determine audio type from extension " + cap.Value + " in url");
            }
            using (UnityWebRequest web = UnityWebRequestMultimedia.GetAudioClip(url, aType))
            {
                _ = web.SendWebRequest();

                // Same stall detector as the asset-bundle path: audio can be large, so bound it on
                // "no progress" rather than a total deadline.
                var lastBytes = web.downloadedBytes;
                var lastProgressAt = Time.realtimeSinceStartup;

                while (!web.isDone)
                {
                    progress?.Invoke(web.downloadProgress);

                    if (web.downloadedBytes != lastBytes)
                    {
                        lastBytes = web.downloadedBytes;
                        lastProgressAt = Time.realtimeSinceStartup;
                    }
                    else if (Time.realtimeSinceStartup - lastProgressAt > LARGE_DOWNLOAD_STALL_SECONDS)
                    {
                        web.Abort();
                        throw new System.Exception($"Audio download stalled: no data for " +
                                                   $"{LARGE_DOWNLOAD_STALL_SECONDS}s after {lastBytes} bytes from {url}");
                    }

                    await new WaitForSecondsRealtime(.05f);
                }
                if (web.result != UnityWebRequest.Result.Success)
                {
                    throw new System.Exception(web.error);
                }
                else
                {
                    var clip = DownloadHandlerAudioClip.GetContent(web);
                    if (clip != null)
                    {
                        objectCache.TryAdd(url, clip);
                        return clip;
                    }
                    else
                    {
                        throw new System.Exception("Unable to download audio clip from " + url);
                    }
                }


            }

        }
    }
}
