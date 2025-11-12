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
    - `legacyShaderFix` - Convert materials to unlit shaders (Quest optimization).

    **Code Example**
    ```js
        const url = "https://cdn.sidequestvr.com/file/167567/canyon_environment.apk";
        const gameObject = new BS.GameObject("MyQuestHome");
        const questHome = await gameObject.AddComponent(new BS.BanterQuestHome(url, true, true));
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

        [Tooltip("Enable to convert materials to unlit shaders (Quest optimization).")]
        [See(initial = "true")][SerializeField] internal bool legacyShaderFix = true;

        private bool loadStarted;
        private GameObject loadedModel;
        private bool alreadyStarted = false;

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
                return;
            }

            _loaded = false;
            loadStarted = true;

            try
            {
                LogLine.Do($"Starting Quest Home load from: {url}");

                // Step 0: Resolve URL (handles both direct APK URLs and SideQuest listing URLs)
                LogLine.Do("Resolving URL...");
                string resolvedApkUrl = await SideQuestUrlResolver.ResolveApkUrl(url);
                if (resolvedApkUrl != url)
                {
                    LogLine.Do($"Resolved listing URL to APK URL: {resolvedApkUrl}");
                }

                // Step 1: Download APK
                LogLine.Do("Downloading APK...");
                byte[] apkData = await DownloadAPK(resolvedApkUrl);
                LogLine.Do($"APK downloaded ({apkData.Length / 1024 / 1024} MB)");

                // Step 2: Extract files
                LogLine.Do("Extracting APK contents...");
                QuestHomeAssets assets = APKExtractor.ExtractAll(apkData);

                if (!APKExtractor.ValidateAssets(assets))
                {
                    throw new Exception("Extracted assets validation failed");
                }

                // Step 3: Load textures
                LogLine.Do($"Loading {assets.textures.Count} textures...");
                Dictionary<string, Texture2D> loadedTextures = await LoadTextures(assets.textures);
                LogLine.Do($"Loaded {loadedTextures.Count} textures");

                // Step 4: Parse GLTF for texture mappings
                LogLine.Do("Parsing GLTF material mappings...");
                List<TextureMapping> textureMappings = GLTFMaterialMapper.ParseGLTF(assets.gltfJson);

                // Step 5: Load GLTF model with animations
                LogLine.Do("Loading GLTF model with animations...");

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
                LogLine.Do("GLTF model loaded successfully");

                // Step 5b: Setup animations if present
                if (animations != null && animations.Length > 0)
                {
                    LogLine.Do($"Setting up {animations.Length} animations...");
                    SetupAnimations(model, animations);
                }

                // Step 6: Apply textures to materials
                // This also applies correct unlit shaders based on alpha mode (OPAQUE/MASK/BLEND)
                LogLine.Do("Applying textures to materials with alpha-aware shaders...");
                GLTFMaterialMapper.ApplyTextures(model, loadedTextures, textureMappings);

                // Step 7: Convert remaining materials to unlit shaders if requested
                // (ApplyTextures already handles materials with textures)
                if (legacyShaderFix)
                {
                    LogLine.Do("Converting any remaining materials to unlit shaders...");
                    GLTFMaterialMapper.ConvertAllToUnlit(model);
                }

                // Step 8: Setup colliders
                if (addColliders)
                {
                    LogLine.Do("Setting up colliders...");
                    SetupColliders(model);
                }

                // Step 9: Load background audio (optional)
                if (assets.audioData != null && assets.audioData.Length > 0)
                {
                    LogLine.Do("Loading background audio...");
                    await SetupAudio(assets.audioData);
                }

                // Step 10: Set skybox to black for Quest Home environments
                SetSkyboxToBlack();

                LogLine.Do("Quest Home loaded successfully!");

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
                loadStarted = false;
            }
        }

        /// <summary>
        /// Download APK from URL with progress tracking and retry logic
        /// </summary>
        private async Task<byte[]> DownloadAPK(string apkUrl, int maxRetries = 3)
        {
            Exception lastException = null;

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

                    if (attempt < maxRetries)
                    {
                        // Wait before retry (exponential backoff: 1s, 2s, 4s)
                        int delayMs = 1000 * (int)Math.Pow(2, attempt - 1);
                        LogLine.Do($"Retrying in {delayMs}ms...");
                        await Task.Delay(delayMs);
                    }
                }
            }

            throw new Exception($"Download failed after {maxRetries} attempts. Last error: {lastException?.Message}", lastException);
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
                    var audioSource = gameObject.AddComponent<AudioSource>();
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

                // Add all animation clips
                for (int i = 0; i < animations.Length; i++)
                {
                    AnimationClip clip = animations[i];
                    animation.AddClip(clip, clip.name);

                    if (i == 0)
                    {
                        // Set first animation as default
                        animation.clip = clip;
                        animation.playAutomatically = true;
                    }

                    LogLine.Do($"Added animation: {clip.name} (length: {clip.length:F2}s, looping: {clip.wrapMode == WrapMode.Loop})");
                }

                // Start playing the first animation
                if (animations.Length > 0 && animation.clip != null)
                {
                    animation.Play();
                    LogLine.Do($"Playing animation: {animation.clip.name}");
                }
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
        /// Public C# API property for URL
        /// </summary>
        public System.String Url
        {
            get { return url; }
            set { url = value; UpdateCallback(new List<PropertyName> { PropertyName.url }); }
        }

        /// <summary>
        /// Public C# API property for AddColliders
        /// </summary>
        public System.Boolean AddColliders
        {
            get { return addColliders; }
            set { addColliders = value; UpdateCallback(new List<PropertyName> { PropertyName.addColliders }); }
        }

        /// <summary>
        /// Public C# API property for LegacyShaderFix
        /// </summary>
        public System.Boolean LegacyShaderFix
        {
            get { return legacyShaderFix; }
            set { legacyShaderFix = value; UpdateCallback(new List<PropertyName> { PropertyName.legacyShaderFix }); }
        }

        /// <summary>
        /// Unity Awake - Register component with BanterScene
        /// </summary>
        void Awake()
        {
            BanterScene.Instance().RegisterComponentOnMainThread(gameObject, this);
        }

        /// <summary>
        /// Unity Start - Initialize and start component
        /// </summary>
        void Start()
        {
            Init();
            StartStuff();
        }

        /// <summary>
        /// Cleanup when component is destroyed
        /// </summary>
        void OnDestroy()
        {
            scene.UnregisterComponentOnMainThread(gameObject, this);
            DestroyStuff();
        }

        // ========== ABSTRACT METHOD IMPLEMENTATIONS ==========

        /// <summary>
        /// Per-frame update (not called - BanterQuestHome has no [Watch] properties)
        /// </summary>
        internal override void UpdateStuff()
        {

        }

        /// <summary>
        /// Watch properties for changes (unused - handled automatically)
        /// </summary>
        internal override void WatchProperties(PropertyName[] properties)
        {
            // Empty implementation - property watching handled by UpdateCallback
        }

        /// <summary>
        /// Deserialize property updates from JavaScript
        /// </summary>
        internal override void Deserialise(List<object> values)
        {
            List<PropertyName> changedProperties = new List<PropertyName>();

            for (int i = 0; i < values.Count; i++)
            {
                // Handle string properties (url)
                if (values[i] is BanterString)
                {
                    var valurl = (BanterString)values[i];
                    if (valurl.n == PropertyName.url)
                    {
                        url = valurl.x;
                        changedProperties.Add(PropertyName.url);
                    }
                }

                // Handle boolean properties (addColliders, legacyShaderFix)
                if (values[i] is BanterBool)
                {
                    var valbool = (BanterBool)values[i];
                    if (valbool.n == PropertyName.addColliders)
                    {
                        addColliders = valbool.x;
                        changedProperties.Add(PropertyName.addColliders);
                    }
                    else if (valbool.n == PropertyName.legacyShaderFix)
                    {
                        legacyShaderFix = valbool.x;
                        changedProperties.Add(PropertyName.legacyShaderFix);
                    }
                }
            }

            if (values.Count > 0)
            {
                UpdateCallback(changedProperties);
            }
        }

        /// <summary>
        /// Call method from JavaScript (not used for BanterQuestHome)
        /// </summary>
        internal override object CallMethod(string methodName, List<object> parameters)
        {
            return null; // No callable methods for BanterQuestHome
        }

        /// <summary>
        /// Sync properties from Unity to JavaScript
        /// </summary>
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

                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.addColliders,
                    type = PropertyType.Bool,
                    value = addColliders,
                    componentType = ComponentType.BanterQuestHome,
                    oid = oid,
                    cid = cid
                });

                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.legacyShaderFix,
                    type = PropertyType.Bool,
                    value = legacyShaderFix,
                    componentType = ComponentType.BanterQuestHome,
                    oid = oid,
                    cid = cid
                });
            }

            scene.SetFromUnityProperties(updates, callback);
        }

        /// <summary>
        /// Reset and reload the component with all properties
        /// </summary>
        internal override void ReSetup()
        {
            List<PropertyName> changedProperties = new List<PropertyName>()
            {
                PropertyName.url,
                PropertyName.addColliders,
                PropertyName.legacyShaderFix
            };
            UpdateCallback(changedProperties);
        }

        /// <summary>
        /// Initialize component with BanterScene
        /// </summary>
        internal override void Init(List<object> constructorProperties = null)
        {
            if (alreadyStarted) { return; }
            alreadyStarted = true;

            // Only register with scene if link is available (not in standalone mode)
            if (scene.link != null)
            {
                scene.RegisterBanterMonoscript(gameObject.GetInstanceID(), GetInstanceID(), ComponentType.BanterQuestHome);
            }

            oid = gameObject.GetInstanceID();
            cid = GetInstanceID();

            if (constructorProperties != null)
            {
                Deserialise(constructorProperties);
            }

            // Only sync properties if link is available
            if (scene.link != null)
            {
                SyncProperties(true);
            }
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
    }
}