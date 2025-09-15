using System;
using System.Collections;
using System.Collections.Generic;
using Banter.UI.Bridge;
using Banter.UI.Core;
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
        RenderTexture renderTexture;
        UIElementBridge uiElementBridge;
        WorldSpaceUIDocument worldSpaceUIDocument;

        [See(initial = "-1")][HideInInspector][SerializeField] internal int panelId = -1;
        [See(initial = "512,512")][HideInInspector][SerializeField] internal Vector2 resolution = new Vector2(512,512);
        [See(initial = "false")][HideInInspector][SerializeField] internal bool screenSpace = false;


        [Method]
        public void _SetBackgroundColor(Vector4 color)
        {
            if (uiDocument != null && uiDocument.rootVisualElement != null)
            {
                uiDocument.rootVisualElement.style.backgroundColor = new StyleColor(new Color(color.x, color.y, color.z, color.w));
            }
        }

        /// <summary>
        /// Acquire a panel ID from the pool and set it on this panel
        /// Used by Visual Scripting nodes to automatically manage panel IDs
        /// </summary>
        /// <returns>The acquired panel ID, or -1 if no panels available</returns>
        public int AcquirePanelId()
        {
            // Release current panel if we have one
            if (acquiredPanelFromPool && lastAcquiredPanelId >= 0)
            {
                UIPanelPool.ReleasePanel(lastAcquiredPanelId);
            }

            // Acquire new panel ID
            int newPanelId = UIPanelPool.AcquirePanel();
            if (newPanelId >= 0)
            {
                panelId = newPanelId;
                acquiredPanelFromPool = true;
                lastAcquiredPanelId = newPanelId;
                Debug.Log($"[BanterUIPanel] Acquired panel ID {newPanelId} from pool");
                
                // Trigger update callback to initialize the panel
                UpdateCallback(new List<PropertyName> { PropertyName.panelId });
            }
            else
            {
                Debug.LogError($"[BanterUIPanel] No available panel IDs! Maximum of {UIPanelPool.MaxPanels} panels allowed.");
            }

            return newPanelId;
        }

        /// <summary>
        /// Get the current panel pool status for debugging
        /// </summary>
        /// <returns>Pool status information</returns>
        public static PoolStatus GetPoolStatus()
        {
            return UIPanelPool.GetPoolStatus();
        }

        /// <summary>
        /// Initialize panel using existing UIDocument and its panel settings
        /// This bypasses the pool system and uses the document's existing configuration
        /// </summary>
        /// <param name="document">UIDocument with existing panel settings</param>
        /// <returns>True if initialization was successful</returns>
        public bool InitializeWithExistingDocument(UIDocument document)
        {
            if (document?.panelSettings == null)
            {
                Debug.LogError("[BanterUIPanel] Cannot initialize with UIDocument - document or panel settings is null");
                return false;
            }

            try
            {
                // Use the existing panel settings without loading from resources
                panelSettings = document.panelSettings;
                uiDocument = document;
                
                // Set a special panel ID to indicate this doesn't use the pool
                panelId = -99; // Special value for UXML-based panels
                acquiredPanelFromPool = false; // We didn't acquire from pool
                lastAcquiredPanelId = -1; // No pool ID to track
                
                Debug.Log($"[BanterUIPanel] Initialized with existing UIDocument and panel settings: {panelSettings.name}");

                // Set up the UI document
                if (!createdUIDocument)
                {
                    createdUIDocument = false; // We didn't create it, so don't destroy it
                }

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

                // Register with a special identifier for UXML panels
                var registrationId = $"UXML_Panel_{gameObject.GetInstanceID()}";
                UIElementBridge.RegisterPanelInstance(registrationId, uiElementBridge);

                Debug.Log($"[BanterUIPanel] Successfully initialized UXML panel with registration ID: {registrationId}");

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

                var renderer = gameObject.GetComponent<Renderer>();
                if (renderer == null)
                {
                    var filter = gameObject.GetComponent<MeshFilter>();
                    if (filter == null)
                    {
                        filter = gameObject.AddComponent<MeshFilter>();
                        filter.mesh = CreateQuadMesh();
                        createdMeshFilter = true;
                    }
                    renderer = gameObject.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = new Material(Shader.Find("Unlit/Texture"));
                    createdMeshRenderer = true;
                    
                    var col = gameObject.AddComponent<BoxCollider>();
                    if (worldSpaceUIDocument == null)
                    {
                        worldSpaceUIDocument = gameObject.AddComponent<WorldSpaceUIDocument>();
                        worldSpaceUIDocument.AllowRaycastThroughBlockers = true;
                        worldSpaceUIDocument._uiDocument = uiDocument;
                        worldSpaceUIDocument._collider = col;
                        worldSpaceUIDocument.enabled = true;
                    }
                }

                if (uiDocument != null)
                {
                    uiDocument.panelSettings.targetTexture = renderTexture;
                }
                
                if (renderer != null)
                {
                    renderer.enabled = true;
                    renderer.sharedMaterial.mainTexture = renderTexture;
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
                
                if (worldSpaceUIDocument != null)
                {
                    worldSpaceUIDocument.enabled = false;
                }
            }
        }

        // Flags to track what we created
        private bool createdMeshRenderer = false;
        private bool createdMeshFilter = false;
        private bool createdUIDocument = false;
        private bool acquiredPanelFromPool = false;
        private int lastAcquiredPanelId = -1; // Track the last panel ID we acquired

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

        private Mesh CreateQuadMesh()
        {
            var mesh = new Mesh();
            mesh.name = "UI Panel Quad";
            
            // Vertices for a quad (from -0.5 to 0.5 on X and Y, Z = 0)
            mesh.vertices = new Vector3[]
            {
                new Vector3(-0.5f, -0.5f, 0f), // Bottom Left
                new Vector3(0.5f, -0.5f, 0f),  // Bottom Right  
                new Vector3(-0.5f, 0.5f, 0f),  // Top Left
                new Vector3(0.5f, 0.5f, 0f)    // Top Right
            };
            
            // UV coordinates (for texture mapping)
            mesh.uv = new Vector2[]
            {
                new Vector2(0f, 0f), // Bottom Left
                new Vector2(1f, 0f), // Bottom Right
                new Vector2(0f, 1f), // Top Left  
                new Vector2(1f, 1f)  // Top Right
            };
            
            // Triangles (two triangles make a quad)
            mesh.triangles = new int[]
            {
                0, 2, 1, // First triangle
                2, 3, 1  // Second triangle
            };
            
            // Normals (pointing towards camera)
            mesh.normals = new Vector3[]
            {
                Vector3.back, Vector3.back, Vector3.back, Vector3.back
            };
            
            return mesh;
        }

        internal override void DestroyStuff()
        {
            // Remove from screenSpace tracking
            RemovePanelFromTracking(this);

            // Unregister this panel instance
            if (uiElementBridge != null)
            {
                var registrationId = $"PanelSettings {panelId}";
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

            // Destroy WorldSpaceUIDocument if we created it
            if (worldSpaceUIDocument != null)
            {
                Destroy(worldSpaceUIDocument);
                worldSpaceUIDocument = null;
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

            // Release panel ID if we acquired it from the pool
            if (acquiredPanelFromPool && lastAcquiredPanelId >= 0)
            {
                UIPanelPool.ReleasePanel(lastAcquiredPanelId);
                acquiredPanelFromPool = false;
                lastAcquiredPanelId = -1;
                Debug.Log($"[BanterUIPanel] Released panel ID {lastAcquiredPanelId} back to pool");
            }

            // Reset flags
            createdUIDocument = false;
            createdMeshRenderer = false;
            createdMeshFilter = false;

            
            UpdateScreenSpaceTracking(this, screenSpace);
            scene.events.OnBanterUiPanelActiveChanged?.Invoke();
        }

        internal override void StartStuff()
        {
            // throw new NotImplementedException();
        }
        void UpdateCallback(List<PropertyName> changedProperties)
        {
            if (changedProperties.Contains(PropertyName.panelId) && uiElementBridge == null)
            {
                // Validate panel ID range
                if (!UIPanelPool.IsValidPanelId(panelId))
                {
                    Debug.LogWarning($"[BanterUIPanel] Invalid panel ID: {panelId}. Panel ID should be 0-{UIPanelPool.MaxPanels - 1}.");
                    return;
                }

                // If we had a previous panel ID that we acquired, release it first
                if (acquiredPanelFromPool && lastAcquiredPanelId >= 0 && lastAcquiredPanelId != panelId)
                {
                    UIPanelPool.ReleasePanel(lastAcquiredPanelId);
                    Debug.Log($"[BanterUIPanel] Released previous panel ID {lastAcquiredPanelId} when changing to {panelId}");
                }

                // Try to acquire the specific panel ID to prevent conflicts
                if (!UIPanelPool.AcquireSpecificPanel(panelId))
                {
                    Debug.LogError($"[BanterUIPanel] Panel ID {panelId} is already in use! This could cause conflicts between TypeScript and Visual Scripting setup.");
                    return;
                }
                
                acquiredPanelFromPool = true; // We acquired it through the pool system
                lastAcquiredPanelId = panelId; // Track this panel ID

                if (renderTexture != null)
                {
                    renderTexture.Release();
                    Destroy(renderTexture);
                }
                renderTexture = new RenderTexture((int)resolution.x, (int)resolution.y, 16, RenderTextureFormat.ARGB32);
                renderTexture.Create();

                // Load the specific panel settings from resources using the panel ID
                var panelSettingsName = $"PanelSettings {panelId}";
                if (panelSettings == null)
                {
                    panelSettings = Resources.Load<PanelSettings>($"UI/{panelSettingsName}");
                    if (panelSettings == null)
                    {
                        Debug.LogError($"[BanterUIPanel] Failed to load PanelSettings: {panelSettingsName}. Make sure the asset exists in Resources/UI/ folder.");
                        return;
                    }
                    Debug.Log($"[BanterUIPanel] Loaded PanelSettings: {panelSettingsName}");
                }
                uiDocument = gameObject.GetComponent<UIDocument>();
                if (uiDocument == null)
                {
                    uiDocument = gameObject.AddComponent<UIDocument>();
                    // Use the panel settings directly instead of instantiating
                    createdUIDocument = true;
                }

                uiDocument.panelSettings = panelSettings;
                uiDocument.rootVisualElement.styleSheets.Add(Resources.Load<StyleSheet>("UI/Slider"));
                uiDocument.rootVisualElement.styleSheets.Add(Resources.Load<StyleSheet>("UI/SwitchToggle"));
                uiDocument.rootVisualElement.styleSheets.Add(Resources.Load<StyleSheet>("UI/Button"));   

                uiDocument.panelSettings.scaleMode = PanelScaleMode.ConstantPixelSize;

                if (uiElementBridge == null)
                {
                    uiElementBridge = gameObject.AddComponent<UIElementBridge>();
                    uiElementBridge.banterLink = scene.link;
                    uiElementBridge.mainDocument = uiDocument;
                }

                // Register this panel instance using the PanelSettings name as ID
                UIElementBridge.RegisterPanelInstance(panelSettingsName, uiElementBridge);

                Debug.Log($"[BanterUIPanel] Successfully initialized panel with ID: {panelSettingsName}");
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
                var renderer = gameObject.GetComponent<Renderer>();
                if (renderer == null)
                {
                    var filter = gameObject.GetComponent<MeshFilter>();
                    if (filter == null)
                    {
                        filter = gameObject.AddComponent<MeshFilter>();
                        filter.mesh = CreateQuadMesh();
                        createdMeshFilter = true;
                    }
                    renderer = gameObject.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = new Material(Shader.Find("Unlit/Texture"));
                    createdMeshRenderer = true;
                    var col = gameObject.AddComponent<BoxCollider>();
                    if (worldSpaceUIDocument == null)
                    {
                        worldSpaceUIDocument = gameObject.AddComponent<WorldSpaceUIDocument>();
                        worldSpaceUIDocument.AllowRaycastThroughBlockers = true;
                        worldSpaceUIDocument.enabled = false;
                        Debug.Log("Panel Settings Initialized bef: " + (uiDocument == null));

                        worldSpaceUIDocument._uiDocument = uiDocument;
                        Debug.Log("Panel Settings Initialized bef aft: " + (uiDocument == null));
                        worldSpaceUIDocument._collider = col;
                    }
                }
                if (screenSpace)
                {
                    renderer.enabled = false;
                }
                else
                {
                    renderer.sharedMaterial.mainTexture = renderTexture;
                }
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
                    if (worldSpaceUIDocument)
                    {
                        worldSpaceUIDocument.enabled = false;
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
                    if (worldSpaceUIDocument)
                    {
                        worldSpaceUIDocument.enabled = true;
                    }
                }
            }

            UpdateScreenSpaceTracking(this, screenSpace);
            scene.events.OnBanterUiPanelActiveChanged?.Invoke();
            SetLoadedIfNot();
        }
        // BANTER COMPILED CODE 
        public System.Int32 PanelId { get { return panelId; } set { panelId = value; UpdateCallback(new List<PropertyName> { PropertyName.panelId }); } }
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
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.panelId, PropertyName.resolution, PropertyName.screenSpace, };
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
                if (values[i] is BanterInt)
                {
                    var valpanelId = (BanterInt)values[i];
                    if (valpanelId.n == PropertyName.panelId)
                    {
                        panelId = valpanelId.x;
                        changedProperties.Add(PropertyName.panelId);
                    }
                }
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
                    name = PropertyName.panelId,
                    type = PropertyType.Int,
                    value = panelId,
                    componentType = ComponentType.BanterUIPanel,
                    oid = oid,
                    cid = cid
                });
            }
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