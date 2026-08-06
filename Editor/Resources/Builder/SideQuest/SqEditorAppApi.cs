using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using UnityEngine;
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
        // Static HttpClient for connection pooling and better performance
        private static readonly HttpClient _httpClient = new HttpClient(new HttpClientHandler
        {
            // Disable automatic decompression to avoid conflicts
            AutomaticDecompression = System.Net.DecompressionMethods.None
        })
        {
            Timeout = TimeSpan.FromMinutes(20) // Global timeout
        };

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
        public IEnumerator PostAvatar(Action<SqEditorAvatar> OnCompleted, Action<Exception> OnError, long highId, long lowId, long screenshotId, string name, bool ispublic)
        {
            if (Data.Token == null)
            {
                OnError?.Invoke(new SqEditorApiAuthException("No user logged in."));
                yield break;
            }
            yield return JsonPost<SqEditorAvatar>($"/v2/avatars", new SqEditorAvatar() { HighId = highId, LowId = lowId, PreviewImage = screenshotId, Public = ispublic, Version = 2, Name = name}, (av) =>
            {
                OnCompleted?.Invoke(av);
            }, OnError, true, false);
        }
        public IEnumerator UpdateAvatar(Action<SqEditorAvatar> OnCompleted, Action<Exception> OnError, long avatarId, long highId, long lowId, long screenshotId, string name, bool ispublic)
        {
            if (Data.Token == null)
            {
                OnError?.Invoke(new SqEditorApiAuthException("No user logged in."));
                yield break;
            }
            yield return JsonPost<SqEditorAvatar>($"/v2/avatars/{avatarId}", new SqEditorAvatar() { HighId = highId, LowId = lowId, PreviewImage = screenshotId, Public = ispublic, Version = 2, Name = name}, (av) =>
            {
                OnCompleted?.Invoke(av);
            }, OnError, true, false, "PUT");
        }
        public IEnumerator GetAvatars(Action<List<SqEditorAvatar>> OnCompleted, Action<Exception> OnError)
        {
            if (Data.Token == null)
            {
                OnError?.Invoke(new SqEditorApiAuthException("No user logged in."));
                yield break;
            }
            yield return JsonGet<List<SqEditorAvatar>>($"/v2/avatars/mine", OnCompleted, OnError, true);
        }
        
         public IEnumerator AttachAvatar(Action<SqAvatarSlot> OnCompleted, Action<Exception> OnError, long avatarId, bool isSelected)
        {
            if (Data.Token == null)
            {
                OnError?.Invoke(new SqEditorApiAuthException("No user logged in."));
                yield break;
            }
            yield return JsonPost<SqAvatarSlot>($"/v2/users/me/avatars", new SqAvatarSlot() { AvatarId = avatarId, IsSelected = true}, OnCompleted, OnError, true, false);
        }
        public IEnumerator SelectAvatar(Action OnCompleted, Action<Exception> OnError, long userAvatarId)
        {
            if (Data.Token == null)
            {
                OnError?.Invoke(new SqEditorApiAuthException("No user logged in."));
                yield break;
            }
            yield return JsonPost<SqAvatarSlot>($"/v2/users/me/avatars/{userAvatarId}", new SqAvatarSlotSelect() { IsSelected = true}, (u) =>
            {
                OnCompleted?.Invoke();
            }, OnError, true, false,"PATCH");
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

        /// <summary>
        /// PUT body that reports how far it has got as it streams, so callers can
        /// show real upload progress instead of a stepped guess.
        /// Note: this counts bytes handed to the transport, not bytes acknowledged
        /// by the server, so it can reach 100% slightly before the PUT returns.
        /// </summary>
        private sealed class ProgressableByteArrayContent : HttpContent
        {
            private const int ChunkSize = 64 * 1024;
            private readonly byte[] _data;
            private readonly Action<float> _onProgress;

            public ProgressableByteArrayContent(byte[] data, string contentType, Action<float> onProgress)
            {
                _data = data;
                _onProgress = onProgress;
                if (!string.IsNullOrEmpty(contentType))
                    Headers.ContentType = new MediaTypeHeaderValue(contentType);
            }

            protected override async Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext context)
            {
                if (_data.Length == 0)
                {
                    _onProgress?.Invoke(1f);
                    return;
                }
                var sent = 0;
                while (sent < _data.Length)
                {
                    var count = Math.Min(ChunkSize, _data.Length - sent);
                    await stream.WriteAsync(_data, sent, count);
                    sent += count;
                    _onProgress?.Invoke((float)sent / _data.Length);
                }
            }

            protected override bool TryComputeLength(out long length)
            {
                length = _data.Length;
                return true;
            }
        }

        public IEnumerator UploadFileToCommunity(string name, byte[] data, string spaceSlug, Action<SqEditorCreateUpload> OnCompleted, Action<Exception> OnError, UploadAssetType assetType, UploadAssetTypePlatform assetPlatform, Action<float> OnProgress = null)
        {
            SqEditorCreateUpload _uploadRequest = null;
            yield return GetUploadRequest((uploadRequest) => _uploadRequest = uploadRequest, OnError, name, data.Length, spaceSlug);

            if (_uploadRequest == null)
            {
                OnError?.Invoke(new SqEditorApiException("Failed to get upload request"));
                yield break;
            }

            yield return UploadFileInternal(_uploadRequest, data, name, (text) => { }, OnError, OnProgress);

            yield return AttachToCommmunity(() => OnCompleted?.Invoke(_uploadRequest), OnError, _uploadRequest.CommunitiesId, _uploadRequest.FileId, name, assetType, assetPlatform);
        }

        public IEnumerator UploadFile(string name, byte[] data, string spaceSlug, Action<SqEditorCreateUpload> OnCompleted, Action<Exception> OnError, Action<float> OnProgress = null)
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

            yield return UploadFileInternal(_uploadRequest, data, name, (text) => { }, OnError, OnProgress);
            OnCompleted?.Invoke(_uploadRequest);

        }

        private IEnumerator UploadFileInternal(SqEditorCreateUpload upload, byte[] data, string name, Action<long> OnCompleted, Action<Exception> OnError, Action<float> OnProgress = null)
        {
            // The upload runs on a worker thread, so it may not touch UI. It only
            // ever writes this cell; the coroutine below reads it on the main
            // thread each frame and is the only thing that calls OnProgress.
            var latest = new float[1];

            // Start the async upload task
            var uploadTask = UploadFileWithRetryAsync(upload, data, name, maxRetries: 3, onProgress: p => latest[0] = p);

            // Wait for the task to complete (bridges async/await with coroutine)
            var lastReported = -1f;
            while (!uploadTask.IsCompleted)
            {
                var current = latest[0];
                if (OnProgress != null && current - lastReported >= 0.01f)
                {
                    lastReported = current;
                    OnProgress(current);
                }
                yield return null;
            }

            // Check for exceptions
            if (uploadTask.IsFaulted)
            {
                var ex = uploadTask.Exception?.InnerException ?? uploadTask.Exception;
                OnError?.Invoke(ex);
                yield break;
            }

            // Return the response code
            OnCompleted?.Invoke(uploadTask.Result);
        }

        private async Task<long> UploadFileWithRetryAsync(SqEditorCreateUpload upload, byte[] data, string name, int maxRetries = 3, Action<float> onProgress = null)
        {
            int attempt = 0;
            Exception lastException = null;

            while (attempt < maxRetries)
            {
                attempt++;
                try
                {
                    LogLine.Do($"Uploading {data.Length} bytes to {upload.UploadURI} (attempt {attempt}/{maxRetries})");

                    // A retry re-sends from the start, so rewind the bar too.
                    onProgress?.Invoke(0f);

                    using (var content = new ProgressableByteArrayContent(data, upload.ContentType, onProgress))
                    using (var request = new HttpRequestMessage(HttpMethod.Put, upload.UploadURI))
                    {
                        request.Content = content;

                        // Set timeout for this specific request
                        using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10)))
                        {
                            var response = await _httpClient.SendAsync(request, cts.Token);

                            // Handle HTTP errors
                            if (!response.IsSuccessStatusCode)
                            {
                                var responseBody = await response.Content.ReadAsStringAsync();

                                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                                    response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                                {
                                    throw new SqEditorApiAuthException((int)response.StatusCode,
                                        $"HTTP {response.StatusCode}: {response.ReasonPhrase} - {responseBody}");
                                }

                                throw new SqEditorApiException(
                                    $"HTTP {response.StatusCode}: {response.ReasonPhrase} - {responseBody}");
                            }

                            LogLine.Do($"Upload completed successfully (attempt {attempt})");
                            return (long)response.StatusCode;
                        }
                    }
                }
                catch (HttpRequestException ex)
                {
                    lastException = ex;
                    LogLine.Do($"Upload failed with HttpRequestException: {ex.Message}");
                }
                catch (TaskCanceledException ex)
                {
                    lastException = ex;
                    LogLine.Do($"Upload timeout after 5 minutes: {ex.Message}");
                }
                catch (SqEditorApiAuthException)
                {
                    // Don't retry auth exceptions
                    throw;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    LogLine.Do($"Upload failed with exception: {ex.Message}");
                }

                // If this wasn't the last attempt, wait before retrying with exponential backoff
                if (attempt < maxRetries)
                {
                    var delaySeconds = Math.Pow(2, attempt); // 2, 4, 8 seconds
                    LogLine.Do($"Retrying in {delaySeconds} seconds...");
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
                }
            }

            // All retries exhausted
            throw new SqEditorApiNetworkException(
                $"Upload failed after {maxRetries} attempts. Last error: {lastException?.Message}",
                lastException);
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
            }, OnError, true, false, "PUT");
        }

        private IEnumerator GetUploadRequest(Action<SqEditorCreateUpload> OnCompleted, Action<Exception> OnError, string name, long numOfBytes, string spaceSlug)
        {
            if (Data.Token == null)
            {
                OnError?.Invoke(new SqEditorApiAuthException("No user logged in."));
                yield break;
            }
            yield return JsonPost<SqEditorCreateUpload>($"/create-upload", new SqEditorCreateUploadRequest() { Size = numOfBytes, SpaceSlug = spaceSlug, Type = Path.GetExtension(name).Replace(".",""), Name = name }, (u) =>

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
            yield return PostFormEncodedStringNoAuth<SqEditorTokenInfo>("/v2/oauth/token", $"grant_type=refresh_token&refresh_token={Uri.EscapeDataString(Data.Token?.RefreshToken)}&client_id={Data.Token?.ClientId}",
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
            var content = new StringContent(data, Encoding.UTF8, "application/x-www-form-urlencoded");
            var task = _httpClient.PostAsync(new Uri(Config.RootApiUri, urlPath), content);
            while (!task.IsCompleted) yield return null;
            if (task.IsFaulted)
            {
                OnError(new SqEditorApiNetworkException(task.Exception.InnerException?.Message ?? task.Exception.Message));
                yield break;
            }
            var response = task.Result;
            if (!response.IsSuccessStatusCode)
            {
                OnError(new SqEditorApiAuthException((int)response.StatusCode, $"Http Error: {response.ReasonPhrase}"));
                yield break;
            }
            var readTask = response.Content.ReadAsStringAsync();
            while (!readTask.IsCompleted) yield return null;
            if (readTask.IsFaulted) { OnError(readTask.Exception.InnerException ?? readTask.Exception); yield break; }
            var resStr = readTask.Result;
            if (string.IsNullOrWhiteSpace(resStr)) { OnCompleted?.Invoke(default(T)); yield break; }
            try
            {
                OnCompleted?.Invoke(JsonConvert.DeserializeObject<T>(resStr));
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Failed deserializing response from API", ex);
                OnError?.Invoke(ex);
            }
        }

        private IEnumerator JsonGet<T>(string urlPath, Action<T> OnCompleted, Action<Exception> OnError, bool withAuth = true)
        {
            string authToken = null;
            if (Data?.Token != null && withAuth)
            {
                Exception error = null;
                yield return GetAuthToken((a) => authToken = a, (e) => error = e);
                if (error != null) { OnError?.Invoke(error); yield break; }
            }
            var request = new HttpRequestMessage(HttpMethod.Get, new Uri(Config.RootApiUri, urlPath));
            request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            if (authToken != null)
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", authToken);
            var task = _httpClient.SendAsync(request);
            while (!task.IsCompleted) yield return null;
            if (task.IsFaulted)
            {
                OnError(new SqEditorApiNetworkException(task.Exception.InnerException?.Message ?? task.Exception.Message));
                yield break;
            }
            var response = task.Result;
            if (!response.IsSuccessStatusCode)
            {
                OnError(new SqEditorApiAuthException((int)response.StatusCode, $"Http Error: {response.ReasonPhrase}"));
                yield break;
            }
            var readTask = response.Content.ReadAsStringAsync();
            while (!readTask.IsCompleted) yield return null;
            if (readTask.IsFaulted) { OnError(readTask.Exception.InnerException ?? readTask.Exception); yield break; }
            var resStr = readTask.Result;
            if (string.IsNullOrWhiteSpace(resStr)) { OnCompleted?.Invoke(default(T)); yield break; }
            try
            {
                OnCompleted?.Invoke(JsonConvert.DeserializeObject<T>(resStr));
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Failed deserializing response from API", ex);
                OnError?.Invoke(ex);
            }
        }
        
        
        private IEnumerator JsonPost<T>(string urlPath, object data, Action<T> OnCompleted, Action<Exception> OnError, bool withAuth = true, bool isCdn = false, string method = "POST")
        {
            var uri = new Uri(isCdn ? Config.RootCdnUri : Config.RootApiUri, urlPath);
            string authToken = null;
            if (Data?.Token != null && withAuth)
            {
                Exception error = null;
                yield return GetAuthToken((a) => authToken = a, (e) => error = e);
                if (error != null) { OnError?.Invoke(error); yield break; }
            }
            var request = new HttpRequestMessage(new HttpMethod(method), uri)
            {
                Content = new StringContent(JsonConvert.SerializeObject(data), Encoding.UTF8, "application/json")
            };
            if (authToken != null)
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", authToken);
            var task = _httpClient.SendAsync(request);
            while (!task.IsCompleted) yield return null;
            if (task.IsFaulted)
            {
                OnError(new SqEditorApiNetworkException(task.Exception.InnerException?.Message ?? task.Exception.Message));
                yield break;
            }
            var response = task.Result;
            if (!response.IsSuccessStatusCode)
            {
                var errReadTask = response.Content.ReadAsStringAsync();
                while (!errReadTask.IsCompleted) yield return null;
                OnError(new SqEditorApiAuthException((int)response.StatusCode, $"Http Error: {uri} {response.ReasonPhrase} {errReadTask.Result}"));
                yield break;
            }
            if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            {
                OnCompleted?.Invoke(default(T));
                yield break;
            }
            var readTask = response.Content.ReadAsStringAsync();
            while (!readTask.IsCompleted) yield return null;
            if (readTask.IsFaulted) { OnError(readTask.Exception.InnerException ?? readTask.Exception); yield break; }
            var resStr = readTask.Result;
            if (string.IsNullOrWhiteSpace(resStr)) { OnCompleted?.Invoke(default(T)); yield break; }
            try
            {
                OnCompleted?.Invoke(JsonConvert.DeserializeObject<T>(resStr));
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Failed deserializing response from API", ex);
                OnError?.Invoke(ex);
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
