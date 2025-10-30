using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json.Linq;

namespace Banter.SDK
{
    /// <summary>
    /// Utility class for resolving SideQuest listing URLs to direct APK URLs.
    /// Works cross-platform on PC, Quest (Android), and other Unity platforms.
    /// </summary>
    public static class SideQuestUrlResolver
    {
        private const string SIDEQUEST_API_BASE = "https://api.sidequestvr.com/v2";
        private const string SIDEQUEST_CDN_BASE = "https://cdn.sidequestvr.com/file";

        /// <summary>
        /// Resolves a URL to a direct APK download URL.
        /// Handles both direct APK URLs (returns as-is) and SideQuest listing URLs (extracts APK URL via API).
        /// </summary>
        /// <param name="inputUrl">SideQuest listing URL or direct APK URL</param>
        /// <returns>Direct APK download URL</returns>
        public static async Task<string> ResolveApkUrl(string inputUrl)
        {
            if (string.IsNullOrWhiteSpace(inputUrl))
            {
                throw new ArgumentException("URL cannot be empty");
            }

            // Normalize URL (trim whitespace)
            inputUrl = inputUrl.Trim();

            // Check if already a direct APK URL
            if (IsDirectApkUrl(inputUrl))
            {
                LogLine.Do("Direct APK URL detected, using as-is");
                return inputUrl;
            }

            // Check if SideQuest listing URL
            if (IsListingUrl(inputUrl))
            {
                LogLine.Do("SideQuest listing URL detected, extracting APK URL via API...");
                string appId = ExtractAppIdFromUrl(inputUrl);
                LogLine.Do($"Extracted app ID: {appId}");

                string apkUrl = await FetchApkUrlFromApi(appId);
                LogLine.Do($"Resolved to APK URL: {apkUrl}");
                return apkUrl;
            }

            throw new Exception($"Invalid URL format. Must be either:\n" +
                              $"- SideQuest listing URL (e.g., https://sidequestvr.com/app/1234/...)\n" +
                              $"- Direct APK URL (e.g., https://cdn.sidequestvr.com/file/1234/app.apk)\n" +
                              $"Got: {inputUrl}");
        }

        /// <summary>
        /// Checks if the URL is a direct APK download URL
        /// </summary>
        public static bool IsDirectApkUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            url = url.ToLowerInvariant();
            return url.EndsWith(".apk") && url.Contains("cdn.sidequestvr.com");
        }

        /// <summary>
        /// Checks if the URL is a SideQuest listing page URL
        /// </summary>
        public static bool IsListingUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            // Match: https://sidequestvr.com/app/1234/...
            // or: http://sidequestvr.com/app/1234/...
            // or: sidequestvr.com/app/1234/...
            return Regex.IsMatch(url, @"sidequestvr\.com/app/\d+", RegexOptions.IgnoreCase);
        }

        /// <summary>
        /// Extracts the numeric app ID from a SideQuest listing URL
        /// </summary>
        /// <param name="url">SideQuest listing URL</param>
        /// <returns>App ID as string</returns>
        public static string ExtractAppIdFromUrl(string url)
        {
            // Match: https://sidequestvr.com/app/1234/app-name
            // Extract: 1234
            var match = Regex.Match(url, @"sidequestvr\.com/app/(\d+)", RegexOptions.IgnoreCase);

            if (!match.Success)
            {
                throw new Exception($"Could not extract app ID from URL: {url}");
            }

            return match.Groups[1].Value;
        }

        /// <summary>
        /// Fetches APK URL from SideQuest API using app ID
        /// </summary>
        /// <param name="appId">SideQuest app ID</param>
        /// <returns>Direct APK download URL</returns>
        public static async Task<string> FetchApkUrlFromApi(string appId)
        {
            string apiUrl = $"{SIDEQUEST_API_BASE}/apps/{appId}";
            LogLine.Do($"Fetching app data from: {apiUrl}");

            using (UnityWebRequest request = UnityWebRequest.Get(apiUrl))
            {
                // Set a reasonable timeout (10 seconds)
                request.timeout = 10;

                // Send request
                var operation = request.SendWebRequest();

                // Wait for completion (async)
                while (!operation.isDone)
                {
                    await Task.Yield();
                }

                // Check for errors
                if (request.result != UnityWebRequest.Result.Success)
                {
                    string error = request.error;
                    if (request.responseCode == 404)
                    {
                        throw new Exception($"App not found on SideQuest (ID: {appId}). Check the URL and try again.");
                    }
                    else if (request.responseCode >= 500)
                    {
                        throw new Exception($"SideQuest API server error ({request.responseCode}). Try again later.");
                    }
                    else
                    {
                        throw new Exception($"Failed to fetch app data from SideQuest API: {error} (Code: {request.responseCode})");
                    }
                }

                // Parse JSON response
                string jsonResponse = request.downloadHandler.text;
                LogLine.Do($"API response received ({jsonResponse.Length} bytes)");

                return ParseApkUrlFromApiResponse(jsonResponse, appId);
            }
        }

        /// <summary>
        /// Parses SideQuest API JSON response to extract APK download URL
        /// </summary>
        /// <param name="jsonResponse">JSON response from SideQuest API</param>
        /// <param name="appId">App ID (for error messages)</param>
        /// <returns>Direct APK download URL</returns>
        public static string ParseApkUrlFromApiResponse(string jsonResponse, string appId)
        {
            try
            {
                JObject json = JObject.Parse(jsonResponse);

                // Get app name for constructing filename
                string appName = json["name"]?.ToString() ?? "app";
                appName = SanitizeFilename(appName);

                // Look for app_release_files array
                var releaseFiles = json["app_release_files"] as JArray;

                if (releaseFiles == null || releaseFiles.Count == 0)
                {
                    throw new Exception($"No release files found for app ID {appId}. The app may not have any published versions.");
                }

                // Find APK file in release files
                foreach (var file in releaseFiles)
                {
                    string fileType = file["type"]?.ToString();

                    if (fileType == "apk")
                    {
                        string filesId = file["files_id"]?.ToString();

                        if (string.IsNullOrEmpty(filesId))
                        {
                            LogLine.Do("Found APK entry but files_id is empty, skipping...");
                            continue;
                        }

                        // Construct CDN URL
                        string apkUrl = $"{SIDEQUEST_CDN_BASE}/{filesId}/{appName}.apk";
                        LogLine.Do($"Constructed APK URL from files_id: {filesId}");
                        return apkUrl;
                    }
                }

                // No APK file found
                throw new Exception($"No APK file found for app ID {appId}. The app may not be a Quest Home or may not have an APK release.");
            }
            catch (Exception ex) when (ex is Newtonsoft.Json.JsonException)
            {
                throw new Exception($"Failed to parse SideQuest API response: {ex.Message}");
            }
        }

        /// <summary>
        /// Sanitizes a filename for use in URLs
        /// </summary>
        private static string SanitizeFilename(string filename)
        {
            // Replace spaces with underscores
            filename = filename.Replace(" ", "_");

            // Convert to lowercase
            filename = filename.ToLowerInvariant();

            // Remove special characters (keep only alphanumeric, underscore, dash)
            filename = Regex.Replace(filename, @"[^a-z0-9_\-]", "");

            return filename;
        }
    }
}
