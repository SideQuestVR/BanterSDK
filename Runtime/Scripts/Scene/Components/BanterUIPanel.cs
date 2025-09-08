using System;
using System.Collections;
using System.Collections.Generic;
using Banter.UI.Bridge;
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
                if (panelId < 0 || panelId >= 20)
                {
                    Debug.LogWarning($"[BanterUIPanel] Invalid panel ID: {panelId}. Panel ID should be 0-19.");
                    return;
                }

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

                if (uiDocument == null)
                {
                    uiDocument = gameObject.AddComponent<UIDocument>();
                    // Use the panel settings directly instead of instantiating
                    uiDocument.panelSettings = panelSettings;
                    createdUIDocument = true;
                }

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
                        worldSpaceUIDocument._uiDocument = uiDocument;
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
                        if (uiDocument)
                        {
                            uiDocument.panelSettings.targetTexture = null;
                        }
                        if (worldSpaceUIDocument)
                        {
                            worldSpaceUIDocument.enabled = false;
                        }
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
                        if (worldSpaceUIDocument)
                        {
                            worldSpaceUIDocument.enabled = true;
                        }
                    }
                }
            }

            UpdateScreenSpaceTracking(this, screenSpace);
            scene.events.OnBanterUiPanelActiveChanged?.Invoke();
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