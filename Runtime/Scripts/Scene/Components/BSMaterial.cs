using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

namespace BS
{
    public enum ShaderType
    {
        Mobile,
        Custom
    }
    public enum MaterialSide
    {
        Front,
        Back,
        Double
    }
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BSObjectId))]
    [WatchComponent]
    public class BSMaterial : BSComponentBase
    {
        public ShaderType shaderType = ShaderType.Custom;
        MeshRenderer _renderer;
        [Tooltip("The name of the shader to use for this material.")]
        [See(initial = "\"Unlit/Diffuse\"")][SerializeField] internal string shaderName = "Unlit/Diffuse";

        [Tooltip("The texture to apply to the material. Provide a valid URL, asset reference or a registered scheme reference such as cc0:{slug}/basecolor/{size}.")]
        [See(initial = "", isAssetReference = true)][SerializeField] internal string texture = "";// "https://cdn.glitch.global/7bdd46d4-73c4-47a1-b156-10440ceb99fb/GridBox_Default.png?v=1708022523716";

        [Tooltip("The color of the material in RGBA format.")]
        [See(initial = "1,1,1,1")][SerializeField] internal Vector4 color = new Vector4(1, 1, 1, 1);

        [Tooltip("Determines which side(s) of the material are rendered.")]
        [See(initial = "0")][SerializeField] internal MaterialSide side = MaterialSide.Front;

        [Tooltip("Enable to generate mipmaps for the texture (improves texture scaling).")]
        [See(initial = "false")][SerializeField] internal bool generateMipMaps = false;
        [See(initial = "")][SerializeField] internal string cacheBust = "";

        [Tooltip("Optional tangent-space normal map (URL, asset reference or scheme reference). Empty = off. Sampled linear; enables the _BS_NORMALMAP keyword on shaders that have it.")]
        [See(initial = "", isAssetReference = true)][SerializeField] internal string normalMap = "";

        [Tooltip("Optional roughness map, read from the GREEN channel (white = rough). Empty = off. May be the same texture as the AO map.")]
        [See(initial = "", isAssetReference = true)][SerializeField] internal string roughnessMap = "";

        [Tooltip("Optional ambient-occlusion map, read from the RED channel. Empty = off. May be the same texture as the roughness map.")]
        [See(initial = "", isAssetReference = true)][SerializeField] internal string aoMap = "";

        [Tooltip("UV tiling for UV-mapped shaders, tiles per metre for the triplanar shaders.")]
        [See(initial = "1")][SerializeField] internal float textureScale = 1f;

        [Tooltip("Normal map strength multiplier.")]
        [See(initial = "1")][SerializeField] internal float normalStrength = 1f;

        static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        static readonly int NormalMapId = Shader.PropertyToID("_NormalMap");
        static readonly int RoughnessMapId = Shader.PropertyToID("_RoughnessMap");
        static readonly int AOMapId = Shader.PropertyToID("_AOMap");
        static readonly int CullId = Shader.PropertyToID("_Cull");
        static readonly int NormalStrengthId = Shader.PropertyToID("_NormalStrength");
        static readonly int TriplanarScaleId = Shader.PropertyToID("_TriplanarScale");
        const string KeywordNormalMap = "_BS_NORMALMAP";
        const string KeywordMaskMaps = "_BS_MASKMAPS";

        Texture2D defaultTexture;
        Texture2D mainTex;

        bool UpdateCallbackRan = false;

        private static Dictionary<string, Material> materialCache = new Dictionary<string, Material>();

        public static void ClearCache()
        {
            foreach (var mat in materialCache)
            {
                Destroy(mat.Value);
            }
            materialCache.Clear();
            BSShaderResolver.ClearCache();
        }

        // ------------------------------------------------------------------ texture sources

        static readonly Dictionary<string, IBSTextureSource> textureSources =
            new Dictionary<string, IBSTextureSource>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Register a resolver for <c>&lt;scheme&gt;:&lt;path&gt;</c> texture references (see <see cref="IBSTextureSource"/>).
        /// Re-registering a scheme replaces the previous source.
        /// </summary>
        public static void RegisterTextureSource(IBSTextureSource source)
        {
            if (source == null || string.IsNullOrEmpty(source.Scheme)) return;
            textureSources[source.Scheme] = source;
        }

        public static bool UnregisterTextureSource(string scheme)
        {
            return !string.IsNullOrEmpty(scheme) && textureSources.Remove(scheme);
        }

        /// <summary>
        /// Splits <paramref name="reference"/> at its first colon and looks the scheme up. Checked
        /// BEFORE the URL path on purpose: "cc0:bark/basecolor/1024" is a well-formed absolute URI as
        /// far as <see cref="Uri.IsWellFormedUriString"/> is concerned, and would otherwise be handed
        /// to UnityWebRequest.
        /// </summary>
        internal static bool TryGetTextureSource(string reference, out IBSTextureSource source, out string path)
        {
            source = null;
            path = null;
            if (string.IsNullOrEmpty(reference)) return false;
            var colon = reference.IndexOf(':');
            if (colon <= 0 || colon == reference.Length - 1) return false;
            if (!textureSources.TryGetValue(reference.Substring(0, colon), out source)) return false;
            path = reference.Substring(colon + 1);
            return true;
        }

        // ------------------------------------------------------------------ material cache

        /// <summary>
        /// The material for this component's current property set. Materials are shared scene-wide:
        /// every BSMaterial with the same signature (every [See] property) draws with the same
        /// instance, which is why nothing per-object is ever written to one after creation.
        /// </summary>
        private Material GetCachedMaterial(out bool created)
        {
            var signature = GetSignature();
            if (materialCache.TryGetValue(signature, out var cached) && cached != null)
            {
                created = false;
                return cached;
            }
            var material = new Material(BSShaderResolver.Find(shaderType == ShaderType.Custom ? shaderName : BSShaderResolver.DefaultShader));
            materialCache[signature] = material;
            created = true;
            return material;
        }
        internal override void StartStuff()
        {
            if(!UpdateCallbackRan)
            {
                _ = SetupMaterial();
            }
        }

        internal override void UpdateStuff()
        {

        }
        internal void UpdateCallback(List<PropertyName> changedProperties)
        {
            UpdateCallbackRan = true;
            _ = SetupMaterial(changedProperties);
        }
        async Task SetupMaterial(List<PropertyName> changedProperties = null)
        {
            try
            {

                if (defaultTexture == null)
                {
                    defaultTexture = Resources.Load<Texture2D>("Images/GridBox_Default");
                }
                _renderer = GetComponent<MeshRenderer>();
                if (_renderer == null)
                {
                    _renderer = gameObject.AddComponent<MeshRenderer>();
                }
                if (changedProperties != null && changedProperties.Count > 0)
                {
                    // The cached material is a pure function of the signature, so ANY property change
                    // re-resolves it and the renderer always points at the instance for its current
                    // properties. Previously only a shaderName change swapped materials and every
                    // other edit mutated the shared instance in place, which changed every object
                    // that still had the old signature.
                    var material = GetCachedMaterial(out var created);
                    _renderer.sharedMaterial = material;
                    if (created)
                    {
                        await ApplyProperties(material);
                    }
                    if (changedProperties.Contains(PropertyName.texture) && !string.IsNullOrEmpty(texture))
                    {
                        scene.link.Send(APICommands.EVENT + APICommands.LOADED + MessageDelimiters.PRIMARY + cid);
                    }
                }
            }
            catch { }
            SetLoadedIfNot();
        }

        /// <summary>
        /// Writes every property onto a freshly created material. Only the main texture is awaited
        /// (it gates the LOADED event, as before); the optional maps land whenever they arrive.
        /// </summary>
        async Task ApplyProperties(Material material)
        {
            BSShaderResolver.SetColor(material, new Color(color.x, color.y, color.z, color.w));
            if (material.HasProperty(CullId))
            {
                material.SetFloat(CullId, 2 - (int)side);
            }
            if (material.HasProperty(MainTexId))
            {
                material.SetTextureScale(MainTexId, new Vector2(textureScale, textureScale));
            }
            if (material.HasProperty(TriplanarScaleId))
            {
                material.SetFloat(TriplanarScaleId, textureScale);
            }
            if (material.HasProperty(NormalStrengthId))
            {
                material.SetFloat(NormalStrengthId, normalStrength);
            }
            SetKeyword(material, KeywordNormalMap, !string.IsNullOrEmpty(normalMap));
            SetKeyword(material, KeywordMaskMaps, !string.IsNullOrEmpty(roughnessMap) || !string.IsNullOrEmpty(aoMap));

            _ = SetTextureSlot(material, NormalMapId, normalMap, linear: true, isMain: false);
            _ = SetTextureSlot(material, RoughnessMapId, roughnessMap, linear: true, isMain: false);
            _ = SetTextureSlot(material, AOMapId, aoMap, linear: true, isMain: false);
            await SetTextureSlot(material, MainTexId, texture, linear: false, isMain: true);
        }

        static void SetKeyword(Material material, string keyword, bool enabled)
        {
            // A local keyword the shader does not declare (any custom shader) is simply not there
            // to set. Looked up through the keyword space, not the LocalKeyword constructor: the
            // constructor logs an error for an unknown name, and every material on a shader other
            // than the four diffuse ones would trip it twice.
            var local = material.shader.keywordSpace.FindKeyword(keyword);
            if (!local.isValid) return;
            material.SetKeyword(local, enabled);
        }

        public void SetColor(Color color)
        {
            BSShaderResolver.SetColor(_renderer.sharedMaterial, color);
        }

        public async Task SetTexture(string texture)
        {
            if (_renderer == null || _renderer.sharedMaterial == null) return;
            await SetTextureSlot(_renderer.sharedMaterial, MainTexId, texture, linear: false, isMain: true);
        }

        /// <summary>
        /// Resolves a texture reference and assigns it to one slot of <paramref name="material"/>.
        /// Accepted forms, checked in this order: empty (leave the shader default), an
        /// <c>asset_</c> registry id, a registered <see cref="IBSTextureSource"/> scheme, an absolute URL.
        /// </summary>
        async Task SetTextureSlot(Material material, int propertyId, string reference, bool linear, bool isMain)
        {
            try
            {
                if (material == null || !material.HasProperty(propertyId) || string.IsNullOrEmpty(reference))
                {
                    return;
                }

                if (reference.StartsWith("asset_"))
                {
                    // It's an asset reference - look it up in the asset registry
                    var asset = BSAssetRegistry.Instance.GetAsset<Texture2D>(reference);
                    if (asset != null)
                    {
                        Debug.Log($"Using texture asset from registry: {reference}");
                        Assign(material, propertyId, asset, isMain);
                    }
                    else
                    {
                        Debug.LogWarning($"Texture asset not found in registry: {reference}");
                    }
                    return;
                }

                if (TryGetTextureSource(reference, out var source, out var path))
                {
                    var task = source.Resolve(path, linear, true, preview => Assign(material, propertyId, preview, isMain));
                    // The preview (when the source has one) is already showing; the full-size file
                    // must not gate the LOADED event or the world load behind a 4K download.
                    _ = SwapWhenDone(material, propertyId, task, isMain, reference);
                    return;
                }

                // Original URL-based texture loading
                if (!Uri.IsWellFormedUriString(reference, UriKind.Absolute))
                {
                    return;
                }
                var downloaded = await Get.Texture(reference, linear, generateMipMaps);
                Assign(material, propertyId, downloaded, isMain);
            }
            catch (Exception e)
            {
                Debug.Log("Could not get texture: " + reference);
                Debug.LogError(e);
            }
        }

        async Task SwapWhenDone(Material material, int propertyId, Task<Texture2D> task, bool isMain, string reference)
        {
            try
            {
                var final = await task;
                Assign(material, propertyId, final, isMain);
            }
            catch (Exception e)
            {
                Debug.Log("Could not get texture: " + reference);
                Debug.LogError(e);
            }
        }

        void Assign(Material material, int propertyId, Texture2D tex, bool isMain)
        {
            // The material is a shared, statically cached instance that can outlive this component,
            // so the null check is on IT, not on this - every object sharing it swaps together.
            if (material == null || tex == null) return;
            material.SetTexture(propertyId, tex);
            if (isMain)
            {
                mainTex = tex;
            }
        }

        internal override void DestroyStuff()
        {
            if (_renderer != null)
            {
                Destroy(_renderer);
            }
        }
        // BANTER COMPILED CODE 
        public System.String ShaderName { get { return shaderName; } set { shaderName = value; UpdateCallback(new List<PropertyName> { PropertyName.shaderName }); } }
        public System.String Texture { get { return texture; } set { texture = value; UpdateCallback(new List<PropertyName> { PropertyName.texture }); } }
        public UnityEngine.Vector4 Color { get { return color; } set { color = value; UpdateCallback(new List<PropertyName> { PropertyName.color }); } }
        public BS.MaterialSide Side { get { return side; } set { side = value; UpdateCallback(new List<PropertyName> { PropertyName.side }); } }
        public System.Boolean GenerateMipMaps { get { return generateMipMaps; } set { generateMipMaps = value; UpdateCallback(new List<PropertyName> { PropertyName.generateMipMaps }); } }
        public System.String CacheBust { get { return cacheBust; } set { cacheBust = value; UpdateCallback(new List<PropertyName> { PropertyName.cacheBust }); } }
        public System.String NormalMap { get { return normalMap; } set { normalMap = value; UpdateCallback(new List<PropertyName> { PropertyName.normalMap }); } }
        public System.String RoughnessMap { get { return roughnessMap; } set { roughnessMap = value; UpdateCallback(new List<PropertyName> { PropertyName.roughnessMap }); } }
        public System.String AoMap { get { return aoMap; } set { aoMap = value; UpdateCallback(new List<PropertyName> { PropertyName.aoMap }); } }
        public System.Single TextureScale { get { return textureScale; } set { textureScale = value; UpdateCallback(new List<PropertyName> { PropertyName.textureScale }); } }
        public System.Single NormalStrength { get { return normalStrength; } set { normalStrength = value; UpdateCallback(new List<PropertyName> { PropertyName.normalStrength }); } }

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
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.shaderName, PropertyName.texture, PropertyName.color, PropertyName.side, PropertyName.generateMipMaps, PropertyName.cacheBust, PropertyName.normalMap, PropertyName.roughnessMap, PropertyName.aoMap, PropertyName.textureScale, PropertyName.normalStrength, };
            UpdateCallback(changedProperties);
        }
        internal override string GetSignature()
        {
            return "Material" +  PropertyName.shaderName + shaderName + PropertyName.texture + texture + PropertyName.color + color + PropertyName.side + side + PropertyName.generateMipMaps + generateMipMaps + PropertyName.cacheBust + cacheBust + PropertyName.normalMap + normalMap + PropertyName.roughnessMap + roughnessMap + PropertyName.aoMap + aoMap + PropertyName.textureScale + textureScale + PropertyName.normalStrength + normalStrength;
        }

        internal override void Init(List<object> constructorProperties = null)
        {
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(gameObject.GetInstanceID(), GetInstanceID(), ComponentType.Material);


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

        internal override object CallMethod(string methodName, List<object> parameters)
        {
            return null;
        }

        internal override void Deserialise(List<object> values)
        {
            List<PropertyName> changedProperties = new List<PropertyName>();
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] is BSString)
                {
                    var valshaderName = (BSString)values[i];
                    if (valshaderName.n == PropertyName.shaderName)
                    {
                        shaderName = valshaderName.x;
                        changedProperties.Add(PropertyName.shaderName);
                    }
                }
                if (values[i] is BSString)
                {
                    var valtexture = (BSString)values[i];
                    if (valtexture.n == PropertyName.texture)
                    {
                        texture = valtexture.x;
                        changedProperties.Add(PropertyName.texture);
                    }
                }
                if (values[i] is BSVector4)
                {
                    var valcolor = (BSVector4)values[i];
                    if (valcolor.n == PropertyName.color)
                    {
                        color = new Vector4(valcolor.x, valcolor.y, valcolor.z, valcolor.w);
                        changedProperties.Add(PropertyName.color);
                    }
                }
                if (values[i] is BSInt)
                {
                    var valside = (BSInt)values[i];
                    if (valside.n == PropertyName.side)
                    {
                        side = (MaterialSide)valside.x;
                        changedProperties.Add(PropertyName.side);
                    }
                }
                if (values[i] is BSBool)
                {
                    var valgenerateMipMaps = (BSBool)values[i];
                    if (valgenerateMipMaps.n == PropertyName.generateMipMaps)
                    {
                        generateMipMaps = valgenerateMipMaps.x;
                        changedProperties.Add(PropertyName.generateMipMaps);
                    }
                }
                if (values[i] is BSString)
                {
                    var valcacheBust = (BSString)values[i];
                    if (valcacheBust.n == PropertyName.cacheBust)
                    {
                        cacheBust = valcacheBust.x;
                        changedProperties.Add(PropertyName.cacheBust);
                    }
                }
                if (values[i] is BSString)
                {
                    var valnormalMap = (BSString)values[i];
                    if (valnormalMap.n == PropertyName.normalMap)
                    {
                        normalMap = valnormalMap.x;
                        changedProperties.Add(PropertyName.normalMap);
                    }
                }
                if (values[i] is BSString)
                {
                    var valroughnessMap = (BSString)values[i];
                    if (valroughnessMap.n == PropertyName.roughnessMap)
                    {
                        roughnessMap = valroughnessMap.x;
                        changedProperties.Add(PropertyName.roughnessMap);
                    }
                }
                if (values[i] is BSString)
                {
                    var valaoMap = (BSString)values[i];
                    if (valaoMap.n == PropertyName.aoMap)
                    {
                        aoMap = valaoMap.x;
                        changedProperties.Add(PropertyName.aoMap);
                    }
                }
                if (values[i] is BSFloat)
                {
                    var valtextureScale = (BSFloat)values[i];
                    if (valtextureScale.n == PropertyName.textureScale)
                    {
                        textureScale = valtextureScale.x;
                        changedProperties.Add(PropertyName.textureScale);
                    }
                }
                if (values[i] is BSFloat)
                {
                    var valnormalStrength = (BSFloat)values[i];
                    if (valnormalStrength.n == PropertyName.normalStrength)
                    {
                        normalStrength = valnormalStrength.x;
                        changedProperties.Add(PropertyName.normalStrength);
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
                    name = PropertyName.shaderName,
                    type = PropertyType.String,
                    value = shaderName,
                    componentType = ComponentType.Material,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.texture,
                    type = PropertyType.String,
                    value = texture,
                    componentType = ComponentType.Material,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.color,
                    type = PropertyType.Vector4,
                    value = color,
                    componentType = ComponentType.Material,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.side,
                    type = PropertyType.Int,
                    value = side,
                    componentType = ComponentType.Material,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.generateMipMaps,
                    type = PropertyType.Bool,
                    value = generateMipMaps,
                    componentType = ComponentType.Material,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.cacheBust,
                    type = PropertyType.String,
                    value = cacheBust,
                    componentType = ComponentType.Material,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.normalMap,
                    type = PropertyType.String,
                    value = normalMap,
                    componentType = ComponentType.Material,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.roughnessMap,
                    type = PropertyType.String,
                    value = roughnessMap,
                    componentType = ComponentType.Material,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.aoMap,
                    type = PropertyType.String,
                    value = aoMap,
                    componentType = ComponentType.Material,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.textureScale,
                    type = PropertyType.Float,
                    value = textureScale,
                    componentType = ComponentType.Material,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.normalStrength,
                    type = PropertyType.Float,
                    value = normalStrength,
                    componentType = ComponentType.Material,
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