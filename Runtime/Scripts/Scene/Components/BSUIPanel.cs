using System;
using System.Collections;
using System.Collections.Generic;
using BS.UI.Bridge;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.XR;

namespace BS
{
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BSObjectId))]
    [WatchComponent]

    public class BSUIPanel : BSComponentBase
    {
        private const string LogPrefix = "[BSUIPanel]";

        [System.Diagnostics.Conditional("BANTER_UI_DEBUG")]
        private static void LogVerbose(string message)
        {
            Debug.Log($"{LogPrefix} {message}");
        }

        PanelSettings panelSettings;
        UIDocument uiDocument;
        public RenderTexture renderTexture;
        UIElementBridge uiElementBridge;

        [See(initial = "512,512")][SerializeField] internal Vector2 resolution = new Vector2(512,512);
        [See(initial = "false")][HideInInspector][SerializeField] internal bool screenSpace = false;

        [Tooltip("Render onto the mesh already on this object instead of UI Toolkit's own flat quad, and " +
                 "take pointer input from that mesh's UVs, so the panel can be any shape. Ignored in screen space.")]
        [See(initial = "false")][SerializeField] internal bool meshInput = false;

        [Header("Haptics")]
        [See(initial = "false")][SerializeField] internal bool enableHaptics = false;
        [See(initial = "0.5,0.1")][SerializeField] internal Vector2 clickHaptic = new Vector2(0.5f, 0.1f); // amplitude, duration
        [See(initial = "0.3,0.05")][SerializeField] internal Vector2 enterHaptic = new Vector2(0.3f, 0.05f); // amplitude, duration
        [See(initial = "0.2,0.05")][SerializeField] internal Vector2 exitHaptic = new Vector2(0.2f, 0.05f); // amplitude, duration

        [Header("Sounds")]
        [See(initial = "false")][SerializeField] internal bool enableSounds = false;
        [Tooltip("Click sound - can be assigned directly or loaded from URL via JS")]
        [SerializeField] private AudioClip clickSound;
        [Tooltip("Enter/Hover sound - can be assigned directly or loaded from URL via JS")]
        [SerializeField] private AudioClip enterSound;
        [Tooltip("Exit sound - can be assigned directly or loaded from URL via JS")]
        [SerializeField] private AudioClip exitSound;

        // Internal URL storage for JS synchronization
        [See(initial = "")][HideInInspector][SerializeField] internal string clickSoundUrl = "";
        [See(initial = "")][HideInInspector][SerializeField] internal string enterSoundUrl = "";
        [See(initial = "")][HideInInspector][SerializeField] internal string exitSoundUrl = "";

        private AudioSource audioSource;

        private InputDevice _leftDevice;
        private InputDevice _rightDevice;
        
        // Internal panel management
        private static int nextPanelId = 0;
        private int internalPanelId = -1;


        [Method]
        public void _SetBackgroundColor(Vector4 color)
        {
            if (uiDocument != null && uiDocument.rootVisualElement != null)
            {
                uiDocument.rootVisualElement.style.backgroundColor = new StyleColor(new Color(color.x, color.y, color.z, color.w));
            }
        }

        /// <summary>
        /// Gets the render texture used by this panel (null if in screen space mode)
        /// </summary>
        public RenderTexture RenderTexture
        {
            get
            {
                // Return the actual render texture from panel settings if available
                // This is the texture that UI Toolkit is actively rendering to
                if (uiDocument != null && uiDocument.panelSettings != null)
                {
                    var targetTex = uiDocument.panelSettings.targetTexture;
                    if (targetTex != null)
                    {
                        Debug.Log($"[BSUIPanel] RenderTexture: {targetTex.name}, Size: {targetTex.width}x{targetTex.height}, Format: {targetTex.format}, IsCreated: {targetTex.IsCreated()}");
                    }
                    else
                    {
                        Debug.LogWarning($"[BSUIPanel] uiDocument.panelSettings.targetTexture is null! ScreenSpace: {screenSpace}");
                    }
                    return targetTex;
                }

                Debug.LogWarning($"[BSUIPanel] UIDocument or panelSettings is null. Returning private renderTexture field.");
                return renderTexture;
            }
        }

        /// <summary>
        /// Gets the formatted panel ID for UI commands - used by Visual Scripting nodes
        /// Uses object ID and component ID for consistency with TypeScript side
        /// </summary>
        /// <returns>Formatted panel ID string</returns>
        public string GetFormattedPanelId()
        {
            return $"panel_{oid}_{cid}";
        }

        /// <summary>
        /// Gets the internal panel settings name based on internal panel ID
        /// This is used internally to determine which PanelSettings resource to load
        /// </summary>
        /// <returns>Panel settings resource name</returns>
        private string GetPanelSettingsName()
        {
            return screenSpace ? "ScreenSpace" : "WorldSpace";
        }

        /// <summary>
        /// Validates that the panel is ready for UI operations - used by Visual Scripting nodes
        /// </summary>
        /// <param name="operationName">Name of the operation for logging</param>
        /// <returns>True if panel is ready for UI operations</returns>
        public bool ValidateForUIOperation(string operationName)
        {
            // Initialize panel if not already initialized
            if (uiElementBridge == null && uiDocument == null)
            {
                if (!InitializePanel())
                {
                    Debug.LogWarning($"[{operationName}] Failed to initialize panel.");
                    return false;
                }
            }

            if (uiElementBridge == null)
            {
                Debug.LogWarning($"[{operationName}] UI Element Bridge is not initialized. Make sure the panel is properly set up.");
                return false;
            }

            if (internalPanelId == -1)
            {
                Debug.LogWarning($"[{operationName}] Panel ID is not assigned. Panel may not be initialized.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Initialize panel - creates UIDocument if needed or uses existing one
        /// </summary>
        /// <param name="document">Optional UIDocument with existing panel settings</param>
        /// <returns>True if initialization was successful</returns>
        public bool InitializePanel(UIDocument document = null)
        {
            try
            {
                if (document != null && document.panelSettings != null)
                {
                    // Use the existing UIDocument and its panel settings
                    // Shared asset, not a clone — mesh mode must not rewrite it.
                    panelSettings = document.panelSettings;
                    ownsPanelSettings = false;
                    uiDocument = document;
                    LogVerbose($"Using existing UIDocument with panel settings: {panelSettings.name}");
                }
                else
                {
                    // Create or get UIDocument and load panel settings from resources
                    uiDocument = gameObject.GetComponent<UIDocument>();
                    if (uiDocument == null)
                    {
                        uiDocument = gameObject.AddComponent<UIDocument>();
                        gameObject.AddComponent<PanelRaycaster>();
                        gameObject.AddComponent<PanelEventHandler>();
                        gameObject.AddComponent<AddPanelStuff>();
                        createdUIDocument = true;
                    }
                    
                    // Load panel settings from resources using internal panel ID
                    var panelSettingsName = GetPanelSettingsName();
                    panelSettings = Resources.Load<PanelSettings>($"UI/{panelSettingsName}");
                    panelSettings = Instantiate(panelSettings);
                    // Our own copy, so mesh mode is free to rewrite renderMode/targetTexture on it.
                    ownsPanelSettings = true;
                    if (internalPanelId > 19)
                    {
                        Debug.LogWarning($"[BSUIPanel] Internal panel ID {internalPanelId} exceeds maximum of 19. Using panel settings for ID 19.");

                    }
                    if (panelSettings == null)
                    {
                        Debug.LogError($"[BSUIPanel] Failed to load PanelSettings: {panelSettingsName}. Make sure the asset exists in Resources/UI/ folder.");
                        return false;
                    }
                    
                    uiDocument.panelSettings = panelSettings;
                    uiDocument.worldSpaceSizeMode = UIDocument.WorldSpaceSizeMode.Fixed;
                    uiDocument.worldSpaceSize = new Vector2(resolution.x, resolution.y); // Convert pixels to meters
                    uiDocument.panelSettings.referenceSpritePixelsPerUnit = 500;
                    uiDocument.panelSettings.scaleMode = PanelScaleMode.ConstantPhysicalSize;
                    LogVerbose($"Created UIDocument with loaded panel settings: {panelSettingsName}");
                }
                
                LogVerbose($"Initialized with existing UIDocument and panel settings: {panelSettings.name}");

                // Add stylesheets
                // uiDocument.rootVisualElement.styleSheets.Add(Resources.Load<StyleSheet>("UI/Slider"));
                // uiDocument.rootVisualElement.styleSheets.Add(Resources.Load<StyleSheet>("UI/SwitchToggle"));
                // uiDocument.rootVisualElement.styleSheets.Add(Resources.Load<StyleSheet>("UI/Button"));

                // Configure panel settings
                uiDocument.panelSettings.scaleMode = PanelScaleMode.ConstantPixelSize;

                // Ensure required components exist
                if (gameObject.GetComponent<PanelRaycaster>() == null)
                {
                    gameObject.AddComponent<PanelRaycaster>();
                }
                if (gameObject.GetComponent<PanelEventHandler>() == null)
                {
                    gameObject.AddComponent<PanelEventHandler>();
                }
                if (gameObject.GetComponent<AddPanelStuff>() == null)
                {
                    gameObject.AddComponent<AddPanelStuff>();
                }

                // Set up the bridge
                if (uiElementBridge == null)
                {
                    uiElementBridge = gameObject.AddComponent<UIElementBridge>();
                    uiElementBridge.banterLink = scene.link;
                    uiElementBridge.mainDocument = uiDocument;
                }

                // Register with object and component ID for consistency
                var registrationId = GetFormattedPanelId();
                UIElementBridge.RegisterPanelInstance(registrationId, uiElementBridge, this);

                LogVerbose($"Successfully initialized panel with ID: {registrationId}");

                // Auto-register UXML elements if the UIDocument has a visual tree asset
                if (uiDocument.visualTreeAsset != null && uiDocument.rootVisualElement != null)
                {
                    LogVerbose($"Auto-registering UXML elements from visual tree asset");
                    var elementMap = uiElementBridge.ProcessUXMLTree(uiDocument, "uxml");
                    LogVerbose($"Auto-registered {elementMap.Count} elements from UXML");
                }

                // Initialize haptics and sounds
                InitializeHapticsAndSounds();

                // Handle screen space vs world space setup
                SetupRenderingMode();

                // Update tracking
                UpdateScreenSpaceTracking(this, screenSpace);
                scene.events.OnBanterUiPanelActiveChanged?.Invoke();
                SetLoadedIfNot();

                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[BSUIPanel] Failed to initialize with existing document: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Initialize panel using existing UIDocument and its panel settings (legacy method for backward compatibility)
        /// </summary>
        /// <param name="document">UIDocument with existing panel settings</param>
        /// <returns>True if initialization was successful</returns>
        public bool InitializeWithExistingDocument(UIDocument document)
        {
            return InitializePanel(document);
        }

        /// <summary>
        /// Initialize haptics and sounds for UI interactions
        /// </summary>
        private async void InitializeHapticsAndSounds()
        {
            if (enableHaptics)
            {
                UpdateControllerDevices();
            }

            if (enableSounds && audioSource == null)
            {
                audioSource = gameObject.GetComponent<AudioSource>();
                if (audioSource == null)
                {
                    audioSource = gameObject.AddComponent<AudioSource>();
                    audioSource.spatialBlend = 0f; // 2D sound
                    audioSource.playOnAwake = false;
                }
            }

            // Load audio clips from URLs if provided and no clip is directly assigned
            if (enableSounds)
            {
                if (clickSound == null && !string.IsNullOrEmpty(clickSoundUrl))
                {
                    clickSound = await Get.Audio(clickSoundUrl);
                    // StartCoroutine(LoadAudioClip(clickSoundUrl, clip => clickSound = clip));
                }
                if (enterSound == null && !string.IsNullOrEmpty(enterSoundUrl))
                {
                    enterSound = await Get.Audio(enterSoundUrl);
                    // StartCoroutine(LoadAudioClip(enterSoundUrl, clip => enterSound = clip));
                }
                if (exitSound == null && !string.IsNullOrEmpty(exitSoundUrl))
                {
                    exitSound = await Get.Audio(exitSoundUrl);
                    // StartCoroutine(LoadAudioClip(exitSoundUrl, clip => exitSound = clip));
                }
            }

            if ((enableHaptics || enableSounds) && uiDocument != null && uiDocument.rootVisualElement != null)
            {
                RegisterUIEventHandlers(uiDocument.rootVisualElement);
            }
        }


        /// <summary>
        /// Update controller device references
        /// </summary>
        private void UpdateControllerDevices()
        {
            _leftDevice = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            _rightDevice = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        }

        /// <summary>
        /// Register haptic and sound event handlers for all interactive UI elements
        /// </summary>
        /// <param name="root">Root visual element to register handlers on</param>
        public void RegisterUIEventHandlers(VisualElement root)
        {
            if (root == null) return;

            // Register on all Button, Toggle, Slider, and other interactive elements
            root.Query<VisualElement>().ForEach(element =>
            {
                if (element is Button || element is Toggle || element is Slider ||
                    element.pickingMode == PickingMode.Position)
                {
                    RegisterElementEvents(element);
                }
            });
        }

        /// <summary>
        /// Register events on a specific visual element
        /// </summary>
        /// <param name="element">Visual element to register events on</param>
        public void RegisterElementEvents(VisualElement element)
        {
            if (element == null) return;

            // Unregister first to avoid duplicates
            element.UnregisterCallback<PointerDownEvent>(OnUIClick, TrickleDown.TrickleDown);
            element.UnregisterCallback<PointerEnterEvent>(OnUIEnter);
            element.UnregisterCallback<PointerLeaveEvent>(OnUIExit);

            // Register new callbacks
            if (enableHaptics || enableSounds)
            {
                element.RegisterCallback<PointerDownEvent>(OnUIClick, TrickleDown.TrickleDown);
                element.RegisterCallback<PointerEnterEvent>(OnUIEnter);
                element.RegisterCallback<PointerLeaveEvent>(OnUIExit);
            }
        }

        /// <summary>
        /// Unregister events from a specific visual element
        /// </summary>
        /// <param name="element">Visual element to unregister events from</param>
        public void UnregisterElementEvents(VisualElement element)
        {
            if (element == null) return;

            element.UnregisterCallback<PointerDownEvent>(OnUIClick, TrickleDown.TrickleDown);
            element.UnregisterCallback<PointerEnterEvent>(OnUIEnter);
            element.UnregisterCallback<PointerLeaveEvent>(OnUIExit);
        }

        private void OnUIClick(PointerDownEvent evt)
        {
            if (enableHaptics)
            {
                SendHapticPulse(clickHaptic.x, clickHaptic.y);
            }

            if (enableSounds && clickSound != null && audioSource != null)
            {
                audioSource.PlayOneShot(clickSound);
            }
        }

        private void OnUIEnter(PointerEnterEvent evt)
        {
            if (enableHaptics)
            {
                SendHapticPulse(enterHaptic.x, enterHaptic.y);
            }

            if (enableSounds && enterSound != null && audioSource != null)
            {
                audioSource.PlayOneShot(enterSound);
            }
        }

        private void OnUIExit(PointerLeaveEvent evt)
        {
            if (enableHaptics)
            {
                SendHapticPulse(exitHaptic.x, exitHaptic.y);
            }

            if (enableSounds && exitSound != null && audioSource != null)
            {
                audioSource.PlayOneShot(exitSound);
            }
        }

        /// <summary>
        /// Send haptic pulse to both controllers
        /// </summary>
        private void SendHapticPulse(float amplitude, float duration)
        {
            SendHapticPulseToDevice(_leftDevice, amplitude, duration);
            SendHapticPulseToDevice(_rightDevice, amplitude, duration);
        }

        /// <summary>
        /// Send haptic pulse to a specific device
        /// </summary>
        private void SendHapticPulseToDevice(InputDevice device, float amplitude, float duration)
        {
            if (device.isValid && device.TryGetHapticCapabilities(out HapticCapabilities capabilities) &&
                capabilities.supportsImpulse)
            {
                device.SendHapticImpulse(0, amplitude, duration);
            }
        }

        /// <summary>
        /// Sets up rendering mode based on screenSpace setting
        /// </summary>
        private void SetupRenderingMode()
        {
            gameObject.layer = LayerMask.NameToLayer("Menu");
            
            if (!screenSpace)
            {
                // World space setup - create render texture and mesh
                if (renderTexture == null)
                {
                    renderTexture = new RenderTexture((int)resolution.x, (int)resolution.y, 16, RenderTextureFormat.ARGB32);
                    renderTexture.Create();
                }

                if (uiDocument != null)
                {
                    uiDocument.panelSettings.targetTexture = renderTexture;
                }

                if (meshInput)
                {
                    BeginMeshBinding();
                }
            }
            else
            {
                // Screen space setup - no render texture needed
                if (renderTexture != null)
                {
                    renderTexture.Release();
                    Destroy(renderTexture);
                    renderTexture = null;
                }
                
                if (uiDocument != null)
                {
                    uiDocument.panelSettings.targetTexture = null;
                }
                
                var renderer = gameObject.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    renderer.enabled = false;
                }
            }
        }

        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        static readonly int UnlitColorMapId = Shader.PropertyToID("_UnlitColorMap");

        private MeshCollider meshInputCollider;
        private Mesh colliderMesh;
        private Mesh colliderSource;
        private Mesh rejectedMesh;
        private Material meshMaterial;
        private PanelSettings boundSettings;
        private bool createdMeshCollider;
        private bool meshBound;
        private bool ownsPanelSettings;
        private Coroutine meshBindRoutine;

        /// <summary>
        /// True while this panel is showing on its own mesh and taking pointer input from that
        /// mesh's UVs rather than from UI Toolkit's flat quad.
        /// </summary>
        public bool UsesMeshInput => meshInput && !screenSpace;

        /// <summary>
        /// The collider the input bridge raycasts to turn a pointer ray into a texture UV.
        /// Null until the mesh has arrived and been bound.
        /// </summary>
        public MeshCollider MeshInputCollider => meshInputCollider;

        /// <summary>
        /// Waits for the mesh to exist, then routes the panel onto it. The mesh normally arrives
        /// from JS a little after the panel does, so this cannot be a one-shot.
        /// </summary>
        /// <summary>
        /// Coroutines do not survive the object being deactivated, and a panel commonly sits
        /// disabled for a few frames while its geometry loads. Without this, mesh mode would go
        /// quiet for the rest of the session and the panel would keep drawing the flat quad.
        /// </summary>
        private void OnEnable()
        {
            if (UsesMeshInput && meshBindRoutine == null)
                BeginMeshBinding();
        }

        private void BeginMeshBinding()
        {
            if (!isActiveAndEnabled) return;
            if (meshBindRoutine != null) StopCoroutine(meshBindRoutine);
            meshBindRoutine = StartCoroutine(BindMeshWhenReady());
        }

        /// <summary>
        /// Keeps watching rather than binding once. The mesh and material arrive from JS after the
        /// panel does, and either can be replaced later — a BSGeometry property change hands the
        /// MeshFilter a brand new Mesh — which would otherwise leave the collider hit-testing a
        /// shape that is no longer on screen.
        /// </summary>
        private IEnumerator BindMeshWhenReady()
        {
            var deadline = Time.realtimeSinceStartup + 30f;
            var warned = false;

            while (UsesMeshInput)
            {
                if (TryBindMesh(out var status))
                {
                    if (!meshBound)
                    {
                        meshBound = true;
                        LogVerbose($"Mesh input bound: {status}");
                    }
                }
                else if (!meshBound && !warned && Time.realtimeSinceStartup > deadline)
                {
                    warned = true;
                    Debug.LogWarning($"{LogPrefix} meshInput is on but nothing has bound yet: {status}");
                }

                yield return null;
            }

            meshBindRoutine = null;
        }

        private bool TryBindMesh(out string status)
        {
            if (uiDocument == null || uiDocument.panelSettings == null) { status = "no UIDocument yet"; return false; }
            if (renderTexture == null) { status = "no render texture"; return false; }

            // Leaves the shared PanelSettings asset alone. Only the panel that instantiated its own
            // copy may rewrite renderMode/targetTexture on it — doing that to the shared asset would
            // drag every other document using it into this panel's texture.
            if (!ownsPanelSettings) { status = "panel settings are shared, not this panel's own"; return false; }

            var filter = GetComponent<MeshFilter>();
            var mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null) { status = "no mesh yet"; return false; }

            var meshRenderer = GetComponent<MeshRenderer>();
            if (meshRenderer == null) { status = "no MeshRenderer yet"; return false; }

            // Already set up and nothing has changed underneath us. The texture is part of that
            // check and not an afterthought: a resolution change destroys the render texture and
            // creates a replacement, and without this the material would still be holding the
            // destroyed one — which reads back as null, so the mesh samples nothing and the panel
            // goes blank while still drawing happily into the new texture.
            if (meshBound && mesh == colliderSource && meshInputCollider != null &&
                meshRenderer.sharedMaterial == meshMaterial && HoldsTexture(meshMaterial, renderTexture))
            {
                status = "bound";
                return true;
            }

            // Everything is checked before anything is changed: this runs every frame until the
            // pieces arrive, and half-configuring the panel on each of those frames would rebuild it
            // repeatedly. Building the collider first also means a mesh that cannot be hit-tested
            // never gets the panel switched into a mode where nothing else can pick it either.
            if (!HasTextureSlot(meshRenderer))
            {
                status = $"material '{meshRenderer.sharedMaterial?.shader?.name ?? "none"}' has no texture slot yet";
                return false;
            }

            if (!BuildMeshCollider(mesh))
            {
                status = $"mesh '{mesh.name}' cannot be used as a pointer target";
                return false;
            }

            var settings = uiDocument.panelSettings;

            // World-space mode draws onto UI Toolkit's own quad and ignores the target texture
            // entirely. The overlay path is what actually routes the UI into the texture.
            if (settings.renderMode != PanelRenderMode.ScreenSpaceOverlay)
                settings.renderMode = PanelRenderMode.ScreenSpaceOverlay;
            if (settings.targetTexture != renderTexture)
                settings.targetTexture = renderTexture;

            // The world-space asset ships with clearing off, which is right when the panel draws
            // straight into the scene and wrong the moment it owns a texture: nothing would ever
            // wipe last frame's contents, and a texture that has only just been created holds
            // whatever was in that memory. Reading black is the usual result.
            if (!settings.clearColor)
                settings.clearColor = true;

            // The input bridge treats the render texture as panel space 1:1, which only holds
            // while the panel is unscaled.
            if (settings.scaleMode != PanelScaleMode.ConstantPixelSize)
                settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            if (!Mathf.Approximately(settings.scale, 1f))
                settings.scale = 1f;

            boundSettings = settings;

            ResetRootForTexture();
            ApplyTexture(meshRenderer, renderTexture);

            status = $"{renderTexture.width}x{renderTexture.height} texture on '{mesh.name}'";
            return true;
        }

        /// <summary>
        /// Unity bakes world-space sizing — fixed width, content height, pivot transform — into
        /// inline styles on the document root, and those survive the switch to the texture path.
        /// Left alone they lay the UI out in a corner of the texture, and picking then resolves to
        /// the root almost everywhere, which would defeat the UV mapping entirely.
        /// </summary>
        private void ResetRootForTexture()
        {
            var root = uiDocument != null ? uiDocument.rootVisualElement : null;
            if (root == null) return;

            root.style.position = Position.Absolute;
            root.style.left = 0;
            root.style.top = 0;
            root.style.right = 0;
            root.style.bottom = 0;
            root.style.width = StyleKeyword.Null;
            root.style.height = StyleKeyword.Null;
            root.style.scale = StyleKeyword.Null;
            root.style.translate = StyleKeyword.Null;
            root.style.transformOrigin = StyleKeyword.Null;
            root.style.rotate = StyleKeyword.Null;
        }

        /// <summary>
        /// Probes the shared material rather than the instance: reading <c>.material</c>
        /// instantiates a copy, and this is asked every frame while waiting on JS.
        /// </summary>
        private static bool HasTextureSlot(MeshRenderer meshRenderer)
        {
            var template = meshRenderer.sharedMaterial;
            return template != null
                && (template.HasProperty(BaseMapId) || template.HasProperty(MainTexId) || template.HasProperty(UnlitColorMapId));
        }

        /// <summary>
        /// True only when every texture slot the material has is already pointing at
        /// <paramref name="texture"/>. Compared against the live texture rather than a remembered
        /// one so that a destroyed texture — which a material reports as null, not as a stale
        /// reference — fails the check just as a swapped one does.
        /// </summary>
        private static bool HoldsTexture(Material material, Texture texture)
        {
            if (material == null || texture == null) return false;
            if (material.HasProperty(BaseMapId) && material.GetTexture(BaseMapId) != texture) return false;
            if (material.HasProperty(MainTexId) && material.GetTexture(MainTexId) != texture) return false;
            if (material.HasProperty(UnlitColorMapId) && material.GetTexture(UnlitColorMapId) != texture) return false;
            return true;
        }

        /// <summary>
        /// Assign the texture to whichever slot the material actually has — URP uses _BaseMap, the
        /// built-in unlit shaders use _MainTex, and <c>material.mainTexture</c> throws on a shader
        /// that has neither.
        /// </summary>
        private void ApplyTexture(MeshRenderer meshRenderer, Texture texture)
        {
            // The instance, not the shared asset: this texture belongs to one panel. Reading
            // `.material` also writes it back to sharedMaterial, so this instantiates once and
            // only repeats if something later swaps the material out.
            if (meshMaterial == null || meshRenderer.sharedMaterial != meshMaterial)
                meshMaterial = meshRenderer.material;

            Bind(BaseMapId);
            Bind(MainTexId);
            Bind(UnlitColorMapId);

            void Bind(int id)
            {
                if (!meshMaterial.HasProperty(id)) return;
                meshMaterial.SetTexture(id, texture);

                // Hit-testing reads raw UVs, so any tiling or offset on the material would shift
                // what is drawn without shifting where clicks land. Force them to identity and the
                // two cannot disagree.
                meshMaterial.SetTextureScale(id, Vector2.one);
                meshMaterial.SetTextureOffset(id, Vector2.zero);
            }
        }

        /// <summary>
        /// Give the panel a collider the pointer ray can hit, on this GameObject so it inherits the
        /// Menu layer that the laser and the pointer module both scan.
        /// </summary>
        private bool BuildMeshCollider(Mesh source)
        {
            if (colliderSource == source && meshInputCollider != null) return true;

            var built = PanelColliderMesh.BuildTwoSided(source);
            if (built == null)
            {
                if (rejectedMesh != source)
                {
                    rejectedMesh = source;
                    Debug.LogWarning($"{LogPrefix} mesh '{source.name}' cannot be used as a pointer target — it needs " +
                                     "UVs, and Read/Write enabled if it is an imported asset.");
                }
                return false;
            }

            if (colliderMesh != null) Destroy(colliderMesh);
            colliderMesh = built;
            colliderSource = source;
            rejectedMesh = null;

            if (meshInputCollider == null)
            {
                // Always a collider of our own, never one already on the object: adopting that
                // would overwrite its mesh and convex flag, and teardown would then destroy the
                // mesh it is still pointing at.
                meshInputCollider = gameObject.AddComponent<MeshCollider>();
                createdMeshCollider = true;
            }

            // Set before the mesh, or it does not apply to this cook. Mesh cleaning is off on
            // purpose: it removes duplicate triangles, and the back-facing half of this mesh is
            // exactly what makes the surface hittable from where the viewer stands.
            meshInputCollider.cookingOptions = MeshColliderCookingOptions.CookForFasterSimulation
                                             | MeshColliderCookingOptions.UseFastMidphase;
            meshInputCollider.convex = false;
            meshInputCollider.sharedMesh = colliderMesh;

            return true;
        }

        /// <summary>
        /// Put the panel back the way it was and release what mesh mode created.
        /// </summary>
        private void TearDownMeshInput()
        {
            if (meshBindRoutine != null)
            {
                StopCoroutine(meshBindRoutine);
                meshBindRoutine = null;
            }

            // Through the cached settings rather than uiDocument, because teardown also runs on
            // paths where the document has already gone. Clearing goes through the 3D overload:
            // the 2D one has no null check and installs a wrapper that throws on the next event.
            if (boundSettings != null)
            {
                boundSettings.SetScreenToPanelSpaceFunction3D(null);
                boundSettings.targetTexture = null;
                if (!screenSpace)
                    boundSettings.renderMode = PanelRenderMode.WorldSpace;
                boundSettings = null;
            }

            if (createdMeshCollider && meshInputCollider != null)
                Destroy(meshInputCollider);

            meshInputCollider = null;
            createdMeshCollider = false;

            if (colliderMesh != null)
            {
                Destroy(colliderMesh);
                colliderMesh = null;
            }

            colliderSource = null;
            rejectedMesh = null;
            meshBound = false;

            // The material instance belongs to the MeshRenderer, which is not ours to strip — it
            // outlives this component when only the panel is removed. Unity reclaims it with the
            // renderer.
            meshMaterial = null;
        }

        // Flags to track what we created
        private bool createdMeshRenderer = false;
        private bool createdMeshFilter = false;
        private bool createdUIDocument = false;

        public static bool IsScreenSpaceActive = false;
        
        // Static tracking of screenSpace panels
        private static readonly HashSet<BSUIPanel> screenSpacePanels = new HashSet<BSUIPanel>();
        private static readonly object screenSpaceLock = new object();
        
        /// <summary>
        /// Register or unregister a panel as screenSpace and update the global flag
        /// </summary>
        private static void UpdateScreenSpaceTracking(BSUIPanel panel, bool isScreenSpace)
        {
            lock (screenSpaceLock)
            {
                if (isScreenSpace)
                {
                    screenSpacePanels.Add(panel);
                }
                else
                {
                    screenSpacePanels.Remove(panel);
                }
                
                // Update the global flag based on whether any panels are screenSpace
                bool wasActive = IsScreenSpaceActive;
                IsScreenSpaceActive = screenSpacePanels.Count > 0;
                
                if (wasActive != IsScreenSpaceActive)
                {
                    LogVerbose($"IsScreenSpaceActive changed to: {IsScreenSpaceActive} (Active panels: {screenSpacePanels.Count})");
                }
            }
        }
        
        /// <summary>
        /// Remove a panel from screenSpace tracking when it's destroyed
        /// </summary>
        private static void RemovePanelFromTracking(BSUIPanel panel)
        {
            lock (screenSpaceLock)
            {
                if (screenSpacePanels.Remove(panel))
                {
                    IsScreenSpaceActive = screenSpacePanels.Count > 0;
                }
            }
        }

        internal override void DestroyStuff()
        {
            // Remove from screenSpace tracking
            RemovePanelFromTracking(this);

            // Unregister haptic and sound event handlers
            if (uiDocument != null && uiDocument.rootVisualElement != null)
            {
                uiDocument.rootVisualElement.Query<VisualElement>().ForEach(element =>
                {
                    UnregisterElementEvents(element);
                });
            }

            // Unregister this panel instance
            if (uiElementBridge != null)
            {
                var registrationId = GetFormattedPanelId();
                UIElementBridge.UnregisterPanelInstance(registrationId);
                Destroy(uiElementBridge);
                uiElementBridge = null;
            }

            TearDownMeshInput();

            // Clean up render texture
            if (renderTexture != null)
            {
                renderTexture.Release();
                Destroy(renderTexture);
                renderTexture = null;
            }

            // Destroy UIDocument if we created it
            if (createdUIDocument && uiDocument != null)
            {
                Destroy(uiDocument);
                var addPanelStiff = gameObject.GetComponent<AddPanelStuff>();
                if(addPanelStiff)
                {
                    Destroy(addPanelStiff);
                }
                var panelRaycaster = gameObject.GetComponent<PanelRaycaster>();
                if(panelRaycaster)
                {
                    Destroy(panelRaycaster);
                }
                var panelEventHandler = gameObject.GetComponent<PanelEventHandler>();
                if(panelEventHandler)
                {
                    Destroy(panelEventHandler);
                }
                uiDocument = null;
            }

            // Destroy mesh components if we created them
                if (createdMeshRenderer)
                {
                    var renderer = gameObject.GetComponent<MeshRenderer>();
                    if (renderer != null)
                    {
                        Destroy(renderer);
                    }
                }

            if (createdMeshFilter)
            {
                var filter = gameObject.GetComponent<MeshFilter>();
                if (filter != null)
                {
                    Destroy(filter);
                }
            }

            // Reset flags
            createdUIDocument = false;
            createdMeshRenderer = false;
            createdMeshFilter = false;

            
            UpdateScreenSpaceTracking(this, screenSpace);
            scene.events.OnBanterUiPanelActiveChanged?.Invoke();
        }

        /// <summary>
        /// Ensures the panel is initialized before any operations
        /// </summary>
        /// <returns>True if initialization is successful or already initialized</returns>
        private bool EnsureInitialized()
        {
            if (uiDocument == null || uiElementBridge == null)
            {
                if (internalPanelId == -1)
                {
                    internalPanelId = nextPanelId++;
                }
                return InitializePanel();
            }
            return true;
        }

        internal override void StartStuff()
        {
            EnsureInitialized();
        }
        async void UpdateCallback(List<PropertyName> changedProperties)
        {
            // Ensure panel is initialized before processing updates
            if (!EnsureInitialized())
            {
                // If initialization fails, we can't process updates yet
                return;
            }

            if (changedProperties.Contains(PropertyName.resolution))
            {
                if (!screenSpace)
                {
                    if (renderTexture != null)
                    {
                        renderTexture.Release();
                        Destroy(renderTexture);
                    }
                    renderTexture = new RenderTexture((int)resolution.x, (int)resolution.y, 16, RenderTextureFormat.ARGB32);
                    renderTexture.Create();
                }

                if (uiDocument != null)
                {
                    if (!screenSpace)
                    {
                        uiDocument.panelSettings.targetTexture = renderTexture;
                    }
                    uiDocument.panelSettings.referenceResolution = new Vector2Int((int)resolution.x, (int)resolution.y);

                }

                gameObject.layer = LayerMask.NameToLayer("Menu");

                // The mesh's material is still holding the texture that was just destroyed, and the
                // panel has to be re-pinned to the new one, or mesh mode goes blank.
                if (UsesMeshInput)
                {
                    BeginMeshBinding();
                }
            }

            // Both properties decide mesh mode, since UsesMeshInput is meshInput && !screenSpace.
            // Turning it off restores the render mode but not the inline styles Unity baked onto
            // the document root for world space — those were stripped on the way in.
            if (changedProperties.Contains(PropertyName.meshInput) ||
                changedProperties.Contains(PropertyName.screenSpace))
            {
                if (UsesMeshInput) BeginMeshBinding();
                else TearDownMeshInput();
            }

            if (changedProperties.Contains(PropertyName.screenSpace))
            {
                var renderer = gameObject.GetComponent<MeshRenderer>();
                if (screenSpace)
                {
                    if (renderTexture != null)
                    {
                        renderTexture.Release();
                        Destroy(renderTexture);
                        if (renderer)
                        {
                            renderer.enabled = false;
                        }
                    }
                    if (uiDocument)
                    {
                        uiDocument.panelSettings.targetTexture = null;
                    }
                }
                else
                {
                    if (renderTexture == null)
                    {
                        renderTexture = new RenderTexture((int)resolution.x, (int)resolution.y, 16, RenderTextureFormat.ARGB32);
                        renderTexture.Create();
                        if (renderer)
                        {
                            renderer.enabled = true;
                            renderer.sharedMaterial.mainTexture = renderTexture;
                        }
                        if (uiDocument != null)
                        {
                            uiDocument.panelSettings.targetTexture = renderTexture;
                        }
                    }
                }
            }

            // Handle haptics property changes
            if (changedProperties.Contains(PropertyName.enableHaptics) ||
                changedProperties.Contains(PropertyName.clickHaptic) ||
                changedProperties.Contains(PropertyName.enterHaptic) ||
                changedProperties.Contains(PropertyName.exitHaptic))
            {
                if (enableHaptics)
                {
                    UpdateControllerDevices();
                }

                // Re-register event handlers if haptics state changed
                if (changedProperties.Contains(PropertyName.enableHaptics))
                {
                    if (uiDocument != null && uiDocument.rootVisualElement != null)
                    {
                        if (enableHaptics || enableSounds)
                        {
                            RegisterUIEventHandlers(uiDocument.rootVisualElement);
                        }
                        else if (!enableHaptics && !enableSounds)
                        {
                            // Unregister all if both are disabled
                            uiDocument.rootVisualElement.Query<VisualElement>().ForEach(element =>
                            {
                                UnregisterElementEvents(element);
                            });
                        }
                    }
                }
            }

            // Handle sound property changes
            if (changedProperties.Contains(PropertyName.enableSounds) ||
                changedProperties.Contains(PropertyName.clickSoundUrl) ||
                changedProperties.Contains(PropertyName.enterSoundUrl) ||
                changedProperties.Contains(PropertyName.exitSoundUrl))
            {
                if (enableSounds)
                {
                    // Ensure audio source exists
                    if (audioSource == null)
                    {
                        audioSource = gameObject.GetComponent<AudioSource>();
                        if (audioSource == null)
                        {
                            audioSource = gameObject.AddComponent<AudioSource>();
                            audioSource.spatialBlend = 0f;
                            audioSource.playOnAwake = false;
                        }
                    }

                    // Reload audio clips if URLs changed (from JS)
                    // Clear the existing clip and load from URL
                    if (changedProperties.Contains(PropertyName.clickSoundUrl))
                    {
                        if (!string.IsNullOrEmpty(clickSoundUrl))
                        {
                            clickSound = await Get.Audio(clickSoundUrl);
                            // StartCoroutine(LoadAudioClip(clickSoundUrl, clip => clickSound = clip));
                        }
                    }
                    if (changedProperties.Contains(PropertyName.enterSoundUrl))
                    {
                        if (!string.IsNullOrEmpty(enterSoundUrl))
                        {
                            enterSound = await Get.Audio(enterSoundUrl); // Clear inspector-assigned clip
                            // StartCoroutine(LoadAudioClip(enterSoundUrl, clip => enterSound = clip));
                        }
                    }
                    if (changedProperties.Contains(PropertyName.exitSoundUrl))
                    {
                        if (!string.IsNullOrEmpty(exitSoundUrl))
                        {
                            exitSound = await Get.Audio(exitSoundUrl); // Clear inspector-assigned clip
                            // StartCoroutine(LoadAudioClip(exitSoundUrl, clip => exitSound = clip));
                        }
                    }
                }

                // Re-register event handlers if sounds state changed
                if (changedProperties.Contains(PropertyName.enableSounds))
                {
                    if (uiDocument != null && uiDocument.rootVisualElement != null)
                    {
                        if (enableHaptics || enableSounds)
                        {
                            RegisterUIEventHandlers(uiDocument.rootVisualElement);
                        }
                        else if (!enableHaptics && !enableSounds)
                        {
                            // Unregister all if both are disabled
                            uiDocument.rootVisualElement.Query<VisualElement>().ForEach(element =>
                            {
                                UnregisterElementEvents(element);
                            });
                        }
                    }
                }
            }

            UpdateScreenSpaceTracking(this, screenSpace);
            scene.events.OnBanterUiPanelActiveChanged?.Invoke();
            SetLoadedIfNot();
        }

        internal override void UpdateStuff()
        {
            
        }
        // BANTER COMPILED CODE 
        public UnityEngine.Vector2 Resolution { get { return resolution; } set { resolution = value; UpdateCallback(new List<PropertyName> { PropertyName.resolution }); } }
        public System.Boolean ScreenSpace { get { return screenSpace; } set { screenSpace = value; UpdateCallback(new List<PropertyName> { PropertyName.screenSpace }); } }
        public System.Boolean MeshInput { get { return meshInput; } set { meshInput = value; UpdateCallback(new List<PropertyName> { PropertyName.meshInput }); } }
        public System.Boolean EnableHaptics { get { return enableHaptics; } set { enableHaptics = value; UpdateCallback(new List<PropertyName> { PropertyName.enableHaptics }); } }
        public UnityEngine.Vector2 ClickHaptic { get { return clickHaptic; } set { clickHaptic = value; UpdateCallback(new List<PropertyName> { PropertyName.clickHaptic }); } }
        public UnityEngine.Vector2 EnterHaptic { get { return enterHaptic; } set { enterHaptic = value; UpdateCallback(new List<PropertyName> { PropertyName.enterHaptic }); } }
        public UnityEngine.Vector2 ExitHaptic { get { return exitHaptic; } set { exitHaptic = value; UpdateCallback(new List<PropertyName> { PropertyName.exitHaptic }); } }
        public System.Boolean EnableSounds { get { return enableSounds; } set { enableSounds = value; UpdateCallback(new List<PropertyName> { PropertyName.enableSounds }); } }
        public System.String ClickSoundUrl { get { return clickSoundUrl; } set { clickSoundUrl = value; UpdateCallback(new List<PropertyName> { PropertyName.clickSoundUrl }); } }
        public System.String EnterSoundUrl { get { return enterSoundUrl; } set { enterSoundUrl = value; UpdateCallback(new List<PropertyName> { PropertyName.enterSoundUrl }); } }
        public System.String ExitSoundUrl { get { return exitSoundUrl; } set { exitSoundUrl = value; UpdateCallback(new List<PropertyName> { PropertyName.exitSoundUrl }); } }

        BSScene _scene;
        public BSScene scene
        {
            get
            {
                if (_scene == null)
                {
                    _scene = BSScene.Instance();
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
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.resolution, PropertyName.screenSpace, PropertyName.meshInput, PropertyName.enableHaptics, PropertyName.clickHaptic, PropertyName.enterHaptic, PropertyName.exitHaptic, PropertyName.enableSounds, PropertyName.clickSoundUrl, PropertyName.enterSoundUrl, PropertyName.exitSoundUrl, };
            UpdateCallback(changedProperties);
        }
        internal override string GetSignature()
        {
            return "UIPanel" +  PropertyName.resolution + resolution + PropertyName.screenSpace + screenSpace + PropertyName.meshInput + meshInput + PropertyName.enableHaptics + enableHaptics + PropertyName.clickHaptic + clickHaptic + PropertyName.enterHaptic + enterHaptic + PropertyName.exitHaptic + exitHaptic + PropertyName.enableSounds + enableSounds + PropertyName.clickSoundUrl + clickSoundUrl + PropertyName.enterSoundUrl + enterSoundUrl + PropertyName.exitSoundUrl + exitSoundUrl;
        }

        internal override void Init(List<object> constructorProperties = null)
        {
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(gameObject.GetInstanceID(), GetInstanceID(), ComponentType.UIPanel);


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
            BSScene.Instance().RegisterComponentOnMainThread(gameObject, this);
        }

        void OnDestroy()
        {
            scene.UnregisterComponentOnMainThread(gameObject, this);

            DestroyStuff();
        }

        void SetBackgroundColor(Vector4 color)
        {
            _SetBackgroundColor(color);
        }
        internal override object CallMethod(string methodName, List<object> parameters)
        {

            if (methodName == "SetBackgroundColor" && parameters.Count == 1 && parameters[0] is Vector4)
            {
                var color = (Vector4)parameters[0];
                SetBackgroundColor(color);
                return null;
            }
            else
            {
                return null;
            }
        }

        internal override void Deserialise(List<object> values)
        {
            List<PropertyName> changedProperties = new List<PropertyName>();
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] is BSVector2)
                {
                    var valresolution = (BSVector2)values[i];
                    if (valresolution.n == PropertyName.resolution)
                    {
                        resolution = new Vector2(valresolution.x, valresolution.y);
                        changedProperties.Add(PropertyName.resolution);
                    }
                }
                if (values[i] is BSBool)
                {
                    var valscreenSpace = (BSBool)values[i];
                    if (valscreenSpace.n == PropertyName.screenSpace)
                    {
                        screenSpace = valscreenSpace.x;
                        changedProperties.Add(PropertyName.screenSpace);
                    }
                }
                if (values[i] is BSBool)
                {
                    var valmeshInput = (BSBool)values[i];
                    if (valmeshInput.n == PropertyName.meshInput)
                    {
                        meshInput = valmeshInput.x;
                        changedProperties.Add(PropertyName.meshInput);
                    }
                }
                if (values[i] is BSBool)
                {
                    var valenableHaptics = (BSBool)values[i];
                    if (valenableHaptics.n == PropertyName.enableHaptics)
                    {
                        enableHaptics = valenableHaptics.x;
                        changedProperties.Add(PropertyName.enableHaptics);
                    }
                }
                if (values[i] is BSVector2)
                {
                    var valclickHaptic = (BSVector2)values[i];
                    if (valclickHaptic.n == PropertyName.clickHaptic)
                    {
                        clickHaptic = new Vector2(valclickHaptic.x, valclickHaptic.y);
                        changedProperties.Add(PropertyName.clickHaptic);
                    }
                }
                if (values[i] is BSVector2)
                {
                    var valenterHaptic = (BSVector2)values[i];
                    if (valenterHaptic.n == PropertyName.enterHaptic)
                    {
                        enterHaptic = new Vector2(valenterHaptic.x, valenterHaptic.y);
                        changedProperties.Add(PropertyName.enterHaptic);
                    }
                }
                if (values[i] is BSVector2)
                {
                    var valexitHaptic = (BSVector2)values[i];
                    if (valexitHaptic.n == PropertyName.exitHaptic)
                    {
                        exitHaptic = new Vector2(valexitHaptic.x, valexitHaptic.y);
                        changedProperties.Add(PropertyName.exitHaptic);
                    }
                }
                if (values[i] is BSBool)
                {
                    var valenableSounds = (BSBool)values[i];
                    if (valenableSounds.n == PropertyName.enableSounds)
                    {
                        enableSounds = valenableSounds.x;
                        changedProperties.Add(PropertyName.enableSounds);
                    }
                }
                if (values[i] is BSString)
                {
                    var valclickSoundUrl = (BSString)values[i];
                    if (valclickSoundUrl.n == PropertyName.clickSoundUrl)
                    {
                        clickSoundUrl = valclickSoundUrl.x;
                        changedProperties.Add(PropertyName.clickSoundUrl);
                    }
                }
                if (values[i] is BSString)
                {
                    var valenterSoundUrl = (BSString)values[i];
                    if (valenterSoundUrl.n == PropertyName.enterSoundUrl)
                    {
                        enterSoundUrl = valenterSoundUrl.x;
                        changedProperties.Add(PropertyName.enterSoundUrl);
                    }
                }
                if (values[i] is BSString)
                {
                    var valexitSoundUrl = (BSString)values[i];
                    if (valexitSoundUrl.n == PropertyName.exitSoundUrl)
                    {
                        exitSoundUrl = valexitSoundUrl.x;
                        changedProperties.Add(PropertyName.exitSoundUrl);
                    }
                }
            }
            if (values.Count > 0) { UpdateCallback(changedProperties); }
        }

        internal override void SyncProperties(bool force = false, Action callback = null)
        {
            var updates = new List<BSComponentPropertyUpdate>();
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.resolution,
                    type = PropertyType.Vector2,
                    value = resolution,
                    componentType = ComponentType.UIPanel,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.screenSpace,
                    type = PropertyType.Bool,
                    value = screenSpace,
                    componentType = ComponentType.UIPanel,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.meshInput,
                    type = PropertyType.Bool,
                    value = meshInput,
                    componentType = ComponentType.UIPanel,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.enableHaptics,
                    type = PropertyType.Bool,
                    value = enableHaptics,
                    componentType = ComponentType.UIPanel,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.clickHaptic,
                    type = PropertyType.Vector2,
                    value = clickHaptic,
                    componentType = ComponentType.UIPanel,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.enterHaptic,
                    type = PropertyType.Vector2,
                    value = enterHaptic,
                    componentType = ComponentType.UIPanel,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.exitHaptic,
                    type = PropertyType.Vector2,
                    value = exitHaptic,
                    componentType = ComponentType.UIPanel,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.enableSounds,
                    type = PropertyType.Bool,
                    value = enableSounds,
                    componentType = ComponentType.UIPanel,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.clickSoundUrl,
                    type = PropertyType.String,
                    value = clickSoundUrl,
                    componentType = ComponentType.UIPanel,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.enterSoundUrl,
                    type = PropertyType.String,
                    value = enterSoundUrl,
                    componentType = ComponentType.UIPanel,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.exitSoundUrl,
                    type = PropertyType.String,
                    value = exitSoundUrl,
                    componentType = ComponentType.UIPanel,
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