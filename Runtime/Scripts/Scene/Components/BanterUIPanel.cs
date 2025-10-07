using System;
using System.Collections;
using System.Collections.Generic;
using Banter.UI.Bridge;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UIElements;

namespace Banter.SDK
{
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BanterObjectId))]
    [WatchComponent]

    public class BanterUIPanel : BanterComponentBase
    {
        PanelSettings panelSettings;
        UIDocument uiDocument;
        public RenderTexture renderTexture;
        UIElementBridge uiElementBridge;

        [See(initial = "512,512")][SerializeField] internal Vector2 resolution = new Vector2(512,512);
        [See(initial = "false")][HideInInspector][SerializeField] internal bool screenSpace = false;
        
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
                    Debug.Log($"[BanterUIPanel] Using existing UIDocument with panel settings: {panelSettings.name}");
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
                    Debug.Log($"[BanterUIPanel] Created UIDocument with loaded panel settings: {panelSettingsName}");
                }
                
                Debug.Log($"[BanterUIPanel] Initialized with existing UIDocument and panel settings: {panelSettings.name}");

                // Add stylesheets
                uiDocument.rootVisualElement.styleSheets.Add(Resources.Load<StyleSheet>("UI/Slider"));
                uiDocument.rootVisualElement.styleSheets.Add(Resources.Load<StyleSheet>("UI/SwitchToggle"));
                uiDocument.rootVisualElement.styleSheets.Add(Resources.Load<StyleSheet>("UI/Button"));

                // Configure panel settings
                uiDocument.panelSettings.scaleMode = PanelScaleMode.ConstantPixelSize;

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

                Debug.Log($"[BanterUIPanel] Successfully initialized panel with ID: {registrationId}");

                // Auto-register UXML elements if the UIDocument has a visual tree asset
                if (uiDocument.visualTreeAsset != null && uiDocument.rootVisualElement != null)
                {
                    Debug.Log($"[BanterUIPanel] Auto-registering UXML elements from visual tree asset");
                    var elementMap = uiElementBridge.ProcessUXMLTree(uiDocument, "uxml");
                    Debug.Log($"[BanterUIPanel] Auto-registered {elementMap.Count} elements from UXML");
                }

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
                    Debug.Log($"[BanterUIPanel] IsScreenSpaceActive changed to: {IsScreenSpaceActive} (Active panels: {screenSpacePanels.Count})");
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

            UpdateScreenSpaceTracking(this, screenSpace);
            scene.events.OnBanterUiPanelActiveChanged?.Invoke();
            SetLoadedIfNot();
        }
        // BANTER COMPILED CODE 
        public UnityEngine.Vector2 Resolution { get { return resolution; } set { resolution = value; UpdateCallback(new List<PropertyName> { PropertyName.resolution }); } }
        public System.Boolean ScreenSpace { get { return screenSpace; } set { screenSpace = value; UpdateCallback(new List<PropertyName> { PropertyName.screenSpace }); } }

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
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.resolution, PropertyName.screenSpace, };
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
            scene.SetFromUnityProperties(updates, callback);
        }

        internal override void WatchProperties(PropertyName[] properties)
        {
        }
        // END BANTER COMPILED CODE 
    }
}