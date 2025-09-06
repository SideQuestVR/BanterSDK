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
        [See(initial = "512,512")][HideInInspector][SerializeField] internal Vector2 resolution = new Vector2(512,512);
        
        // Flags to track what we created
        private bool createdMeshRenderer = false;
        private bool createdMeshFilter = false;
        private bool createdUIDocument = false;

        [Method]
        public void _SetDirty()
        {
            
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
            // Unregister this panel instance
            if (uiElementBridge != null)
            {
                var panelId = $"panel_{oid}_{cid}";
                UIElementBridge.UnregisterPanelInstance(panelId);
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
        }

        internal override void StartStuff()
        {
            // throw new NotImplementedException();
        }
        void UpdateCallback(List<PropertyName> changedProperties)
        {
            if (changedProperties.Contains(PropertyName.resolution))
            {
                if (renderTexture != null)
                {
                    renderTexture.Release();
                    Destroy(renderTexture);
                }
                renderTexture = new RenderTexture((int)resolution.x, (int)resolution.y, 16, RenderTextureFormat.ARGB32);
                renderTexture.Create();
                if (panelSettings == null)
                {
                    panelSettings = Resources.Load<PanelSettings>("TemplatePanelSettings");
                }
                if (uiDocument == null)
                {
                    uiDocument = gameObject.AddComponent<UIDocument>();
                    uiDocument.panelSettings = ScriptableObject.Instantiate(panelSettings);
                    createdUIDocument = true;
                }
                uiDocument.panelSettings.targetTexture = renderTexture;
                uiDocument.panelSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
                uiDocument.panelSettings.referenceResolution = new Vector2Int((int)resolution.x, (int)resolution.y);
                uiDocument.panelSettings.clearColor = true;
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
                }
                renderer.sharedMaterial.mainTexture = renderTexture;
                if (uiElementBridge == null)
                {
                    uiElementBridge = gameObject.AddComponent<UIElementBridge>();
                    uiElementBridge.banterLink = scene.link;
                    uiElementBridge.mainDocument = uiDocument;
                }
                gameObject.layer = LayerMask.NameToLayer("Menu");
                gameObject.AddComponent<BoxCollider>();
                // Register this panel instance with a unique ID
                var panelId = $"panel_{oid}_{cid}";
                UIElementBridge.RegisterPanelInstance(panelId, uiElementBridge);
            }
        }
        // BANTER COMPILED CODE 
        public UnityEngine.Vector2 Resolution { get { return resolution; } set { resolution = value; UpdateCallback(new List<PropertyName> { PropertyName.resolution }); } }

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
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.resolution, };
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

        void SetDirty()
        {
            _SetDirty();
        }
        internal override object CallMethod(string methodName, List<object> parameters)
        {

            if (methodName == "SetDirty" && parameters.Count == 0)
            {
                SetDirty();
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
            scene.SetFromUnityProperties(updates, callback);
        }

        internal override void WatchProperties(PropertyName[] properties)
        {
        }
        // END BANTER COMPILED CODE 
    }
}