using System;
using System.Collections;
using System.Collections.Generic;
using Banter.UI.Bridge;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.XR;

namespace Banter.SDK
{
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BanterObjectId))]
    [WatchComponent]

    public class BanterUIPanel : BanterComponentBase
    {
        private const string LogPrefix = "[BanterUIPanel]";

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
                        Debug.Log($"[BanterUIPanel] RenderTexture: {targetTex.name}, Size: {targetTex.width}x{targetTex.height}, Format: {targetTex.format}, IsCreated: {targetTex.IsCreated()}");
                    }
                    else
                    {
                        Debug.LogWarning($"[BanterUIPanel] uiDocument.panelSettings.targetTexture is null! ScreenSpace: {screenSpace}");
                    }
                    return targetTex;
                }

                Debug.LogWarning($"[BanterUIPanel] UIDocument or panelSettings is null. Returning private renderTexture field.");
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
                    panelSettings = document.panelSettings;
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
                    if (internalPanelId > 19)
                    {
                        Debug.LogWarning($"[BanterUIPanel] Internal panel ID {internalPanelId} exceeds maximum of 19. Using panel settings for ID 19.");

                    }
                    if (panelSettings == null)
                    {
                        Debug.LogError($"[BanterUIPanel] Failed to load PanelSettings: {panelSettingsName}. Make sure the asset exists in Resources/UI/ folder.");
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
                Debug.LogError($"[BanterUIPanel] Failed to initialize with existing document: {e.Message}");
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
        private void InitializeHapticsAndSounds()
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
                    StartCoroutine(LoadAudioClip(clickSoundUrl, clip => clickSound = clip));
                }
                if (enterSound == null && !string.IsNullOrEmpty(enterSoundUrl))
                {
                    StartCoroutine(LoadAudioClip(enterSoundUrl, clip => enterSound = clip));
                }
                if (exitSound == null && !string.IsNullOrEmpty(exitSoundUrl))
                {
                    StartCoroutine(LoadAudioClip(exitSoundUrl, clip => exitSound = clip));
                }
            }

            if ((enableHaptics || enableSounds) && uiDocument != null && uiDocument.rootVisualElement != null)
            {
                RegisterUIEventHandlers(uiDocument.rootVisualElement);
            }
        }

        /// <summary>
        /// Load audio clip from URL
        /// </summary>
        private IEnumerator LoadAudioClip(string url, Action<AudioClip> onLoaded)
        {
            if (string.IsNullOrEmpty(url))
            {
                yield break;
            }

            using (var www = UnityEngine.Networking.UnityWebRequestMultimedia.GetAudioClip(url, AudioType.UNKNOWN))
            {
                yield return www.SendWebRequest();

                if (www.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                {
                    var clip = UnityEngine.Networking.DownloadHandlerAudioClip.GetContent(www);
                    onLoaded?.Invoke(clip);
                    LogVerbose($"Loaded audio clip from {url}");
                }
                else
                {
                    Debug.LogWarning($"[BanterUIPanel] Failed to load audio clip from {url}: {www.error}");
                }
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

                createdMeshRenderer = true;

                if (uiDocument != null)
                {
                    uiDocument.panelSettings.targetTexture = renderTexture;
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

        // Flags to track what we created
        private bool createdMeshRenderer = false;
        private bool createdMeshFilter = false;
        private bool createdUIDocument = false;

        public static bool IsScreenSpaceActive = false;
        
        // Static tracking of screenSpace panels
        private static readonly HashSet<BanterUIPanel> screenSpacePanels = new HashSet<BanterUIPanel>();
        private static readonly object screenSpaceLock = new object();
        
        /// <summary>
        /// Register or unregister a panel as screenSpace and update the global flag
        /// </summary>
        private static void UpdateScreenSpaceTracking(BanterUIPanel panel, bool isScreenSpace)
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
        private static void RemovePanelFromTracking(BanterUIPanel panel)
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
        void UpdateCallback(List<PropertyName> changedProperties)
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
                            clickSound = null; // Clear inspector-assigned clip
                            StartCoroutine(LoadAudioClip(clickSoundUrl, clip => clickSound = clip));
                        }
                    }
                    if (changedProperties.Contains(PropertyName.enterSoundUrl))
                    {
                        if (!string.IsNullOrEmpty(enterSoundUrl))
                        {
                            enterSound = null; // Clear inspector-assigned clip
                            StartCoroutine(LoadAudioClip(enterSoundUrl, clip => enterSound = clip));
                        }
                    }
                    if (changedProperties.Contains(PropertyName.exitSoundUrl))
                    {
                        if (!string.IsNullOrEmpty(exitSoundUrl))
                        {
                            exitSound = null; // Clear inspector-assigned clip
                            StartCoroutine(LoadAudioClip(exitSoundUrl, clip => exitSound = clip));
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
        // BANTER COMPILED CODE 
        public UnityEngine.Vector2 Resolution { get { return resolution; } set { resolution = value; UpdateCallback(new List<PropertyName> { PropertyName.resolution }); } }
        public System.Boolean ScreenSpace { get { return screenSpace; } set { screenSpace = value; UpdateCallback(new List<PropertyName> { PropertyName.screenSpace }); } }
        public System.Boolean EnableHaptics { get { return enableHaptics; } set { enableHaptics = value; UpdateCallback(new List<PropertyName> { PropertyName.enableHaptics }); } }
        public UnityEngine.Vector2 ClickHaptic { get { return clickHaptic; } set { clickHaptic = value; UpdateCallback(new List<PropertyName> { PropertyName.clickHaptic }); } }
        public UnityEngine.Vector2 EnterHaptic { get { return enterHaptic; } set { enterHaptic = value; UpdateCallback(new List<PropertyName> { PropertyName.enterHaptic }); } }
        public UnityEngine.Vector2 ExitHaptic { get { return exitHaptic; } set { exitHaptic = value; UpdateCallback(new List<PropertyName> { PropertyName.exitHaptic }); } }
        public System.Boolean EnableSounds { get { return enableSounds; } set { enableSounds = value; UpdateCallback(new List<PropertyName> { PropertyName.enableSounds }); } }
        public System.String ClickSoundUrl { get { return clickSoundUrl; } set { clickSoundUrl = value; UpdateCallback(new List<PropertyName> { PropertyName.clickSoundUrl }); } }
        public System.String EnterSoundUrl { get { return enterSoundUrl; } set { enterSoundUrl = value; UpdateCallback(new List<PropertyName> { PropertyName.enterSoundUrl }); } }
        public System.String ExitSoundUrl { get { return exitSoundUrl; } set { exitSoundUrl = value; UpdateCallback(new List<PropertyName> { PropertyName.exitSoundUrl }); } }

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
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.resolution, PropertyName.screenSpace, PropertyName.enableHaptics, PropertyName.clickHaptic, PropertyName.enterHaptic, PropertyName.exitHaptic, PropertyName.enableSounds, PropertyName.clickSoundUrl, PropertyName.enterSoundUrl, PropertyName.exitSoundUrl, };
            UpdateCallback(changedProperties);
        }

        internal override void Init(List<object> constructorProperties = null)
        {
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(gameObject.GetInstanceID(), GetInstanceID(), ComponentType.BanterUIPanel);


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
                if (values[i] is BanterVector2)
                {
                    var valresolution = (BanterVector2)values[i];
                    if (valresolution.n == PropertyName.resolution)
                    {
                        resolution = new Vector2(valresolution.x, valresolution.y);
                        changedProperties.Add(PropertyName.resolution);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valscreenSpace = (BanterBool)values[i];
                    if (valscreenSpace.n == PropertyName.screenSpace)
                    {
                        screenSpace = valscreenSpace.x;
                        changedProperties.Add(PropertyName.screenSpace);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valenableHaptics = (BanterBool)values[i];
                    if (valenableHaptics.n == PropertyName.enableHaptics)
                    {
                        enableHaptics = valenableHaptics.x;
                        changedProperties.Add(PropertyName.enableHaptics);
                    }
                }
                if (values[i] is BanterVector2)
                {
                    var valclickHaptic = (BanterVector2)values[i];
                    if (valclickHaptic.n == PropertyName.clickHaptic)
                    {
                        clickHaptic = new Vector2(valclickHaptic.x, valclickHaptic.y);
                        changedProperties.Add(PropertyName.clickHaptic);
                    }
                }
                if (values[i] is BanterVector2)
                {
                    var valenterHaptic = (BanterVector2)values[i];
                    if (valenterHaptic.n == PropertyName.enterHaptic)
                    {
                        enterHaptic = new Vector2(valenterHaptic.x, valenterHaptic.y);
                        changedProperties.Add(PropertyName.enterHaptic);
                    }
                }
                if (values[i] is BanterVector2)
                {
                    var valexitHaptic = (BanterVector2)values[i];
                    if (valexitHaptic.n == PropertyName.exitHaptic)
                    {
                        exitHaptic = new Vector2(valexitHaptic.x, valexitHaptic.y);
                        changedProperties.Add(PropertyName.exitHaptic);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valenableSounds = (BanterBool)values[i];
                    if (valenableSounds.n == PropertyName.enableSounds)
                    {
                        enableSounds = valenableSounds.x;
                        changedProperties.Add(PropertyName.enableSounds);
                    }
                }
                if (values[i] is BanterString)
                {
                    var valclickSoundUrl = (BanterString)values[i];
                    if (valclickSoundUrl.n == PropertyName.clickSoundUrl)
                    {
                        clickSoundUrl = valclickSoundUrl.x;
                        changedProperties.Add(PropertyName.clickSoundUrl);
                    }
                }
                if (values[i] is BanterString)
                {
                    var valenterSoundUrl = (BanterString)values[i];
                    if (valenterSoundUrl.n == PropertyName.enterSoundUrl)
                    {
                        enterSoundUrl = valenterSoundUrl.x;
                        changedProperties.Add(PropertyName.enterSoundUrl);
                    }
                }
                if (values[i] is BanterString)
                {
                    var valexitSoundUrl = (BanterString)values[i];
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
            var updates = new List<BanterComponentPropertyUpdate>();
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.resolution,
                    type = PropertyType.Vector2,
                    value = resolution,
                    componentType = ComponentType.BanterUIPanel,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.screenSpace,
                    type = PropertyType.Bool,
                    value = screenSpace,
                    componentType = ComponentType.BanterUIPanel,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.enableHaptics,
                    type = PropertyType.Bool,
                    value = enableHaptics,
                    componentType = ComponentType.BanterUIPanel,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.clickHaptic,
                    type = PropertyType.Vector2,
                    value = clickHaptic,
                    componentType = ComponentType.BanterUIPanel,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.enterHaptic,
                    type = PropertyType.Vector2,
                    value = enterHaptic,
                    componentType = ComponentType.BanterUIPanel,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.exitHaptic,
                    type = PropertyType.Vector2,
                    value = exitHaptic,
                    componentType = ComponentType.BanterUIPanel,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.enableSounds,
                    type = PropertyType.Bool,
                    value = enableSounds,
                    componentType = ComponentType.BanterUIPanel,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.clickSoundUrl,
                    type = PropertyType.String,
                    value = clickSoundUrl,
                    componentType = ComponentType.BanterUIPanel,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.enterSoundUrl,
                    type = PropertyType.String,
                    value = enterSoundUrl,
                    componentType = ComponentType.BanterUIPanel,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.exitSoundUrl,
                    type = PropertyType.String,
                    value = exitSoundUrl,
                    componentType = ComponentType.BanterUIPanel,
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