using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Siccity.GLTFUtility;
using Newtonsoft.Json.Linq;

namespace Banter.SDK
{
    /*
    #### Banter Quest Home
    Load Quest Home environments from SideQuest APK files. Downloads the APK, extracts GLTF and textures,
    and loads them with automatic material and collider setup.

    **Properties**
    - `url` - The URL of the Quest Home APK file.
    - `addColliders` - If colliders should be added to opaque meshes.
    - `climbable` - Makes surfaces climbable by setting colliders to Grabbable layer (Layer 20).
    - `loadingStatus` - Current loading status message (read-only).
    - `loadingProgress` - Current loading progress 0-1 (read-only).

    **Code Example**
    ```js
        const url = "https://cdn.sidequestvr.com/file/167567/canyon_environment.apk";
        const gameObject = new BS.GameObject("MyQuestHome");
        const questHome = await gameObject.AddComponent(new BS.BanterQuestHome(url, true, false));
    ```
    */
    [DefaultExecutionOrder(-1)]
    [WatchComponent]
    [RequireComponent(typeof(BanterObjectId))]
    public class BanterQuestHome : BanterComponentBase
    {
        [Tooltip("The URL of the Quest Home APK file to be loaded.")]
        [See(initial = "")][SerializeField] internal string url;

        [Tooltip("Enable to automatically add colliders to opaque meshes.")]
        [See(initial = "true")][SerializeField] internal bool addColliders = true;

        [Tooltip("Enable to make surfaces climbable by setting colliders to Grabbable layer (Layer 20).")]
        [See(initial = "false")][SerializeField] internal bool climbable = false;

        [Tooltip("Current loading status message.")]
        public string loadingStatus = "";

        [Tooltip("Current loading progress (0-1).")]
        [Range(0f, 1f)]
        public float loadingProgress = 0f;

        private bool loadStarted;
        private GameObject loadedModel;

        internal override void StartStuff()
        {
            LoadQuestHome();
        }

        /// <summary>
        /// Main loading pipeline for Quest Home APK
        /// </summary>
        async void LoadQuestHome()
        {
            if (loadStarted)
            {
                Debug.LogWarning("Quest Home load already in progress");
                return;
            }

            // Don't load if URL is empty
            if (string.IsNullOrEmpty(url))
            {
                SetLoadedIfNot(false, "URL cannot be empty");
                return;
            }

            _loaded = false;
            loadStarted = true;

            try
            {
                UpdateLoadingStatus($"Starting Quest Home load from: {url}", 0f);

                // Step 0: Resolve URL (handles both direct APK URLs and SideQuest listing URLs)
                UpdateLoadingStatus("Resolving URL...", 0.02f);
                string resolvedApkUrl = await SideQuestUrlResolver.ResolveApkUrl(url);
                if (resolvedApkUrl != url)
                {
                    LogLine.Do($"Resolved listing URL to APK URL: {resolvedApkUrl}");
                }
                UpdateLoadingStatus("URL resolved", 0.05f);

                // Step 1: Download APK
                UpdateLoadingStatus("Downloading APK...", 0.08f);
                LogLine.Do($"🔗 DOWNLOAD URL: {resolvedApkUrl}");
                byte[] apkData = await DownloadAPK(resolvedApkUrl);
                UpdateLoadingStatus($"APK downloaded ({apkData.Length / 1024 / 1024} MB)", 0.30f);

                // Step 2: Extract files
                UpdateLoadingStatus("Extracting APK contents...", 0.32f);
                QuestHomeAssets assets = APKExtractor.ExtractAll(apkData);

                if (!APKExtractor.ValidateAssets(assets))
                {
                    throw new Exception("Extracted assets validation failed");
                }
                UpdateLoadingStatus("APK extraction complete", 0.40f);

                // Step 3: Load textures
                UpdateLoadingStatus($"Loading {assets.textures.Count} textures...", 0.42f);
                Dictionary<string, Texture2D> loadedTextures = await LoadTextures(assets.textures);
                UpdateLoadingStatus($"Loaded {loadedTextures.Count} textures", 0.55f);

                // Step 4: Parse GLTF for texture mappings
                UpdateLoadingStatus("Parsing GLTF material mappings...", 0.57f);
                List<TextureMapping> textureMappings = GLTFMaterialMapper.ParseGLTF(assets.gltfJson);
                UpdateLoadingStatus("Material mappings parsed", 0.60f);

                // Step 5: Load GLTF model with animations
                UpdateLoadingStatus("Loading GLTF model with animations...", 0.62f);

                // Configure import settings for animation looping
                ImportSettings importSettings = new ImportSettings();
                importSettings.animationSettings.looping = true;  // Enable looping
                importSettings.animationSettings.useLegacyClips = true;  // Use legacy Animation component

                var result = await LoadGLTFModel(assets.gltfData, assets.binData, importSettings);
                GameObject model = result.model;
                AnimationClip[] animations = result.animations;

                if (model == null)
                {
                    throw new Exception("Failed to load GLTF model");
                }

                loadedModel = model;
                UpdateLoadingStatus("GLTF model loaded successfully", 0.75f);

                // Step 5b: Setup animations if present
                if (animations != null && animations.Length > 0)
                {
                    UpdateLoadingStatus($"Setting up {animations.Length} animations...", 0.76f);
                    SetupAnimations(model, animations);
                    UpdateLoadingStatus("Animations configured", 0.78f);
                }

                // Step 6: Apply textures to materials
                // This also applies correct unlit shaders based on alpha mode (OPAQUE/MASK/BLEND)
                UpdateLoadingStatus("Applying textures to materials with alpha-aware shaders...", 0.79f);
                GLTFMaterialMapper.ApplyTextures(model, loadedTextures, textureMappings);
                UpdateLoadingStatus("Textures applied to materials", 0.83f);

                // Step 7: Convert remaining materials to unlit shaders (Quest optimization, always enabled)
                // (ApplyTextures already handles materials with textures)
                UpdateLoadingStatus("Converting any remaining materials to unlit shaders...", 0.84f);
                GLTFMaterialMapper.ConvertAllToUnlit(model);
                UpdateLoadingStatus("Material shaders converted", 0.88f);

                // Step 8: Setup colliders
                if (addColliders)
                {
                    UpdateLoadingStatus("Setting up colliders...", 0.89f);
                    SetupColliders(model);
                    UpdateLoadingStatus("Colliders configured", 0.93f);
                }

                // Step 9: Load background audio (optional)
                if (assets.audioData != null && assets.audioData.Length > 0)
                {
                    UpdateLoadingStatus("Loading background audio...", 0.94f);
                    await SetupAudio(assets.audioData);
                    UpdateLoadingStatus("Background audio loaded", 0.97f);
                }

                // Step 10: Set skybox to black for Quest Home environments
                UpdateLoadingStatus("Finalizing environment...", 0.98f);
                SetSkyboxToBlack();

                UpdateLoadingStatus("Quest Home loaded successfully!", 1.0f);

                // SetLoadedIfNot triggers callbacks that may require BanterLink
                // Wrap in try-catch for standalone usage (e.g., via QuestHomeLoader)
                try
                {
                    SetLoadedIfNot();
                }
                catch (Exception callbackEx)
                {
                    Debug.LogWarning($"SetLoadedIfNot callback failed (this is normal for standalone usage): {callbackEx.Message}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to load Quest Home: {ex.Message}\n{ex.StackTrace}");
                UpdateLoadingStatus($"Failed to load: {ex.Message}", 0f);
                loadStarted = false;
                SetLoadedIfNot(false, ex.Message);
            }
        }

        /// <summary>
        /// Update loading status message and progress for Visual Scripting access
        /// </summary>
        private void UpdateLoadingStatus(string message, float progress)
        {
            loadingStatus = message;
            loadingProgress = progress;
            percentage = progress; // Update base class percentage
            LogLine.Do(message);
        }

        /// <summary>
        /// Download APK from URL with progress tracking and retry logic with alternate filename patterns
        /// </summary>
        private async Task<byte[]> DownloadAPK(string apkUrl, int maxRetries = 6)
        {
            Exception lastException = null;
            string originalUrl = apkUrl;
            bool htmlDetected = false;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    if (attempt > 1)
                    {
                        LogLine.Do($"Download attempt {attempt}/{maxRetries}...");
                    }

                    using (UnityWebRequest request = UnityWebRequest.Get(apkUrl))
                    {
                        // Set timeout for large APKs (5 minutes)
                        request.timeout = 300;

                        var operation = request.SendWebRequest();

                        while (!operation.isDone)
                        {
                            float progress = request.downloadProgress;
                            // Log progress every 10%
                            if (progress > 0 && (int)(progress * 10) % 2 == 0)
                            {
                                LogLine.Do($"Download progress: {progress * 100:F0}%");
                            }
                            await Task.Yield();
                        }

                        if (request.result != UnityWebRequest.Result.Success)
                        {
                            throw new Exception($"APK download failed: {request.error}");
                        }

                        // Get data reference before disposal
                        byte[] data = request.downloadHandler.data;
                        if (data == null || data.Length == 0)
                        {
                            throw new Exception("Downloaded data is null or empty");
                        }

                        // CRITICAL FIX: Copy data before disposal to prevent corruption
                        byte[] dataCopy = new byte[data.Length];
                        Buffer.BlockCopy(data, 0, dataCopy, 0, data.Length);

                        LogLine.Do($"Download complete: {dataCopy.Length} bytes ({dataCopy.Length / 1024.0 / 1024.0:F2} MB)");

                        // Validate ZIP signature (PK header: 0x50 0x4B)
                        if (dataCopy.Length < 4 || dataCopy[0] != 0x50 || dataCopy[1] != 0x4B)
                        {
                            // Check if it's HTML (starts with "<!DO" or similar)
                            bool isHtml = dataCopy.Length >= 2 && dataCopy[0] == 0x3C && (dataCopy[1] == 0x21 || dataCopy[1] == 0x68);

                            if (isHtml && attempt < maxRetries)
                            {
                                htmlDetected = true;
                                LogLine.Do($"⚠ HTML page detected instead of APK file. Wrong filename in URL!");

                                // Try alternate filename pattern
                                string alternateUrl = GenerateAlternateApkUrl(originalUrl, attempt);
                                if (alternateUrl != null)
                                {
                                    LogLine.Do($"🔄 Trying alternate filename pattern {attempt}: {alternateUrl}");
                                    apkUrl = alternateUrl;
                                    lastException = new Exception($"HTML detected, trying alternate filename pattern");
                                    continue; // Retry with new URL
                                }
                            }

                            throw new Exception($"Downloaded data is not a valid ZIP/APK file. First 4 bytes: {BitConverter.ToString(dataCopy, 0, Math.Min(4, dataCopy.Length))}");
                        }

                        // Validate size matches Content-Length if available
                        string contentLength = request.GetResponseHeader("Content-Length");
                        if (!string.IsNullOrEmpty(contentLength) && long.TryParse(contentLength, out long expectedSize))
                        {
                            if (dataCopy.Length != expectedSize)
                            {
                                Debug.LogWarning($"Downloaded size mismatch: expected {expectedSize} bytes, got {dataCopy.Length} bytes");
                            }
                        }

                        return dataCopy;
                    }
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    LogLine.Do($"Download attempt {attempt} failed: {ex.Message}");

                    if (attempt < maxRetries && !htmlDetected)
                    {
                        // Wait before retry (exponential backoff: 1s, 2s, 4s) - but not for HTML retries
                        int delayMs = 1000 * (int)Math.Pow(2, Math.Min(attempt - 1, 3));
                        LogLine.Do($"Retrying in {delayMs}ms...");
                        await Task.Delay(delayMs);
                    }

                    htmlDetected = false; // Reset for next attempt
                }
            }

            string errorMessage = htmlDetected
                ? $"Download failed after {maxRetries} attempts with multiple filename patterns. Last error: {lastException?.Message}"
                : $"Download failed after {maxRetries} attempts. Last error: {lastException?.Message}";

            throw new Exception(errorMessage, lastException);
        }

        /// <summary>
        /// Generate alternate APK URL with different filename patterns
        /// </summary>
        private string GenerateAlternateApkUrl(string originalUrl, int attemptNumber)
        {
            // Extract files_id and current filename from URL
            var match = System.Text.RegularExpressions.Regex.Match(originalUrl, @"cdn\.sidequestvr\.com/file/(\d+)/(.+\.apk)");
            if (!match.Success)
                return null;

            string filesId = match.Groups[1].Value;
            string currentFilename = match.Groups[2].Value;
            string baseUrl = $"https://cdn.sidequestvr.com/file/{filesId}";

            // Extract base name without .apk extension and clean it
            string baseName = currentFilename.Replace(".apk", "").ToLowerInvariant();
            // Remove common prefixes
            baseName = baseName.Replace("custom_home_", "").Replace("customhome_", "").Replace("custom_home", "").Replace("customhome", "");
            baseName = baseName.Trim('_', '-');

            // Try different filename patterns based on attempt number
            switch (attemptNumber)
            {
                case 1:
                    // Try: "custom_home_baseName.apk"
                    return $"{baseUrl}/custom_home_{baseName}.apk";

                case 2:
                    // Try: "customhome_baseName.apk"
                    return $"{baseUrl}/customhome_{baseName}.apk";

                case 3:
                    // Try: "baseName_environment.apk"
                    return $"{baseUrl}/{baseName}_environment.apk";

                case 4:
                    // Try: "CustomHome_TitleCase.apk"
                    string titleCase = ToTitleCase(baseName);
                    return $"{baseUrl}/CustomHome_{titleCase}.apk";

                case 5:
                    // Try: just "baseName.apk" (simplest form)
                    return $"{baseUrl}/{baseName}.apk";

                default:
                    return null;
            }
        }

        /// <summary>
        /// Convert string to Title Case (e.g., "hello_world" -> "Hello_World")
        /// </summary>
        private string ToTitleCase(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            var words = input.Split('_', '-');
            for (int i = 0; i < words.Length; i++)
            {
                if (words[i].Length > 0)
                {
                    words[i] = char.ToUpper(words[i][0]) + words[i].Substring(1).ToLowerInvariant();
                }
            }
            return string.Join("_", words);
        }

        /// <summary>
        /// Load textures from KTX files
        /// </summary>
        private async Task<Dictionary<string, Texture2D>> LoadTextures(Dictionary<string, byte[]> textureFiles)
        {
            var loadedTextures = new Dictionary<string, Texture2D>();

            foreach (var kvp in textureFiles)
            {
                string textureName = kvp.Key;
                byte[] textureData = kvp.Value;

                try
                {
                    Texture2D texture = null;

                    if (textureName.EndsWith(".ktx", StringComparison.OrdinalIgnoreCase))
                    {
                        // Load ASTC texture from KTX file
                        texture = KTXParser.LoadASTCTexture(textureData, textureName);
                    }
                    else if (textureName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                             textureName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                             textureName.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
                    {
                        // Load regular image texture
                        texture = new Texture2D(2, 2);
                        texture.LoadImage(textureData);
                        texture.name = textureName;
                    }

                    if (texture != null)
                    {
                        loadedTextures[textureName] = texture;
                        LogLine.Do($"Loaded texture: {textureName} ({texture.width}x{texture.height})");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Failed to load texture '{textureName}': {ex.Message}");
                }

                await Task.Yield();
            }

            return loadedTextures;
        }

        /// <summary>
        /// Strip texture references from GLTF JSON to prevent loading errors
        /// </summary>
        private byte[] StripTextureReferences(byte[] gltfData)
        {
            try
            {
                // Parse GLTF JSON
                string gltfJson = System.Text.Encoding.UTF8.GetString(gltfData);
                var gltf = JObject.Parse(gltfJson);

                // Remove texture references from materials
                if (gltf["materials"] != null && gltf["materials"] is JArray materials)
                {
                    foreach (JObject material in materials)
                    {
                        // Remove texture references from PBR metallic roughness
                        if (material["pbrMetallicRoughness"] is JObject pbr)
                        {
                            pbr.Remove("baseColorTexture");
                            pbr.Remove("metallicRoughnessTexture");
                        }

                        // Remove other texture references
                        material.Remove("normalTexture");
                        material.Remove("emissiveTexture");
                        material.Remove("occlusionTexture");
                    }
                }

                // Clear images array (textures reference these)
                if (gltf["images"] != null)
                {
                    gltf["images"] = new JArray();
                }

                // Clear textures array
                if (gltf["textures"] != null)
                {
                    gltf["textures"] = new JArray();
                }

                // Fix buffer URI to point to our temp .bin file
                if (gltf["buffers"] != null && gltf["buffers"] is JArray buffers && buffers.Count > 0)
                {
                    if (buffers[0] is JObject buffer && buffer["uri"] != null)
                    {
                        // Update the URI to point to our temp bin file
                        buffer["uri"] = "temp_quest_home.bin";
                        LogLine.Do($"Updated buffer URI to: temp_quest_home.bin");
                    }
                }

                // Serialize back to bytes
                string modifiedJson = gltf.ToString(Newtonsoft.Json.Formatting.None);
                return System.Text.Encoding.UTF8.GetBytes(modifiedJson);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to strip texture references: {ex.Message}\n{ex.StackTrace}");
                return gltfData; // Return original if parsing fails
            }
        }

        /// <summary>
        /// Load GLTF model from extracted data with animation support
        /// </summary>
        /// <param name="gltfData">GLTF JSON data</param>
        /// <param name="binData">Binary buffer data</param>
        /// <param name="importSettings">Import settings (includes animation looping configuration)</param>
        /// <returns>Tuple containing loaded GameObject and AnimationClip array</returns>
        private async Task<(GameObject model, AnimationClip[] animations)> LoadGLTFModel(byte[] gltfData, byte[] binData, ImportSettings importSettings)
        {
            GameObject model = null;
            AnimationClip[] animations = null;

            try
            {
                // Create temporary file for GLTF (GLTFUtility requires file-based loading)
                string tempGltfPath = System.IO.Path.Combine(Application.temporaryCachePath, "temp_quest_home.gltf");
                string tempBinPath = System.IO.Path.Combine(Application.temporaryCachePath, "temp_quest_home.bin");

                // Strip texture references from GLTF JSON
                // This prevents GLTFUtility from trying to load texture files that don't exist
                // We load textures separately via KTXParser and apply them later
                LogLine.Do("Stripping texture references from GLTF...");
                byte[] modifiedGltfData = StripTextureReferences(gltfData);

                // Debug: Save modified GLTF for inspection
                string debugGltfPath = System.IO.Path.Combine(Application.temporaryCachePath, "debug_quest_home.gltf");
                System.IO.File.WriteAllBytes(debugGltfPath, modifiedGltfData);
                LogLine.Do($"Debug GLTF saved to: {debugGltfPath}");

                // Write temporary files
                System.IO.File.WriteAllBytes(tempGltfPath, modifiedGltfData);
                LogLine.Do($"GLTF file written: {tempGltfPath} ({modifiedGltfData.Length} bytes)");

                if (binData != null && binData.Length > 0)
                {
                    System.IO.File.WriteAllBytes(tempBinPath, binData);
                    LogLine.Do($"BIN file written: {tempBinPath} ({binData.Length} bytes)");
                }
                else
                {
                    LogLine.Do("No BIN data to write");
                }

                // Yield to let Unity process other tasks
                await Task.Yield();

                // Load using GLTFUtility (must run on main thread)
                // GLTFUtility accesses Unity rendering APIs that require the main thread
                LogLine.Do("Loading GLTF with GLTFUtility...");
                AnimationClip[] loadedAnimations;
                model = Importer.LoadFromFile(tempGltfPath, importSettings, out loadedAnimations);
                animations = loadedAnimations;
                LogLine.Do($"GLTFUtility load completed ({animations?.Length ?? 0} animations)");

                // Yield again after loading to keep things responsive
                await Task.Yield();

                if (model != null)
                {
                    // Parent to this GameObject
                    model.transform.SetParent(transform, false);
                    model.transform.localPosition = Vector3.zero;
                    model.transform.localRotation = Quaternion.identity;
                    model.transform.localScale = Vector3.one;
                    model.name = "QuestHomeModel";
                }

                // Cleanup temporary files
                try
                {
                    if (System.IO.File.Exists(tempGltfPath))
                        System.IO.File.Delete(tempGltfPath);
                    if (System.IO.File.Exists(tempBinPath))
                        System.IO.File.Delete(tempBinPath);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Failed to cleanup temp files: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"GLTF loading failed: {ex.Message}");
                throw;
            }

            return (model, animations);
        }

        /// <summary>
        /// Setup colliders on opaque meshes
        /// </summary>
        private void SetupColliders(GameObject model)
        {
            if (model == null) return;

            int collidersAdded = 0;
            var meshFilters = model.GetComponentsInChildren<MeshFilter>();

            foreach (var meshFilter in meshFilters)
            {
                if (meshFilter.sharedMesh == null) continue;

                var renderer = meshFilter.GetComponent<Renderer>();
                if (renderer == null) continue;

                // Check if material is opaque
                bool hasOpaqueMaterial = false;
                foreach (var material in renderer.sharedMaterials)
                {
                    if (GLTFMaterialMapper.IsOpaqueMaterial(material))
                    {
                        hasOpaqueMaterial = true;
                        break;
                    }
                }

                if (hasOpaqueMaterial)
                {
                    // Add MeshCollider
                    var existingCollider = meshFilter.GetComponent<MeshCollider>();
                    if (existingCollider == null)
                    {
                        var collider = meshFilter.gameObject.AddComponent<MeshCollider>();
                        collider.sharedMesh = meshFilter.sharedMesh;
                        collider.convex = false; // Use exact mesh collision
                        collidersAdded++;
                    }

                    // Set to Grabbable layer if climbable is enabled
                    if (climbable)
                    {
                        meshFilter.gameObject.layer = 20; // Layer 20 = Grabbable
                    }
                }
            }

            LogLine.Do($"Added {collidersAdded} mesh colliders");
        }

        /// <summary>
        /// Setup background audio (optional)
        /// </summary>
        private async Task SetupAudio(byte[] audioData)
        {
            try
            {
                // Create temporary audio file
                string tempAudioPath = System.IO.Path.Combine(Application.temporaryCachePath, "quest_home_audio.ogg");
                System.IO.File.WriteAllBytes(tempAudioPath, audioData);
                LogLine.Do($"Audio file written: {tempAudioPath} ({audioData.Length} bytes)");

                AudioClip clip = null;

                // Load audio clip
                using (UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip("file://" + tempAudioPath, AudioType.OGGVORBIS))
                {
                    // Configure download handler to stream audio
                    ((DownloadHandlerAudioClip)request.downloadHandler).streamAudio = false;

                    var operation = request.SendWebRequest();

                    // Wait for completion
                    while (!operation.isDone)
                    {
                        await Task.Yield();
                    }

                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        clip = DownloadHandlerAudioClip.GetContent(request);

                        if (clip != null)
                        {
                            clip.name = "QuestHomeAudio";
                            LogLine.Do($"Audio clip loaded: {clip.length:F2}s, {clip.channels} channels, {clip.frequency}Hz");
                        }
                        else
                        {
                            Debug.LogWarning("DownloadHandlerAudioClip.GetContent returned null");
                        }
                    }
                    else
                    {
                        Debug.LogError($"Audio load failed: {request.error}");
                    }
                }

                // Add AudioSource with loaded clip (outside using block to keep clip alive)
                if (clip != null)
                {
                    // Get existing AudioSource or add a new one
                    var audioSource = gameObject.GetComponent<AudioSource>();
                    if (audioSource == null)
                    {
                        audioSource = gameObject.AddComponent<AudioSource>();
                    }

                    audioSource.clip = clip;
                    audioSource.loop = true;
                    audioSource.playOnAwake = false; // We'll call Play() manually
                    audioSource.volume = 0.5f;
                    audioSource.spatialBlend = 0f; // 2D sound
                    audioSource.Play();

                    LogLine.Do($"Background audio playing: {clip.name}");
                }

                // Cleanup temp file
                try
                {
                    if (System.IO.File.Exists(tempAudioPath))
                        System.IO.File.Delete(tempAudioPath);
                }
                catch { }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to setup audio: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// Set the scene skybox to solid black for Quest Home environments
        /// </summary>
        private void SetSkyboxToBlack()
        {
            try
            {
                // Create or get black skybox material
                Material skyboxMaterial = new Material(Shader.Find("Skybox/Procedural"));
                skyboxMaterial.SetColor("_SkyTint", Color.black);
                skyboxMaterial.SetColor("_GroundColor", Color.black);
                skyboxMaterial.SetFloat("_SunSize", 0f);
                skyboxMaterial.SetFloat("_SunSizeConvergence", 0f);
                skyboxMaterial.SetFloat("_AtmosphereThickness", 0f);
                skyboxMaterial.SetFloat("_Exposure", 0f);

                // Apply to RenderSettings
                RenderSettings.skybox = skyboxMaterial;
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = Color.black;

                LogLine.Do("Skybox set to black");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to set skybox to black: {ex.Message}");
            }
        }

        /// <summary>
        /// Setup animations on the loaded model with looping
        /// </summary>
        /// <param name="model">Root GameObject with animations</param>
        /// <param name="animations">Array of animation clips to add</param>
        private void SetupAnimations(GameObject model, AnimationClip[] animations)
        {
            if (model == null || animations == null || animations.Length == 0)
            {
                LogLine.Do("No animations to setup");
                return;
            }

            try
            {
                // Add Animation component (legacy) - simpler for Quest Home environments
                Animation animation = model.GetComponent<Animation>();
                if (animation == null)
                {
                    animation = model.AddComponent<Animation>();
                }

                // Add all animation clips to the Animation component
                foreach (AnimationClip clip in animations)
                {
                    animation.AddClip(clip, clip.name);
                }

                // Configure and play all animation states
                // Use layers so each clip can play independently and simultaneously
                int clipsPlayed = 0;
                int layerIndex = 0;

                foreach (AnimationState state in animation)
                {
                    if (state.clip != null)
                    {
                        // Assign each clip to a unique layer for simultaneous playback
                        state.layer = layerIndex++;

                        // Set to loop mode
                        state.wrapMode = WrapMode.Loop;

                        // Enable the animation state with full weight
                        state.enabled = true;
                        state.weight = 1.0f;

                        // Start playing from the beginning
                        state.time = 0f;

                        clipsPlayed++;

                        LogLine.Do($"Playing animation: '{state.name}' on layer {state.layer} (Loop, {state.clip.length:F2}s)");
                    }
                }

                LogLine.Do($"Animation setup complete: {clipsPlayed} clip(s) configured and playing simultaneously");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to setup animations: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles property changes from JavaScript or C# API
        /// </summary>
        internal void UpdateCallback(List<PropertyName> changedProperties)
        {
            if (changedProperties.Contains(PropertyName.url))
            {
                // Reset and reload if URL changes
                loadStarted = false;
                if (loadedModel != null)
                {
                    Destroy(loadedModel);
                    loadedModel = null;
                }
            }
            LoadQuestHome();
        }

        /// <summary>
        /// Cleanup loaded assets
        /// </summary>
        internal override void DestroyStuff()
        {
            if (loadedModel != null)
            {
                Destroy(loadedModel);
                loadedModel = null;
            }
        }
        internal override void UpdateStuff()
        {
            
        }
        // BANTER COMPILED CODE 
        public System.String Url { get { return url; } set { url = value; UpdateCallback(new List<PropertyName> { PropertyName.url }); } }
        public System.Boolean AddColliders { get { return addColliders; } set { addColliders = value; UpdateCallback(new List<PropertyName> { PropertyName.addColliders }); } }
        public System.Boolean Climbable { get { return climbable; } set { climbable = value; UpdateCallback(new List<PropertyName> { PropertyName.climbable }); } }

        BanterScene _scene;
        public BanterScene scene
        {
            get
            {
                if (_scene == null)
                {
                    _scene = BanterScene.Instance();
                }
                return _scene;
            }
        }
        bool alreadyStarted = false;
        void Start()
        {
            Init();
            StartStuff();
        }

        internal override void ReSetup()
        {
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.url, PropertyName.addColliders, PropertyName.climbable, };
            UpdateCallback(changedProperties);
        }
        internal override string GetSignature()
        {
            return "BanterQuestHome" +  PropertyName.url + url + PropertyName.addColliders + addColliders + PropertyName.climbable + climbable;
        }

        internal override void Init(List<object> constructorProperties = null)
        {
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(gameObject.GetInstanceID(), GetInstanceID(), ComponentType.BanterQuestHome);


            oid = gameObject.GetInstanceID();
            cid = GetInstanceID();

            if (constructorProperties != null)
            {
                Deserialise(constructorProperties);
            }

            SyncProperties(true);

        }

        void Awake()
        {
            BanterScene.Instance().RegisterComponentOnMainThread(gameObject, this);
        }

        void OnDestroy()
        {
            scene.UnregisterComponentOnMainThread(gameObject, this);

            DestroyStuff();
        }

        internal override object CallMethod(string methodName, List<object> parameters)
        {
            return null;
        }

        internal override void Deserialise(List<object> values)
        {
            List<PropertyName> changedProperties = new List<PropertyName>();
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] is BanterString)
                {
                    var valurl = (BanterString)values[i];
                    if (valurl.n == PropertyName.url)
                    {
                        url = valurl.x;
                        changedProperties.Add(PropertyName.url);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valaddColliders = (BanterBool)values[i];
                    if (valaddColliders.n == PropertyName.addColliders)
                    {
                        addColliders = valaddColliders.x;
                        changedProperties.Add(PropertyName.addColliders);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valclimbable = (BanterBool)values[i];
                    if (valclimbable.n == PropertyName.climbable)
                    {
                        climbable = valclimbable.x;
                        changedProperties.Add(PropertyName.climbable);
                    }
                }
            }
            if (values.Count > 0) { UpdateCallback(changedProperties); }
        }

        internal override void SyncProperties(bool force = false, Action callback = null)
        {
            var updates = new List<BanterComponentPropertyUpdate>();
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.url,
                    type = PropertyType.String,
                    value = url,
                    componentType = ComponentType.BanterQuestHome,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.addColliders,
                    type = PropertyType.Bool,
                    value = addColliders,
                    componentType = ComponentType.BanterQuestHome,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.climbable,
                    type = PropertyType.Bool,
                    value = climbable,
                    componentType = ComponentType.BanterQuestHome,
                    oid = oid,
                    cid = cid
                });
            }
            scene.SetFromUnityProperties(updates, callback);
        }

        internal override void WatchProperties(PropertyName[] properties)
        {
        }
        // END BANTER COMPILED CODE 
    }
}