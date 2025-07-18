using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

/// <summary>
/// Object containing any information that's persisted by the SqEditor api classes
/// </summary>

namespace Banter.SDKEditor
{
    public class SqEditorPersistentData
    {
        /// <summary>
        /// The currently logged in user's profile data (null if no user logged in)
        /// </summary>
        public SqEditorUser User { get; set; }

        private List<SqEditorUserAchievement> _UserAchievements = new List<SqEditorUserAchievement>();

        /// <summary>
        /// The list of achievements the user has gained for the app
        /// </summary>
        public List<SqEditorUserAchievement> UserAchievements
        {
            get
            {
                if (_UserAchievements == null)
                {
                    _UserAchievements = new List<SqEditorUserAchievement>();
                }
                return _UserAchievements;
            }
            set
            {
                _UserAchievements = value;
            }
        }

        /// <summary>
        /// The currently logged in user's API token information (null if no user logged in)
        /// </summary>
        public SqEditorTokenInfo Token { get; set; }

        /// <summary>
        /// The current short code login that's in progress (null if no short code login in progress)
        /// </summary>
        public SqEditorLoginCode LoginCode { get; set; }
    }

    /// <summary>
    /// Information about a user token
    /// </summary>
    public class SqEditorTokenInfo
    {
        /// <summary>
        /// The refresh token used to generate new access tokens
        /// </summary>
        [JsonProperty("refresh_token")]
        public string RefreshToken { get; set; }

        /// <summary>
        /// The Bearer access token used to authenticate API requests
        /// </summary>
        [JsonProperty("access_token")]
        public string AccessToken { get; set; }

        /// <summary>
        /// The time at which the access token will expire and need to be refreshed using the refresh token
        /// </summary>
        [JsonProperty("access_token_expires_at")]
        public DateTimeOffset? AccessTokenExpiresAt { get; set; }

        /// <summary>
        /// The time at which the refresh token will expire
        /// </summary>
        [JsonProperty("refresh_token_expires_at")]
        public DateTimeOffset? RefreshTokenExpiresAt { get; set; }

        /// <summary>
        /// The client Id that this token was issued for
        /// </summary>
        [JsonProperty("client_id")]
        public string ClientId { get; set; }

        /// <summary>
        /// The user id the token was issued for
        /// </summary>
        [JsonProperty("users_id")]
        public long UserId { get; set; }

        /// <summary>
        /// The app id of the client this token was issued for
        /// </summary>
        [JsonProperty("apps_id")]
        public long AppId { get; set; }

        /// <summary>
        /// The list of scopes granted by the user when approving the app and generating the token
        /// </summary>
        [JsonProperty("scopes")]
        public List<string> GrantedScopes { get; set; } = new List<string>();
    }

    /// <summary>
    /// A list of scopes that can be requested for a user
    /// </summary>
    public static class SqEditorAuthScopes
    {
        /// <summary>
        /// Basic user profile information excluding email address
        /// </summary>
        public static readonly string ReadBasicProfile = "user.basic_profile.read";

        /// <summary>
        /// Allows reading of a user's achievements for the app
        /// </summary>
        public static readonly string ReadAppAchievements = "user.app_achievements.read";

        /// <summary>
        /// Allows setting of a user's achievements for the app
        /// </summary>
        public static readonly string WriteAppAchievements = "user.app_achievements.write";


        /// <summary>
        /// TODO: banter only stuff.  Will need documentation before any 3rd party access
        /// </summary>
        public static readonly string User_Friends_Read = "user.friends.read";
        public static readonly string User_Friends_Write = "user.friends.write";
        public static readonly string User_RichPresence_Write = "user.rich_presence.write";
        public static readonly string User_Communities_Read = "user.communities.read";
        public static readonly string User_Communities_Write = "user.communities.write";
        public static readonly string User_Messages_Receive = "user.messages.read";
        public static readonly string User_Messages_Send = "user.messages.write";
        public static readonly string User_Message_History = "user.messagehistory.read";
        public static readonly string User_Avatar_Write = "user.avatars.write";
    }

    /// <summary>
    /// Holds information relating to a login short code that was generated
    /// </summary>
    public class SqEditorLoginCode
    {
        /// <summary>
        /// The short code itself that is presented to the user to be entered in the browser
        /// </summary>
        [JsonProperty("code")]
        public string Code { get; set; }

        /// <summary>
        /// A reference to this request for a short code login, used to check the status of the short code
        /// </summary>
        [JsonProperty("device_id")]
        public string DeviceId { get; set; }

        /// <summary>
        /// Timestamp of when the short code will expire and no longer be useable
        /// </summary>
        [JsonProperty("expires_at")]
        public DateTimeOffset ExpiresAt { get; set; }

        /// <summary>
        /// The interval (in seconds) that the API should be polled to check the status of a short code
        /// </summary>
        [JsonProperty("interval")]
        public int PollIntervalSeconds { get; set; }

        /// <summary>
        /// The URL that should be displayed to a user to direct them where to enter the short code
        /// </summary>
        [JsonProperty("verification_url")]
        public string VerificationUrl { get; set; }
    }

    /// <summary>
    /// Represents basic upload creation request
    /// </summary>
    public class SqEditorCreateUploadDone
    {
        /// <summary>
        /// THe file unique id.
        /// </summary>
        [JsonProperty("files_id")]
        public long FileId { get; set; }

        /// <summary>
        /// THe file path
        /// </summary>
        [JsonProperty("name")]
        public string Name { get; set; }

    }

    /// <summary>
    /// Represents basic upload creation request
    /// </summary>
    public class SqEditorCreateUploadRequest
    {
        /// <summary>
        /// THe file unique id.
        /// </summary>
        [JsonProperty("space_slug")]
        public string SpaceSlug { get; set; }

        /// <summary>
        /// THe file path
        /// </summary>
        [JsonProperty("name")]
        public string Name { get; set; }

        /// <summary>
        /// A URL to upload the file to
        /// </summary>
        [JsonProperty("size")]
        public long Size { get; set; }

        /// <summary>
        /// A URL to upload the file to
        /// </summary>
        [JsonProperty("type")]
        public string Type { get; set; }

    }


    /// <summary>
    /// Represents basic upload creation request
    /// </summary>
    public class SqEditorCreateUpload
    {
        /// <summary>
        /// THe file unique id.
        /// </summary>
        [JsonProperty("fileId")]
        public long FileId { get; set; }

        /// <summary>
        /// THe file path
        /// </summary>
        [JsonProperty("path")]
        public string Path { get; set; }

        /// <summary>
        /// A URL to upload the file to
        /// </summary>
        [JsonProperty("upload_uri")]
        public string UploadURI { get; set; }

        /// <summary>
        /// A URL to upload the file to
        /// </summary>
        [JsonProperty("communities_id")]
        public long CommunitiesId { get; set; }

    }

    /// <summary>
    /// Represents basic profile information for a user
    /// </summary>
    public class SqEditorUser
    {
        /// <summary>
        /// The user's identifier
        /// </summary>
        [JsonProperty("users_id")]
        public long UserId { get; set; }

        /// <summary>
        /// Display name
        /// </summary>
        [JsonProperty("name")]
        public string Name { get; set; }

        /// <summary>
        /// A URL to the user's profile picture
        /// </summary>
        [JsonProperty("preview_image")]
        public string PreviewImageUrl { get; set; }

        /// <summary>
        /// The user's SideQuest score points
        /// </summary>
        [JsonProperty("score_points")]
        public int ScorePoints { get; set; }

        /// <summary>
        /// The type of profile the user has
        /// </summary>
        [JsonProperty("profile_type")]
        public string ProfileType { get; set; }

        /// <summary>
        /// The tag line the user has entered
        /// </summary>
        [JsonProperty("tag_line")]
        public string TagLine { get; set; }

        /// <summary>
        /// The timestamp of when the user account was created
        /// </summary>
        [JsonProperty("created")]
        public DateTimeOffset Created { get; set; }

        /// <summary>
        /// The user's bio, if they've entered it
        /// </summary>
        [JsonProperty("bio")]
        public string Bio { get; set; }
    }

    /// <summary>
    /// Configuration options for SqEditorAppApi 
    /// </summary>
    public class SqEditorAppApiConfig
    {
        /// <summary>
        /// Creates a new instance of the config
        /// </summary>
        /// <param name="clientId">The client ID retrieved from the sidequest app management for your app</param>
        /// <param name="dataPath">The path to where persistent data can be stored (e.g. Application.persistentDataPath)</param>
        /// <param name="testMode">True if the SideQuest Test API should be used, set to false explicitly to use production</param>
        /// <param name="dataFileName">The filename within dataPath where persistent data will be stored</param>
        public SqEditorAppApiConfig(string clientId, string dataPath, bool testMode = true, string dataFileName = "SqEditorappapi.json")
        {
            if (!Directory.Exists(dataPath))
            {
                throw new DirectoryNotFoundException("Specified data path does not exist");
            }
            if (string.IsNullOrWhiteSpace(dataFileName))
            {
                throw new ArgumentException("dataFileName must be provided.");
            }
            if (string.IsNullOrWhiteSpace(clientId))
            {
                throw new ArgumentException("clientId must be specified");
            }
            DataPath = dataPath;
            DataFileName = dataFileName;
            if (testMode)
            {
                RootApiUri = new Uri("https://api.sidetestvr.com");
            }
            else
            {
                RootApiUri = new Uri("https://api.sidequestvr.com");
            }
            if (testMode)
            {
                RootCdnUri = new Uri("https://cdn.sidetestvr.com");
            }
            else
            {
                RootCdnUri = new Uri("https://cdn.sidequestvr.com");
            }
            ClientId = clientId;
        }

        /// <summary>
        /// Gets the combined data path and data filename
        /// </summary>
        public string DataFile
        {
            get
            {
                return Path.Combine(DataPath, DataFileName);
            }
        }

        /// <summary>
        /// The root URI of the sidequest CDN that will be used
        /// </summary>
        public Uri RootCdnUri { get; private set; }

        /// <summary>
        /// The root URI of the sidequest API that will be used
        /// </summary>
        public Uri RootApiUri { get; private set; }

        /// <summary>
        /// The path to where persistent data can be stored (e.g. Application.persistentDataPath)
        /// </summary>
        public string DataPath { get; private set; }

        /// <summary>
        /// The filename within dataPath where persistent data will be stored
        /// </summary>
        public string DataFileName { get; private set; }

        /// <summary>
        /// The client ID retrieved from the sidequest app management for your app
        /// </summary>
        public string ClientId { get; private set; }
    }

    public class SqEditorUserAchievement : SqEditorAchievement
    {
        /// <summary>
        /// The ID of the user that achieved
        /// </summary>
        [JsonProperty("users_id")]
        public long UserId { get; set; }

        /// <summary>
        /// The timestamp when the user unlocked the achievement
        /// </summary>
        [JsonProperty("unlocked_at")]
        public DateTimeOffset UserUnlockedAt { get; set; }
    }

    public class SqEditorAchievement
    {
        /// <summary>
        /// The ID of the app the achievement is for
        /// </summary>
        [JsonProperty("apps_id")]
        public long AppId { get; set; }

        /// <summary>
        /// The string identifier for the achievement (defined in SqEditor app management dashboard when achievement was created)
        /// </summary>
        [JsonProperty("achievement_identifier")]
        public string AchievementId { get; set; }

        /// <summary>
        /// The display name of the achievement
        /// </summary>
        [JsonProperty("name")]
        public string Name { get; set; }

        /// <summary>
        /// The URL of the larger image associated with the achievement
        /// </summary>
        [JsonProperty("image")]
        public string ImageUrl { get; set; }

        /// <summary>
        /// The URL of the icon image associated with the achievement
        /// </summary>
        [JsonProperty("icon")]
        public string IconUrl { get; set; }

        /// <summary>
        /// The timestamp when the achievement definition was created (NOT when the user got it)
        /// </summary>
        [JsonProperty("created_at")]
        public DateTimeOffset AchievementCreatedAt { get; set; }

    }
    
/// <summary>
/// Represents basic upload creation request
/// </summary>
public class SqEditorUploadAvatars
{
    /// <summary>
    /// THe file unique id.
    /// </summary>
    [JsonProperty("high_avatar_files_id")]
    public long HighId { get; set; }

    /// <summary>
    /// THe file unique id.
    /// </summary>
    [JsonProperty("version")]
    public long Version { get; set; }

    /// <summary>
    /// THe file path
    /// </summary>
    [JsonProperty("low_avatar_files_id")]
    public long LowId { get; set; }

    /// <summary>
    /// THe file path
    /// </summary>
    [JsonProperty("is_public")]
    public bool Public { get; set; }

}
}
