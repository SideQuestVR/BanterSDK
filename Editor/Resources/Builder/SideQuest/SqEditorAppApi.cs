using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Debug = System.Diagnostics.Debug;
using Unity.EditorCoroutines.Editor;

/// <summary>
/// Class for interacting with the SideQuest API
/// </summary>
/// 

namespace Banter.SDKEditor
{


    public enum UploadAssetType
    {
        AssetBundle = 1,
        Index = 2,
        Js = 3
    }

    public enum UploadAssetTypePlatform
    {
        Any = 0,
        Windows = 1,
        Mac = 2,
        Linux = 3,
        Android = 4,
        iOS = 5,
        VisionOS = 6
    }
    public class SqEditorAppApi
    {
        /// <summary>
        /// Create a new instance
        /// </summary>
        /// <param name="config">The configuration options that should be used</param>
        public SqEditorAppApi(SqEditorAppApiConfig config)
        {
            Config = config;
            LoadData();
        }

        /// <summary>
        /// The currently logged in sidequest user's details, or null if a user is not logged in
        /// </summary>
        public SqEditorUser User
        {
            get
            {
                return Data.User;
            }
        }
    public IEnumerator AttachAvatar(Action OnCompleted, Action<Exception> OnError, long highId, long lowId)
        {
            if (Data.Token == null)
            {
                OnError?.Invoke(new SqEditorApiAuthException("No user logged in."));
                yield break;
            }
            yield return JsonPost<SqEditorUploadAvatars>($"/v2/users/me/avatar/files", new SqEditorUploadAvatars() { HighId = highId, LowId = lowId, Public = true, Version = 2 }, (u) =>
            {
                OnCompleted?.Invoke();
            }, OnError, true, false, true);
        }
        /// <summary>
        /// Get a list of the currently logged in sidequest user's achievements
        /// </summary>
        public List<SqEditorUserAchievement> UserAchievements
        {
            get
            {
                return Data.UserAchievements;
            }
        }

        /// <summary>
        /// The currently active short code information or null if no short code login is in progress
        /// </summary>
        public SqEditorLoginCode CurrentLoginCode
        {
            get
            {
                return Data.LoginCode;
            }
        }

        /// <summary>
        /// The configuration being used
        /// </summary>
        public SqEditorAppApiConfig Config { get; private set; }

        /// <summary>
        /// Clears the current user and any active short code requests
        /// </summary>
        public void Logout()
        {
            var wasUserNull = Data?.Token == null;
            Data.Token = null;
            Data.User = null;
            Data.LoginCode = null;
            Data.UserAchievements = null;

            SaveData();
            if (!wasUserNull)
            {
                //todo: raise some event for this?
            }
        }

        /// <summary>
        /// Clears the current short code login request
        /// </summary>
        public void ClearLoginCode()
        {
            if (Data.LoginCode != null)
            {
                Data.LoginCode = null;
                SaveData();
            }
        }

        /// <summary>
        /// Gets login code information and begins the shortcode login process with default scopes
        /// </summary>
        /// <param name="OnCompleted">Function invoked with the resulting short code login when the call is successful</param>
        /// <param name="OnError">Function invoked with the exception when the call fails</param>
        public IEnumerator GetLoginCode(Action<SqEditorLoginCode> OnCompleted, Action<Exception> OnError)
        {
            yield return GetLoginCode(new string[] { SqEditorAuthScopes.ReadBasicProfile, SqEditorAuthScopes.ReadAppAchievements, SqEditorAuthScopes.WriteAppAchievements,
            SqEditorAuthScopes.User_Friends_Read,
            SqEditorAuthScopes.User_Friends_Write,
            SqEditorAuthScopes.User_RichPresence_Write,
            SqEditorAuthScopes.User_Communities_Read,
            SqEditorAuthScopes.User_Communities_Write,
            SqEditorAuthScopes.User_Messages_Receive,
            SqEditorAuthScopes.User_Messages_Send,
            SqEditorAuthScopes.User_Message_History,
            SqEditorAuthScopes.User_Avatar_Write
}, OnCompleted, OnError);
        }

        /// <summary>
        /// Gets login code information and begins the shortcode login process for requesting specific scopes
        /// </summary>
        /// <param name="scopes">The list of scopes to request from the user</param>
        /// <param name="OnCompleted">Function invoked with the resulting short code login when the call is successful</param>
        /// <param name="OnError">Function invoked with the exception when the call fails</param>
        public IEnumerator GetLoginCode(IEnumerable<string> scopes, Action<SqEditorLoginCode> OnCompleted, Action<Exception> OnError)
        {
            _lastLoginPoll = DateTime.MinValue;
            yield return JsonPost<SqEditorLoginCode>("/v2/oauth/getshortcode", new
            {
                client_id = Config.ClientId,
                scopes = scopes.ToArray()
            }, (c) =>
            {
                Data.LoginCode = c;
                SaveData();
                OnCompleted?.Invoke(c);
            }, (e) =>
            {
                OnError?.Invoke(e);
            }, false);
        }

        /// <summary>
        /// Checks whether a shortcode login (started with GetLoginCode) has been completed by the user
        /// </summary>
        /// <param name="OnCompleted">Invoked when the check completes successfully with the parameters (completed, user).  completed will be false and user will be null until the user completes the login using the short code.  When the short code login is completed by the user, true and the user object will be passed</param>
        /// <param name="OnError">Function invoked with the provoking exception when something goes wrong</param>
        public IEnumerator CheckLoginCodeComplete(Action<bool, SqEditorUser> OnCompleted, Action<Exception> OnError)
        {
            if (Data.LoginCode == null)
            {
                OnError?.Invoke(new InvalidOperationException("There is not a code login in progress"));
                yield break;
            }
            if (DateTimeOffset.Now > Data.LoginCode.ExpiresAt)
            {
                OnError?.Invoke(new SqEditorApiAuthException("Device code has expired"));
                yield break;
            }
            //check to make sure this isn't being called too frequently
            if ((DateTime.Now - _lastLoginPoll).TotalSeconds < Data.LoginCode.PollIntervalSeconds)
            {
                OnCompleted?.Invoke(false, null);
                yield break;
            }
            SqEditorTokenInfo tok = null;
            Exception ex = null;
            yield return JsonPost<SqEditorTokenInfo>("/v2/oauth/checkshortcode", new { code = Data.LoginCode.Code, device_id = Data.LoginCode.DeviceId },
                (t) =>
                {
                    tok = t;
                },
                (e) =>
                {
                    ex = e;
                }, false);
            if (ex == null)
            {
                if (tok == null)
                {
                    _lastLoginPoll = DateTime.Now;
                    OnCompleted?.Invoke(false, null);
                    yield break;
                }
                Data.User = null;
                Data.Token = tok;
                ex = null;
                yield return GetUserProfile((u) =>
                {
                    Data.User = u;
                }, (e) =>
                {
                    ex = e;

                });
                if (ex != null)
                {
                    OnError?.Invoke(ex);
                }
                else
                {
                    Data.LoginCode = null;

                    if (Data?.Token?.GrantedScopes?.Contains(SqEditorAuthScopes.ReadAppAchievements) ?? false)
                    {
                        yield return RefreshUserAchievements(c => { }, e => ex = e);
                        if (ex != null)
                        {
                            OnError?.Invoke(new SqEditorApiException("Unable to refresh achievements", ex));
                            yield break;
                        }
                    }
                    SaveData();
                    OnCompleted?.Invoke(true, Data.User);
                }
            }
            else
            {
                OnError?.Invoke(ex);
            }
        }

        /// <summary>
        /// Refreshes the currently logged in user's profile
        /// </summary>
        /// <param name="OnCompleted">Function invoked with the refreshed user's profile</param>
        /// <param name="OnError">Function invoked with the provoking exception when something goes wrong</param>
        public IEnumerator RefreshUserProfile(Action<SqEditorUser> OnCompleted, Action<Exception> OnError)
        {
            SqEditorUser user = null;
            Exception ex = null;
            yield return GetUserProfile((u) => user = u, e => ex = e);
            if (ex != null)
            {
                OnError(ex);
                yield break;
            }

            if (user?.UserId != Data.Token?.UserId)
            {
                OnError?.Invoke(new SqEditorApiException("User refreshed data does not match user token ID!"));
                yield break;
            }
            Data.User = user;
            SaveData();
            if (Data?.Token?.GrantedScopes?.Contains(SqEditorAuthScopes.ReadAppAchievements) ?? false)
            {
                yield return RefreshUserAchievements(c => { }, e => ex = e);
                if (ex != null)
                {
                    OnError?.Invoke(new SqEditorApiException("Unable to refresh achievements", ex));
                    yield break;
                }
            }
            OnCompleted?.Invoke(user);
        }

        /// <summary>
        /// Refreshes and returns a list of achievements a user has completed for the app
        /// </summary>
        /// <param name="OnCompleted">Function invoked with the refreshed list of user achievements</param>
        /// <param name="OnError">Function invoked with the provoking exception when something goes wrong</param>
        public IEnumerator RefreshUserAchievements(Action<List<SqEditorUserAchievement>> OnCompleted, Action<Exception> OnError)
        {
            List<SqEditorUserAchievement> achievements = null;
            Exception ex = null;
            yield return JsonGet<List<SqEditorUserAchievement>>("/v2/users/me/apps/me/achievements",
                (a) => achievements = a,
                (e) => ex = e,
                true);
            if (ex != null)
            {
                OnError?.Invoke(ex);
                yield break;
            }
            else
            {
                Data.UserAchievements = achievements;
                SaveData();
                OnCompleted?.Invoke(achievements);
            }
        }

        /// <summary>
        /// Refreshes and returns a list of available app achievements that the user may or may not have completed
        /// </summary>
        /// <param name="OnCompleted">Function invoked with the refreshed list of user achievements</param>
        /// <param name="OnError">Function invoked with the provoking exception when something goes wrong</param>
        public IEnumerator GetAppAchievements(Action<List<SqEditorAchievement>> OnCompleted, Action<Exception> OnError)
        {
            List<SqEditorAchievement> achievements = null;
            Exception ex = null;
            yield return JsonGet<List<SqEditorAchievement>>("/v2/apps/me/achievements",
                (a) => achievements = a,
                (e) => ex = e,
                true);
            if (ex != null)
            {
                OnError?.Invoke(ex);
                yield break;
            }
            else
            {
                OnCompleted?.Invoke(achievements);
            }
        }

        /// <summary>
        /// Adds an achievement to a user, optionally throwing an exception if it already exists
        /// </summary>
        /// <param name="achievementID">The ID of the achievement to add to the user</param>
        /// <param name="OnCompleted">Function invoked with the resulting user achievement when adding the achievement to the user has succeeded
        ///                 NOTE: if the user token does not have achievement read scope, null will be returned</param>
        /// <param name="OnError">Function invoked with the provoking exception when something goes wrong.</param>
        /// <param name="throwIfAlreadyExists">If true and an achievement is being added to a user that</param>
        /// <returns></returns>
        public IEnumerator AddUserAchievement(string achievementID, Action<SqEditorUserAchievement> OnCompleted, Action<Exception> OnError, bool throwIfAlreadyExists = false)
        {
            Exception ex = null;

            yield return JsonPost<string>("/v2/users/me/apps/me/achievements", new { achievement_identifier = achievementID, achieved = true }, o =>
                {
                }, e => ex = e, true);
            if (ex != null)
            {
                var apiex = ex as SqEditorApiException;
                if (!(apiex != null && apiex.HttpCode == 409 && !throwIfAlreadyExists))
                {
                    OnError?.Invoke(ex);
                    yield break;
                }
            }
            if (Data?.Token?.GrantedScopes?.Contains(SqEditorAuthScopes.ReadAppAchievements) ?? false)
            {
                yield return RefreshUserAchievements(c =>
                {
                    var found = c.FirstOrDefault(x => string.Compare(achievementID, x.AchievementId, true) == 0);
                    if (found == null)
                    {
                        OnError?.Invoke(new SqEditorApiException("User achievement was added, but was not returned from the server after being added"));
                    }
                    else
                    {
                        OnCompleted(found);
                    }

                }, e =>
                {
                    OnError?.Invoke(e);
                });
            }
            else
            {
                OnCompleted?.Invoke(null);
            }
        }

        private SqEditorPersistentData _data;
        public SqEditorPersistentData Data
        {
            get
            {
                if (_data == null)
                {
                    _data = new SqEditorPersistentData();
                }
                return _data;
            }
            set
            {
                _data = value;
            }
        }

        public IEnumerator UploadFileToCommunity(string name, byte[] data, string spaceSlug, Action<SqEditorCreateUpload> OnCompleted, Action<Exception> OnError, UploadAssetType assetType, UploadAssetTypePlatform assetPlatform)
        {
            SqEditorCreateUpload _uploadRequest = null;
            yield return GetUploadRequest((uploadRequest) => _uploadRequest = uploadRequest, OnError, name, data.Length, spaceSlug);

            if (_uploadRequest == null)
            {
                OnError?.Invoke(new SqEditorApiException("Failed to get upload request"));
                yield break;
            }

            yield return UploadFileInternal(_uploadRequest.UploadURI, data, name, (text) => { }, OnError);

            yield return AttachToCommmunity(() => OnCompleted?.Invoke(_uploadRequest), OnError, _uploadRequest.CommunitiesId, _uploadRequest.FileId, name, assetType, assetPlatform);
        }

        public IEnumerator UploadFile(string name, byte[] data, string spaceSlug, Action<SqEditorCreateUpload> OnCompleted, Action<Exception> OnError)
        {
            SqEditorCreateUpload _uploadRequest = null;
            UnityEngine.Debug.Log("Before Upload");
            yield return GetUploadRequest((uploadRequest) => _uploadRequest = uploadRequest, OnError, name, data.Length, spaceSlug);
            UnityEngine.Debug.Log("After Upload");
            if (_uploadRequest == null)
            {
                OnError?.Invoke(new SqEditorApiException("Failed to get upload request"));
                yield break;
            }

            yield return UploadFileInternal(_uploadRequest.UploadURI, data, name, (text) => { }, OnError);
            OnCompleted?.Invoke(_uploadRequest);

        }

        private IEnumerator UploadFileInternal(string url, byte[] data, string name, Action<long> OnCompleted, Action<Exception> OnError)
        {
            using (UnityWebRequest req = new UnityWebRequest(new Uri(url)))
            {
                req.method = "PUT";
                req.uploadHandler = new UploadHandlerRaw(data);
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.ConnectionError)
                {
                    OnError(new SqEditorApiNetworkException($"Unity Network Error: {req.error}"));
                    yield break;
                }
                else if (req.result == UnityWebRequest.Result.ProtocolError)
                {
                    if (req.responseCode == 401 || req.responseCode == 403)
                    {
                        OnError(new SqEditorApiAuthException((int)req.responseCode, $"Unity Http Error: {req.error} {req.downloadHandler.text}"));
                        yield break;
                    }
                    else
                    {
                        OnError(new SqEditorApiAuthException((int)req.responseCode, $"Unity Http Error: {req.error}"));
                        yield break;
                    }
                }
                try
                {
                    OnCompleted?.Invoke(req.responseCode);
                    yield break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Failed deserializing response from API", ex);
                    OnError?.Invoke(ex);
                    yield break;
                }
            }
        }

        private IEnumerator AttachToCommmunity(Action OnCompleted, Action<Exception> OnError, long CommunitiesId, long fileId, string name, UploadAssetType assetType, UploadAssetTypePlatform assetPlatform)
        {
            if (Data.Token == null)
            {
                OnError?.Invoke(new SqEditorApiAuthException("No user logged in."));
                yield break;
            }
            yield return JsonPost<SqEditorCreateUpload>($"/v2/communities/{CommunitiesId}/assets/type/{(int)assetType}" + (assetType == UploadAssetType.AssetBundle ? $"/platform/{(int)assetPlatform}" : ""), new SqEditorCreateUploadDone() { FileId = fileId, Name = name }, (u) =>
            {
                if (u == null)
                {
                    OnError?.Invoke(new SqEditorApiException("Request could not be retrieved"));
                    return;
                }
                OnCompleted?.Invoke();
            }, OnError, true, false, true);
        }

        private IEnumerator GetUploadRequest(Action<SqEditorCreateUpload> OnCompleted, Action<Exception> OnError, string name, long numOfBytes, string spaceSlug)
        {
            if (Data.Token == null)
            {
                OnError?.Invoke(new SqEditorApiAuthException("No user logged in."));
                yield break;
            }

            yield return JsonPost<SqEditorCreateUpload>($"/create-upload", new SqEditorCreateUploadRequest() { Size = numOfBytes, SpaceSlug = spaceSlug, Type = "", Name = name }, (u) =>
            {
                if (u == null)
                {
                    OnError?.Invoke(new SqEditorApiException("Request could not be retrieved"));
                    return;
                }
                OnCompleted?.Invoke(u);
            }, OnError, true, true);
        }

        private IEnumerator GetUserProfile(Action<SqEditorUser> OnCompleted, Action<Exception> OnError)
        {
            if (Data.Token == null)
            {
                OnError?.Invoke(new SqEditorApiAuthException("No user logged in."));
                yield break;
            }
            //todo: get user
            yield return JsonGet<SqEditorUser>($"/v2/users/me", (u) =>
            {
                if (u == null)
                {
                    OnError?.Invoke(new SqEditorApiException("User could not be retrieved"));
                    return;
                }
                OnCompleted?.Invoke(u);
            }, OnError, true);
        }

        private IEnumerator GetAuthToken(Action<string> OnCompleted, Action<Exception> OnError)
        {
            if (Data?.Token?.AccessTokenExpiresAt == null)
            {
                OnError?.Invoke(new SqEditorApiAuthException("No user is logged in"));
                yield break;
            }
            if (DateTimeOffset.Now < Data.Token.AccessTokenExpiresAt.Value.AddMinutes(-1) && !string.IsNullOrWhiteSpace(Data.Token.AccessToken))
            {
                OnCompleted?.Invoke(Data.Token.AccessToken);
                yield break;
            }
            if (string.IsNullOrWhiteSpace(Data?.Token?.RefreshToken))
            {
                Logout();
                OnError?.Invoke(new SqEditorApiAuthException("User refresh token is missing, logging user out"));
                yield break;
            }
            yield return PostFormEncodedStringNoAuth<SqEditorTokenInfo>("/v2/oauth/token", $"grant_type=refresh_token&refresh_token={UnityWebRequest.EscapeURL(Data.Token?.RefreshToken)}&client_id={Data.Token?.ClientId}",
                (a) =>
                {
                    if (a == null || a.AccessToken == null)
                    {
                        OnError?.Invoke(new SqEditorApiAuthException("Failed to retrieve auth token"));
                        return;
                    }
                    Data.Token.AccessToken = a.AccessToken;
                    Data.Token.AccessTokenExpiresAt = a.AccessTokenExpiresAt;
                    SaveData();
                    OnCompleted?.Invoke(Data.Token.AccessToken);
                }, OnError);
        }

        private IEnumerator PostFormEncodedStringNoAuth<T>(string urlPath, string data, Action<T> OnCompleted, Action<Exception> OnError)
        {
            using (UnityWebRequest req = new UnityWebRequest(new Uri(Config.RootApiUri, urlPath)))
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(data))
                {
                    contentType = "application/x-www-form-urlencoded"
                };
                req.method = "POST";
                req.downloadHandler = new DownloadHandlerBuffer();

                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.ConnectionError)
                {
                    OnError(new SqEditorApiNetworkException($"Unity Network Error: {req.error}"));
                    yield break;
                }
                else if (req.result == UnityWebRequest.Result.ProtocolError)
                {
                    if (req.responseCode == 401 || req.responseCode == 403)
                    {
                        OnError(new SqEditorApiAuthException((int)req.responseCode, $"Unity Http Error: {req.error}"));
                        yield break;
                    }
                    else
                    {
                        OnError(new SqEditorApiAuthException((int)req.responseCode, $"Unity Http Error: {req.error}"));
                        yield break;
                    }
                }

                var resStr = req.downloadHandler.text;
                if (string.IsNullOrWhiteSpace(resStr))
                {
                    OnCompleted?.Invoke(default(T));
                    yield break;
                }
                else
                {
                    try
                    {
                        OnCompleted?.Invoke(JsonConvert.DeserializeObject<T>(resStr));
                        yield break;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("Failed deserializing response from API", ex);
                        OnError?.Invoke(ex);
                        yield break;
                    }
                }
            }
        }

        private IEnumerator JsonGet<T>(string urlPath, Action<T> OnCompleted, Action<Exception> OnError, bool withAuth = true)
        {
            using (UnityWebRequest req = UnityWebRequest.Get(new Uri(Config.RootApiUri, urlPath)))
            {
                req.SetRequestHeader("Content-Type", "application/json");
                if (Data?.Token != null && withAuth)
                {
                    string authToken = null;
                    Exception error = null;
                    yield return GetAuthToken((a) => authToken = a, (e) => error = e);
                    if (error != null)
                    {
                        OnError?.Invoke(error);
                        yield break;
                    }
                    req.SetRequestHeader("Authorization", "Bearer " + authToken);
                }

                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.ConnectionError)
                {
                    OnError(new SqEditorApiNetworkException($"Unity Network Error: {req.error}"));
                    yield break;
                }
                else if (req.result == UnityWebRequest.Result.ProtocolError)
                {
                    if (req.responseCode == 401 || req.responseCode == 403)
                    {
                        OnError(new SqEditorApiAuthException((int)req.responseCode, $"Unity Http Error: {req.error}"));
                        yield break;
                    }
                    else
                    {
                        OnError(new SqEditorApiAuthException((int)req.responseCode, $"Unity Http Error: {req.error}"));
                        yield break;
                    }
                }
                var resStr = req.downloadHandler.text;
                if (string.IsNullOrWhiteSpace(resStr))
                {
                    OnCompleted?.Invoke(default(T));
                    yield break;
                }
                else
                {
                    try
                    {
                        OnCompleted?.Invoke(JsonConvert.DeserializeObject<T>(resStr));
                        yield break;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("Failed deserializing response from API", ex);
                        OnError?.Invoke(ex);
                        yield break;
                    }
                }
            }
        }

        private IEnumerator JsonPost<T>(string urlPath, object data, Action<T> OnCompleted, Action<Exception> OnError, bool withAuth = true, bool isCdn = false, bool isPut = false)
        {
            // The whole UnitytWebRequest.Put then changing method to POST thing is a janky workaround for JSON posting being broken in Unity...
            var uri = new Uri(isCdn ? Config.RootCdnUri : Config.RootApiUri, urlPath);
            using (UnityWebRequest req = UnityWebRequest.Put(uri, JsonConvert.SerializeObject(data)))
            {
                req.method = isPut ? "PUT" : "POST";
                req.SetRequestHeader("Content-Type", "application/json");
                if (Data?.Token != null && withAuth)
                {
                    string authToken = null;
                    Exception error = null;
                    yield return GetAuthToken((a) => authToken = a, (e) => error = e);
                    if (error != null)
                    {
                        OnError?.Invoke(error);
                        yield break;
                    }
                    req.SetRequestHeader("Authorization", "Bearer " + authToken);
                }

                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.ConnectionError)
                {
                    OnError(new SqEditorApiNetworkException($"Unity Network Error: {req.error}"));
                    yield break;
                }
                else if (req.result == UnityWebRequest.Result.ProtocolError)
                {
                    if (req.responseCode == 401 || req.responseCode == 403)
                    {
                        OnError(new SqEditorApiAuthException((int)req.responseCode, $"Unity Http Error: {uri} {req.error} {req.downloadHandler.text}"));
                        yield break;
                    }
                    else
                    {
                        OnError(new SqEditorApiAuthException((int)req.responseCode, $"Unity Http Error: {uri} {req.error} {req.downloadHandler.text}"));
                        yield break;
                    }
                }
                if (req.responseCode == 204)
                {
                    OnCompleted?.Invoke(default(T));
                    yield break;
                }
                var resStr = req.downloadHandler.text;
                if (string.IsNullOrWhiteSpace(resStr))
                {
                    OnCompleted?.Invoke(default(T));
                    yield break;
                }
                else
                {
                    try
                    {
                        OnCompleted?.Invoke(JsonConvert.DeserializeObject<T>(resStr));
                        yield break;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("Failed deserializing response from API", ex);
                        OnError?.Invoke(ex);
                        yield break;
                    }
                }
            }
        }



        private DateTime _lastLoginPoll = DateTime.MinValue;


        private void LoadData()
        {

            if (File.Exists(Config.DataFile))
            {
                try
                {
                    var data = JsonConvert.DeserializeObject<SqEditorPersistentData>(File.ReadAllText(Config.DataFile));
                    Data = data;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Failed to load data file", ex);
                }
            }
        }

        private void SaveData()
        {
            File.WriteAllText(Config.DataFile, JsonConvert.SerializeObject(Data));
        }
    }
}
