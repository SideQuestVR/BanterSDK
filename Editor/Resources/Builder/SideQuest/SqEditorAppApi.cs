using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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

namespace BS.SDKEditor
{


    public enum UploadAssetType
    {
        AssetBundle = 1,
        Index = 2,
        Js = 3,
        // A single platform-agnostic combined bundle (world.asset). Attached to a world with platform 0
        // (Any) via /v2/worlds/{id}/assets/type/4/platform/0 (see AttachToWorld).
        WorldAsset = 4
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

        /// <summary>
        /// Lists the signed-in user's worlds (GET /v2/worlds?users_id={me}&amp;limit={limit}). The user id
        /// comes from the token, which always carries it (works even before the profile is fetched).
        /// </summary>
        public IEnumerator ListWorlds(Action<List<SqEditorWorld>> OnCompleted, Action<Exception> OnError, int limit = 1000)
        {
            if (Data?.Token == null)
            {
                OnError?.Invoke(new SqEditorApiAuthException("No user logged in."));
                yield break;
            }
            long usersId = Data.Token.UserId;
            // Rows are converted one at a time so a single malformed world can't take down the whole
            // list (and with it the upload target dropdown) — skip it and keep the rest.
            yield return JsonGet<JArray>($"/v2/worlds?users_id={usersId}&limit={limit}", rows =>
            {
                var worlds = new List<SqEditorWorld>();
                if (rows != null)
                    foreach (var row in rows)
                    {
                        try { worlds.Add(row.ToObject<SqEditorWorld>()); }
                        catch (Exception ex) { Debug.WriteLine("Skipping malformed world row", ex); }
                    }
                OnCompleted?.Invoke(worlds);
            }, OnError, true);
        }

        /// <summary>
        /// Creates a new world owned by the signed-in user (POST /v2/worlds) and returns the created world.
        /// Born Private (status 100) unless the caller explicitly asks otherwise — matching the API's own
        /// default and the website's create form; pass 1000 for Public. Publishing is a deliberate act on
        /// the world's settings page, not a side effect of every editor test build.
        /// </summary>
        public IEnumerator CreateWorld(string name, Action<SqEditorWorld> OnCompleted, Action<Exception> OnError, int status = 100, int maxOccupancy = 20)
        {
            if (Data?.Token == null)
            {
                OnError?.Invoke(new SqEditorApiAuthException("No user logged in."));
                yield break;
            }
            yield return JsonPost<SqEditorWorld>("/v2/worlds",
                new SqEditorCreateWorldRequest { Name = name, Status = status, MaxOccupancy = maxOccupancy },
                OnCompleted, OnError, true, false);
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
        /// True when a short code login is in progress and the code is past the expiry the server gave us.
        /// The server keeps an approved code redeemable for a while after this, so callers should still
        /// ask it once before treating the code as dead (see CheckLoginCodeComplete).
        /// </summary>
        public bool IsLoginCodeExpired => Data?.LoginCode != null && DateTimeOffset.Now > Data.LoginCode.ExpiresAt;

        /// <summary>
        /// The configuration being used
        /// </summary>
        public SqEditorAppApiConfig Config { get; private set; }

        /// <summary>
        /// Raised when a signed-in session is cleared, whether by an explicit Logout call or because the
        /// API rejected it (expired or revoked refresh token, or a 401 that a refresh could not fix).
        /// Raised on the editor main thread.
        /// </summary>
        public event Action LoggedOut;

        /// <summary>
        /// Clears the current user and any active short code requests
        /// </summary>
        public void Logout()
        {
            var wasSignedIn = Data?.Token != null;
            Data.Token = null;
            Data.User = null;
            Data.LoginCode = null;
            Data.UserAchievements = null;

            SaveData();
            if (wasSignedIn)
            {
                LoggedOut?.Invoke();
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
            // No local expiry check before asking the server: an approved code stays redeemable for a
            // while past expires_at, so a user who approved at T+14:59 and is polled at T+15:01 still
            // gets signed in. Expiry is decided below, once the server has said "nothing yet".
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
                    if (IsLoginCodeExpired)
                    {
                        // Past expiry and the server has nothing for us: this code is dead, mint a new one.
                        OnError?.Invoke(new SqEditorLoginCodeExpiredException("Login code has expired"));
                        yield break;
                    }
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
            else if (ex is SqEditorApiAuthException authEx && authEx.HttpCode == 400)
            {
                // The code row is gone (expired and swept, or never valid). Anything else - network
                // faults, 5xx - is transient and left to the caller to retry.
                OnError?.Invoke(new SqEditorLoginCodeExpiredException(400, "Login code has expired or is invalid"));
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

            yield return AttachToCommmunity(() => OnCompleted?.Invoke(_uploadRequest), OnError, _uploadRequest.CommunitiesId ?? 0, _uploadRequest.FileId, name, assetType, assetPlatform);
        }

        /// <summary>
        /// Uploads a file and attaches it to a WORLD (the greenfield space model): CDN /create-upload scoped
        /// to the world, PUT the bytes, then attach via PUT /v2/worlds/{worlds_id}/assets/type/{type}/platform/{platform}.
        /// For the combined world.asset bundle pass assetType=WorldAsset and platform=Any (0).
        /// </summary>
        public IEnumerator UploadFileToWorld(string name, byte[] data, string worldsId, string worldSlug, Action<SqEditorCreateUpload> OnCompleted, Action<Exception> OnError, UploadAssetType assetType, UploadAssetTypePlatform assetPlatform, Action<float> OnProgress = null)
        {
            SqEditorCreateUpload _uploadRequest = null;
            yield return GetWorldUploadRequest((uploadRequest) => _uploadRequest = uploadRequest, OnError, name, data.Length, worldsId, worldSlug);

            if (_uploadRequest == null)
            {
                OnError?.Invoke(new SqEditorApiException("Failed to get upload request"));
                yield break;
            }

            yield return UploadFileInternal(_uploadRequest, data, name, (text) => { }, OnError, OnProgress);

            yield return AttachToWorld(() => OnCompleted?.Invoke(_uploadRequest), OnError, worldsId, _uploadRequest.FileId, name, assetType, assetPlatform);
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

        // Attaches an already-uploaded file to a world. The world route always carries the /platform segment
        // (unlike the community route), and platform must be 0/Any for index.html, script.js and world.asset.
        private IEnumerator AttachToWorld(Action OnCompleted, Action<Exception> OnError, string worldsId, long fileId, string name, UploadAssetType assetType, UploadAssetTypePlatform assetPlatform)
        {
            if (Data.Token == null)
            {
                OnError?.Invoke(new SqEditorApiAuthException("No user logged in."));
                yield break;
            }
            yield return JsonPost<SqEditorCreateUpload>($"/v2/worlds/{worldsId}/assets/type/{(int)assetType}/platform/{(int)assetPlatform}", new SqEditorCreateUploadDone() { FileId = fileId, Name = name }, (u) =>
            {
                if (u == null)
                {
                    OnError?.Invoke(new SqEditorApiException("Request could not be retrieved"));
                    return;
                }
                OnCompleted?.Invoke();
            }, OnError, true, false, "PUT");
        }

        private IEnumerator GetWorldUploadRequest(Action<SqEditorCreateUpload> OnCompleted, Action<Exception> OnError, string name, long numOfBytes, string worldsId, string worldSlug)
        {
            if (Data.Token == null)
            {
                OnError?.Invoke(new SqEditorApiAuthException("No user logged in."));
                yield break;
            }
            yield return JsonPost<SqEditorCreateUpload>($"/create-upload", new SqEditorCreateWorldUploadRequest() { WorldId = worldsId, WorldSlug = worldSlug, Size = numOfBytes, Type = Path.GetExtension(name).Replace(".", ""), Name = name }, (u) =>
            {
                if (u == null)
                {
                    OnError?.Invoke(new SqEditorApiException("Request could not be retrieved"));
                    return;
                }
                OnCompleted?.Invoke(u);
            }, OnError, true, true);
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

        // Refresh the access token this long before the server's expiry. The long CDN PUT in
        // UploadFileWithRetryAsync carries no bearer token, so the margin only has to cover API calls.
        private static readonly TimeSpan AccessTokenEarlyRefresh = TimeSpan.FromMinutes(5);

        // One refresh at a time: coroutines that need a token while a refresh is in flight wait on this
        // flag and share the outcome instead of each posting to /v2/oauth/token.
        private bool _refreshInFlight;
        private Exception _refreshResult;

        private bool HasValidAccessToken()
        {
            var token = Data?.Token;
            return token?.AccessTokenExpiresAt != null
                && !string.IsNullOrWhiteSpace(token.AccessToken)
                && DateTimeOffset.Now < token.AccessTokenExpiresAt.Value - AccessTokenEarlyRefresh;
        }

        /// <summary>
        /// True when a refresh failed with a status that means the session itself is no longer accepted
        /// (oauth2-server answers 400 for an invalid or revoked refresh token).
        /// </summary>
        private static bool IsSessionRejected(Exception e)
        {
            var code = (e as SqEditorApiAuthException)?.HttpCode;
            return code == 400 || code == 401 || code == 403;
        }

        /// <summary>
        /// Hands back a usable access token, refreshing it first when it is missing, near expiry, or
        /// <paramref name="forceRefresh"/> is set. A refresh the server rejects clears the session (and
        /// raises LoggedOut); a refresh that fails for network or server reasons keeps the session so
        /// the next call can try again.
        /// </summary>
        private IEnumerator GetAuthToken(Action<string> OnCompleted, Action<Exception> OnError, bool forceRefresh = false)
        {
            if (Data?.Token == null)
            {
                OnError?.Invoke(new SqEditorApiAuthException("No user is logged in"));
                yield break;
            }
            if (!forceRefresh && HasValidAccessToken())
            {
                OnCompleted?.Invoke(Data.Token.AccessToken);
                yield break;
            }
            if (_refreshInFlight)
            {
                // Share the refresh another call already started.
                while (_refreshInFlight) yield return null;
                if (_refreshResult != null)
                {
                    OnError?.Invoke(_refreshResult);
                    yield break;
                }
                var shared = Data?.Token?.AccessToken;
                if (string.IsNullOrWhiteSpace(shared))
                {
                    OnError?.Invoke(new SqEditorApiAuthException("No user is logged in"));
                    yield break;
                }
                OnCompleted?.Invoke(shared);
                yield break;
            }
            if (string.IsNullOrWhiteSpace(Data.Token.RefreshToken))
            {
                Logout();
                OnError?.Invoke(new SqEditorApiAuthException("Your session has expired. Please sign in again."));
                yield break;
            }

            _refreshInFlight = true;
            _refreshResult = null;
            try
            {
                SqEditorTokenInfo refreshed = null;
                Exception refreshError = null;
                yield return PostFormEncodedStringNoAuth<SqEditorTokenInfo>("/v2/oauth/token",
                    $"grant_type=refresh_token&refresh_token={Uri.EscapeDataString(Data.Token.RefreshToken)}&client_id={Data.Token.ClientId}",
                    (a) => refreshed = a, (e) => refreshError = e);
                if (refreshError == null && string.IsNullOrWhiteSpace(refreshed?.AccessToken))
                {
                    refreshError = new SqEditorApiAuthException("Failed to retrieve auth token");
                }
                if (refreshError != null)
                {
                    if (IsSessionRejected(refreshError))
                    {
                        // The server no longer accepts the refresh token: the session is dead.
                        Logout();
                        _refreshResult = new SqEditorApiAuthException(((SqEditorApiException)refreshError).HttpCode.Value,
                            "Your session has expired. Please sign in again.", refreshError);
                    }
                    else
                    {
                        // Network or server trouble: keep the session so the next call can try again.
                        _refreshResult = refreshError;
                    }
                }
                else if (Data?.Token != null)
                {
                    // (Token is null here only if something signed out while the refresh was in flight.)
                    var token = Data.Token;
                    token.AccessToken = refreshed.AccessToken;
                    token.AccessTokenExpiresAt = refreshed.AccessTokenExpiresAt;
                    if (!string.IsNullOrWhiteSpace(refreshed.ClientId)) token.ClientId = refreshed.ClientId;
                    if (refreshed.GrantedScopes != null && refreshed.GrantedScopes.Count > 0) token.GrantedScopes = refreshed.GrantedScopes;
                    if (refreshed.UserId != 0) token.UserId = refreshed.UserId;
                    // The refresh grant does not rotate the refresh token; only take one if the server sent it.
                    if (!string.IsNullOrWhiteSpace(refreshed.RefreshToken)) token.RefreshToken = refreshed.RefreshToken;
                    if (refreshed.RefreshTokenExpiresAt != null) token.RefreshTokenExpiresAt = refreshed.RefreshTokenExpiresAt;
                    SaveData();
                }
            }
            finally
            {
                _refreshInFlight = false;
            }
            if (_refreshResult != null)
            {
                OnError?.Invoke(_refreshResult);
                yield break;
            }
            var accessToken = Data?.Token?.AccessToken;
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                OnError?.Invoke(new SqEditorApiAuthException("No user is logged in"));
                yield break;
            }
            OnCompleted?.Invoke(accessToken);
        }

        private IEnumerator PostFormEncodedStringNoAuth<T>(string urlPath, string data, Action<T> OnCompleted, Action<Exception> OnError)
        {
            var content = new StringContent(data, Encoding.UTF8, "application/x-www-form-urlencoded");
            var task = _httpClient.PostAsync(new Uri(Config.RootApiUri, urlPath), content);
            while (!task.IsCompleted) yield return null;
            if (task.IsFaulted || task.IsCanceled)
            {
                // IsCanceled is how HttpClient reports a timeout.
                OnError(new SqEditorApiNetworkException(task.Exception?.InnerException?.Message ?? task.Exception?.Message ?? "Request timed out"));
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

        /// <summary>
        /// Sends a JSON request built by <paramref name="requestFactory"/>, attaching the bearer token when
        /// <paramref name="withAuth"/> is set and a user is signed in. A 401 on an authenticated request
        /// forces one token refresh and resends the request once (the factory runs again because an
        /// HttpRequestMessage cannot be reused); a 401 on the resend means the server no longer accepts
        /// the session, so it is cleared.
        /// </summary>
        private IEnumerator SendJsonRequest<T>(Func<HttpRequestMessage> requestFactory, bool withAuth, Action<T> OnCompleted, Action<Exception> OnError)
        {
            string authToken = null;
            if (withAuth && Data?.Token != null)
            {
                Exception authError = null;
                yield return GetAuthToken((a) => authToken = a, (e) => authError = e);
                if (authError != null) { OnError?.Invoke(authError); yield break; }
            }

            var retriedAuth = false;
            while (true)
            {
                var request = requestFactory();
                if (authToken != null)
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authToken);
                var task = _httpClient.SendAsync(request);
                while (!task.IsCompleted) yield return null;
                if (task.IsFaulted || task.IsCanceled)
                {
                    OnError(new SqEditorApiNetworkException(task.Exception?.InnerException?.Message ?? task.Exception?.Message ?? "Request timed out"));
                    yield break;
                }
                var response = task.Result;

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && withAuth && Data?.Token != null && !retriedAuth)
                {
                    // The server rejected a token we thought was still good (revoked, or signed with a key
                    // it no longer trusts). Refresh regardless of the local expiry and try once more.
                    retriedAuth = true;
                    Exception refreshError = null;
                    yield return GetAuthToken((a) => authToken = a, (e) => refreshError = e, forceRefresh: true);
                    if (refreshError != null) { OnError?.Invoke(refreshError); yield break; }
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    var errReadTask = response.Content.ReadAsStringAsync();
                    while (!errReadTask.IsCompleted) yield return null;
                    var errBody = errReadTask.IsFaulted || errReadTask.IsCanceled ? "" : errReadTask.Result;
                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && withAuth)
                    {
                        if (Data?.Token == null)
                        {
                            OnError(new SqEditorApiAuthException((int)response.StatusCode, "No user is logged in"));
                        }
                        else
                        {
                            // A freshly refreshed token was rejected too; nothing more can be done locally.
                            Logout();
                            OnError(new SqEditorApiAuthException((int)response.StatusCode, "Your session has expired. Please sign in again."));
                        }
                        yield break;
                    }
                    OnError(new SqEditorApiAuthException((int)response.StatusCode, $"Http Error: {request.RequestUri} {response.ReasonPhrase} {errBody}"));
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
                yield break;
            }
        }

        private IEnumerator JsonGet<T>(string urlPath, Action<T> OnCompleted, Action<Exception> OnError, bool withAuth = true)
        {
            var uri = new Uri(Config.RootApiUri, urlPath);
            yield return SendJsonRequest<T>(() =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                return request;
            }, withAuth, OnCompleted, OnError);
        }
        
        
        private IEnumerator JsonPost<T>(string urlPath, object data, Action<T> OnCompleted, Action<Exception> OnError, bool withAuth = true, bool isCdn = false, string method = "POST")
        {
            var uri = new Uri(isCdn ? Config.RootCdnUri : Config.RootApiUri, urlPath);
            // Serialised once so a 401 retry resends exactly the same body.
            var body = JsonConvert.SerializeObject(data);
            yield return SendJsonRequest<T>(() => new HttpRequestMessage(new HttpMethod(method), uri)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            }, withAuth, OnCompleted, OnError);
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
