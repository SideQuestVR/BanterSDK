using System;
using System.Linq;
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
        /// Validates if the URL is a valid format (either direct APK URL or SideQuest listing URL).
        /// Does not perform any network requests or actual resolution.
        /// </summary>
        /// <param name="url">URL to validate</param>
        /// <returns>True if the URL format is valid, false otherwise</returns>
        public static bool ValidateUrlFormat(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            // Normalize URL (trim whitespace)
            url = url.Trim();

            // Check if it's either a direct APK URL or a listing URL
            return IsDirectApkUrl(url) || IsListingUrl(url);
        }

        /// <summary>
        /// Checks if the URL is a direct APK download URL
        /// </summary>
        public static bool IsDirectApkUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            url = url.ToLowerInvariant();
            // Accept any .apk URL, not just CDN URLs
            return url.EndsWith(".apk");
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

                // Debug: Log the JSON structure
                LogLine.Do($"API Response Keys: {string.Join(", ", json.Properties().Select(p => p.Name))}");

                // Get app name for constructing filename (used as fallback)
                string appName = json["name"]?.ToString() ?? "app";
                appName = SanitizeFilename(appName);

                // Try multiple paths to find APK URL (prioritize full URLs over constructed ones)
                string filesId = null;

                // Method 1: Look for urls array FIRST (contains full CDN URLs with correct filenames)
                LogLine.Do("Method 1: Checking urls array (highest priority - contains full CDN URLs)...");
                var urls = json["urls"] as JArray;
                if (urls != null && urls.Count > 0)
                {
                    LogLine.Do($"Found urls array with {urls.Count} entries");
                    string urlResult = ExtractFilesIdFromUrls(urls);

                    // Check if we got a full URL or just a files_id
                    if (!string.IsNullOrEmpty(urlResult))
                    {
                        if (urlResult.StartsWith("http"))
                        {
                            // Full URL returned, use it directly - THIS IS THE PREFERRED METHOD
                            LogLine.Do($"✓ SUCCESS: Using full URL from urls array: {urlResult}");
                            return urlResult;
                        }
                        else
                        {
                            // Just files_id returned
                            filesId = urlResult;
                            LogLine.Do($"Extracted files_id from urls array: {filesId}");
                        }
                    }
                    else
                    {
                        LogLine.Do("Method 1: No valid APK URL found in urls array");
                    }
                }
                else
                {
                    LogLine.Do("Method 1: No urls array found");
                }

                // Method 2: Look for direct download_url field
                if (string.IsNullOrEmpty(filesId))
                {
                    LogLine.Do("Method 2: Checking direct download_url field...");
                    string downloadUrl = json["download_url"]?.ToString();
                    if (!string.IsNullOrEmpty(downloadUrl) && downloadUrl.EndsWith(".apk"))
                    {
                        LogLine.Do($"✓ SUCCESS: Found direct download_url: {downloadUrl}");
                        return downloadUrl;
                    }
                    else
                    {
                        LogLine.Do("Method 2: No valid download_url field found");
                    }
                }

                // Method 3: Check top-level URL fields
                if (string.IsNullOrEmpty(filesId))
                {
                    LogLine.Do("Method 3: Checking top-level URL fields (apk_url, file_url, url, cdn_url)...");
                    string[] topLevelFields = { "apk_url", "file_url", "url", "cdn_url" };

                    foreach (var field in topLevelFields)
                    {
                        string url = json[field]?.ToString();
                        if (!string.IsNullOrEmpty(url) && url.EndsWith(".apk"))
                        {
                            LogLine.Do($"✓ SUCCESS: Found APK URL in top-level field '{field}': {url}");
                            return url;
                        }
                    }
                    LogLine.Do("Method 3: No valid APK URL found in top-level fields");
                }

                // Method 4: Check nested data object
                if (string.IsNullOrEmpty(filesId))
                {
                    LogLine.Do("Method 4: Checking nested data object...");
                    var dataObj = json["data"] as JObject;
                    if (dataObj != null)
                    {
                        LogLine.Do("Found nested data object");

                        // Check data.download_url
                        string dataDownloadUrl = dataObj["download_url"]?.ToString();
                        if (!string.IsNullOrEmpty(dataDownloadUrl) && dataDownloadUrl.EndsWith(".apk"))
                        {
                            LogLine.Do($"✓ SUCCESS: Found APK URL in data.download_url: {dataDownloadUrl}");
                            return dataDownloadUrl;
                        }

                        // Check data.apk_url
                        string dataApkUrl = dataObj["apk_url"]?.ToString();
                        if (!string.IsNullOrEmpty(dataApkUrl) && dataApkUrl.EndsWith(".apk"))
                        {
                            LogLine.Do($"✓ SUCCESS: Found APK URL in data.apk_url: {dataApkUrl}");
                            return dataApkUrl;
                        }

                        // Check data.urls array
                        var dataUrls = dataObj["urls"] as JArray;
                        if (dataUrls != null && dataUrls.Count > 0)
                        {
                            LogLine.Do($"Found data.urls array with {dataUrls.Count} entries");
                            string urlResult = ExtractFilesIdFromUrls(dataUrls);

                            if (!string.IsNullOrEmpty(urlResult))
                            {
                                if (urlResult.StartsWith("http"))
                                {
                                    LogLine.Do($"✓ SUCCESS: Using full URL from data.urls array: {urlResult}");
                                    return urlResult;
                                }
                                else
                                {
                                    filesId = urlResult;
                                    LogLine.Do($"Extracted files_id from data.urls: {filesId}");
                                }
                            }
                        }

                        LogLine.Do("Method 4: No valid APK URL found in nested data object");
                    }
                    else
                    {
                        LogLine.Do("Method 4: No nested data object found");
                    }
                }

                // Method 5: Look for app_release_files array (LAST RESORT - constructs URL with potentially incorrect filename)
                if (string.IsNullOrEmpty(filesId))
                {
                    LogLine.Do("Method 5: Checking app_release_files array as fallback...");
                    var releaseFiles = json["app_release_files"] as JArray;
                    if (releaseFiles != null && releaseFiles.Count > 0)
                    {
                        LogLine.Do($"Found app_release_files array with {releaseFiles.Count} entries");
                        filesId = ExtractFilesIdFromReleaseFiles(releaseFiles);
                        if (!string.IsNullOrEmpty(filesId))
                        {
                            LogLine.Do($"⚠ WARNING: Using app_release_files fallback. Extracted files_id: {filesId}. Will construct URL with sanitized app name - filename may be incorrect!");
                        }
                    }
                    else
                    {
                        LogLine.Do("Method 5: No app_release_files array found");
                    }
                }

                if (string.IsNullOrEmpty(filesId))
                {
                    // Log the full JSON for debugging
                    Debug.LogWarning($"Could not find files_id or APK URL in API response. JSON: {jsonResponse.Substring(0, Math.Min(500, jsonResponse.Length))}...");
                    throw new Exception($"No APK download URL found for app ID {appId}. " +
                        $"Tried the following methods in order:\n" +
                        $"1. urls array (full CDN URLs - PREFERRED)\n" +
                        $"2. download_url field\n" +
                        $"3. Top-level URL fields (apk_url, file_url, url, cdn_url)\n" +
                        $"4. Nested data object (data.download_url, data.apk_url, data.urls)\n" +
                        $"5. app_release_files array (fallback - constructs URL)\n" +
                        $"This may not be a Quest Home app, or the app structure is not supported.");
                }

                // Construct CDN URL from files_id (this is a fallback - filename may be incorrect!)
                string apkUrl = $"{SIDEQUEST_CDN_BASE}/{filesId}/{appName}.apk";
                LogLine.Do($"⚠ FALLBACK: Constructed APK URL from files_id {filesId} with sanitized app name '{appName}': {apkUrl}");
                LogLine.Do($"⚠ WARNING: If download fails with HTML, the filename may be incorrect. Check the API response for the actual filename in the urls array.");
                return apkUrl;
            }
            catch (Exception ex) when (ex is Newtonsoft.Json.JsonException)
            {
                throw new Exception($"Failed to parse SideQuest API response: {ex.Message}");
            }
        }

        /// <summary>
        /// Extract files_id from app_release_files array
        /// </summary>
        private static string ExtractFilesIdFromReleaseFiles(JArray releaseFiles)
        {
            foreach (var file in releaseFiles)
            {
                string fileType = file["type"]?.ToString();

                if (fileType == "apk")
                {
                    string filesId = file["files_id"]?.ToString();

                    if (!string.IsNullOrEmpty(filesId))
                    {
                        LogLine.Do($"Found APK in release_files with files_id: {filesId}");
                        return filesId;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Extract files_id from urls array (alternative structure)
        /// </summary>
        private static string ExtractFilesIdFromUrls(JArray urls)
        {
            // Property names to check in URL objects (matching Tampermonkey script)
            string[] candidateFields = { "link_url", "url", "download_url", "cdn_url" };

            foreach (var urlEntry in urls)
            {
                LogLine.Do($"  → Checking URL entry type: {urlEntry.Type}");

                // Check if the entry is a simple string
                if (urlEntry.Type == JTokenType.String)
                {
                    string url = urlEntry.ToString();
                    LogLine.Do($"    String entry value: {url}");

                    // Look for any URL ending with .apk (not just CDN URLs)
                    if (url.EndsWith(".apk"))
                    {
                        LogLine.Do($"    ✓ Found APK URL in urls array (string): {url}");
                        return url; // Return full URL directly
                    }
                    else
                    {
                        LogLine.Do($"    ✗ String does not end with .apk");
                    }

                    // Try to extract files_id from CDN URL
                    var match = Regex.Match(url, @"cdn\.sidequestvr\.com/file/(\d+)/");
                    if (match.Success)
                    {
                        string filesId = match.Groups[1].Value;
                        LogLine.Do($"Extracted files_id from URL string in urls array: {filesId}");
                        return filesId;
                    }
                }

                // If it's an object, check common URL property names
                if (urlEntry.Type == JTokenType.Object)
                {
                    var urlObject = (JObject)urlEntry;
                    LogLine.Do($"    Object entry with properties: {string.Join(", ", urlObject.Properties().Select(p => p.Name))}");

                    foreach (var field in candidateFields)
                    {
                        string url = urlEntry[field]?.ToString();
                        if (!string.IsNullOrEmpty(url))
                        {
                            LogLine.Do($"      → Checking field '{field}': {url}");

                            // Look for any URL ending with .apk (not just CDN URLs)
                            if (url.EndsWith(".apk"))
                            {
                                LogLine.Do($"      ✓ Found APK URL in urls array ({field}): {url}");
                                return url; // Return full URL directly
                            }
                            else
                            {
                                LogLine.Do($"      ✗ Field '{field}' does not end with .apk");
                            }

                            // Try to extract files_id from CDN URL
                            var match = Regex.Match(url, @"cdn\.sidequestvr\.com/file/(\d+)/");
                            if (match.Success)
                            {
                                string filesId = match.Groups[1].Value;
                                LogLine.Do($"Extracted files_id from {field} in urls array: {filesId}");
                                return filesId;
                            }
                        }
                    }
                }
            }

            LogLine.Do("  ✗ No APK URL or files_id found in urls array");
            return null;
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
