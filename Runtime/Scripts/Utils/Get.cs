using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace Banter.SDK
{
    [Serializable]
    public class Community
    {
        public string communities_id;
        public string name;
        public string icon;
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
    public class UserAvatar
    {
        public long user_avatars_id;
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
        static List<UnityEngine.Object> objectCache = new List<UnityEngine.Object>();

        public static void Clear()
        {
            foreach (var obj in objectCache)
            {
                if (obj != null)
                {
                    GameObject.Destroy(obj);
                }
            }
            objectCache.Clear();
        }
        public static async Task<Texture2D> Texture(string url)
        {
            using (UnityWebRequest uwr = UnityWebRequestTexture.GetTexture(url))
            {
                await uwr.SendWebRequest();
                if (uwr.result != UnityWebRequest.Result.Success)
                {
                    throw new System.Exception(uwr.error);
                }
                else
                {
                    var texture = DownloadHandlerTexture.GetContent(uwr);
                    objectCache.Add(texture);
                    return texture;
                }
            }
        }
        public static async Task<Community> SpaceMeta(string url)
        {
            if (url.Contains("?"))
                url = url.Split('?')[0];
            
            try
            {
                var text = await Text(GetUrl(EnvType.PROD, UrlType.API) + "/v2/communities/space-info?space_url=" + UnityWebRequest.EscapeURL(url));
                // TODO: Grab Event if Event is live?
                // This cant really work as the events can have any url now? So the space may not be the same as the event destination? 
                // https://api.sidetestvr.com/v2/communities/557/events
                return JsonUtility.FromJson<Community>(text);
            }
            catch (Exception)
            {
                try
                {
                    var text = await Text(GetUrl(EnvType.PROD, UrlType.API) + "/v2/urlmetadata?url=" + UnityWebRequest.EscapeURL(url));
                    var space = JsonUtility.FromJson<MetaData>(text);
                    return new Community { name = space.title, icon = space.image };
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }
        public static async Task<UserAvatar> UserAvatar(long userId, long userAvatarId)
        {
            try
            {
                var text = await Text(GetUrl(EnvType.TEST, UrlType.API) + $"/v2/users/{userId}/avatars");
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
        public static async Task<byte[]> Bytes(string url)
        {
            UnityWebRequest uwr = UnityWebRequest.Get(url);
            await uwr.SendWebRequest();
            if (uwr.result != UnityWebRequest.Result.Success)
            {
                throw new System.Exception(uwr.error);
            }
            else
            {
                return uwr.downloadHandler.data;
            }
        }
        public static async Task<T> Json<T>(string url)
        {
            UnityWebRequest uwr = UnityWebRequest.Get(url);
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
            var hash = new Hash128();
            hash.Append(url);
            using (UnityWebRequest head = UnityWebRequest.Head(url))
            {
                await head.SendWebRequest();
                var headers = head.GetResponseHeaders();
                if (headers.ContainsKey("Last-Modified"))
                {
                    hash.Append(headers["Last-Modified"]);
                }
                if (headers.ContainsKey("ETag"))
                {
                    hash.Append(headers["ETag"]);
                }
            }
            using (UnityWebRequest web = UnityWebRequestAssetBundle.GetAssetBundle(url, hash))
            {
                progress?.Invoke(0f);
                _ = web.SendWebRequest();

                while (!web.isDone)
                {
                    progress?.Invoke(web.downloadProgress);
                    await new WaitForSeconds(.1f);
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

        public static async Task<AudioClip> Audio(string url, Action<float> progress = null)
        {
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
                while (!web.isDone)
                {
                    progress?.Invoke(web.downloadProgress);
                    await new WaitForSeconds(.05f);
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
                        objectCache.Add(clip);
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
