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
    public partial class BSMaterial : BSComponentBase
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
        // Holds a colour from SetColor on this renderer alone; null until the first one.
        MaterialPropertyBlock tint;
        // Counts requests for a main texture, so that of several still loading only the latest takes effect.
        int textureRequest;
        // asset_ ids this component follows, per texture slot, so a replaced asset is re-assigned;
        // released when the slot resolves again and on destroy.
        readonly Dictionary<int, Action> assetFollowers = new Dictionary<int, Action>();

        private static Dictionary<string, Material> materialCache = new Dictionary<string, Material>();
        // Every material the cache has made, which tells a shared material from a creator's own whatever this component's
        // properties say. It keeps one dropped from materialCache: objects can still be drawing it.
        static readonly HashSet<Material> cachedMaterials = new HashSet<Material>();
        // The main textures SetTexture is loading, so an object that asks for the same material waits for the same load.
        static readonly Dictionary<Material, Task<bool>> textureLoads = new Dictionary<Material, Task<bool>>();

        public static void ClearCache()
        {
            foreach (var material in cachedMaterials)
            {
                Destroy(material);
            }
            cachedMaterials.Clear();
            textureLoads.Clear();
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
            cachedMaterials.Add(material);
            created = true;
            return material;
        }
        internal override void StartStuff()
        {
            if (!valuesApplied)
            {
                // Placed in the Inspector, so nothing has applied the fields yet (see valuesApplied).
                // The exception is every value still at its default on a renderer that already has a
                // material: that is a JS handle on the creator's own material, from when Inspector
                // values never applied, and building one would swap it for a plain white
                // Unlit/Diffuse. It's left alone until a property is set, as before.
                var meshRenderer = GetComponent<MeshRenderer>();
                if (meshRenderer != null && meshRenderer.sharedMaterial != null && HasDefaultValues())
                {
                    _ = SetupMaterial();
                }
                else
                {
                    ReSetup();
                }
            }
        }

        bool HasDefaultValues() =>
            shaderType == ShaderType.Custom && shaderName == "Unlit/Diffuse" && string.IsNullOrEmpty(texture) &&
            color == new Vector4(1, 1, 1, 1) && side == MaterialSide.Front && !generateMipMaps &&
            string.IsNullOrEmpty(cacheBust) && string.IsNullOrEmpty(normalMap) && string.IsNullOrEmpty(roughnessMap) &&
            string.IsNullOrEmpty(aoMap) && textureScale == 1f && normalStrength == 1f;

        internal override void UpdateStuff()
        {

        }
        internal void UpdateCallback(List<PropertyName> changedProperties)
        {
            valuesApplied = true;
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
                    if (changedProperties.Contains(PropertyName.texture))
                    {
                        textureRequest++;
                    }
                    // The cached material is a pure function of the signature, so ANY property change
                    // re-resolves it and the renderer always points at the instance for its current
                    // properties. Previously only a shaderName change swapped materials and every
                    // other edit mutated the shared instance in place, which changed every object
                    // that still had the old signature.
                    var material = GetCachedMaterial(out var created);
                    _renderer.sharedMaterial = material;
                    if (tint != null && changedProperties.Contains(PropertyName.color))
                    {
                        ApplyTint(new Color(color.x, color.y, color.z, color.w));
                    }
                    if (created)
                    {
                        await ApplyProperties(material);
                    }
                    else
                    {
                        // A cached material can hold a live asset_ texture (a browser's) that nothing
                        // follows any more: the component that created it was destroyed, e.g. on a page
                        // reload. Follow those references from this component too, or the material keeps
                        // a texture that the next resize destroys (a black screen).
                        await FollowAssetSlots(material);
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

        /// <summary>Resolve and follow only the slots that reference a registry asset (asset_...).</summary>
        async Task<bool> FollowAssetSlots(Material material)
        {
            var tasks = new List<Task>();
            void Follow(int propertyId, string reference, bool linear, bool isMain)
            {
                if (!string.IsNullOrEmpty(reference) && reference.StartsWith("asset_"))
                    tasks.Add(SetTextureSlot(material, propertyId, reference, linear, isMain));
            }
            Follow(NormalMapId, normalMap, true, false);
            Follow(RoughnessMapId, roughnessMap, true, false);
            Follow(AOMapId, aoMap, true, false);
            Follow(MainTexId, texture, false, true);
            await Task.WhenAll(tasks);
            return true;
        }

        /// <summary>
        /// Writes every property onto a freshly created material. Only the main texture is awaited
        /// (it gates the LOADED event, as before); the optional maps land whenever they arrive.
        /// A material whose main texture never came leaves the cache, or the next request for it
        /// would find it and load nothing. False in that case.
        /// </summary>
        async Task<bool> ApplyProperties(Material material, bool waitForTexture = false)
        {
            var signature = GetSignature();
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
            var loaded = await SetTextureSlot(material, MainTexId, texture, linear: false, isMain: true, waitForTexture);
            if (!loaded)
            {
                materialCache.Remove(signature);
            }
            return loaded;
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

        /// <summary>
        /// Colours this object. The cached material is shared with every BSMaterial of the same signature, so the
        /// colour goes in a block on this renderer. A creator's own material is written directly.
        /// </summary>
        public void SetColor(Color color)
        {
            if (_renderer == null) return;
            if (DrawsCachedMaterial())
            {
                ApplyTint(color);
            }
            else
            {
                BSShaderResolver.SetColor(_renderer.sharedMaterial, color);
            }
        }

        void ApplyTint(Color color)
        {
            tint ??= new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(tint);
            BSShaderResolver.SetColor(tint, color);
            _renderer.SetPropertyBlock(tint);
        }

        /// <summary>
        /// Changes this object's main texture. On a cached material that means a different signature, so this object
        /// moves to that material once the new texture has arrived, and stays as it is if it never does. Of several
        /// requests the latest counts. A creator's own material is written directly.
        /// </summary>
        public async Task SetTexture(string texture)
        {
            if (_renderer == null || _renderer.sharedMaterial == null) return;
            if (string.IsNullOrEmpty(texture) || !DrawsCachedMaterial())
            {
                await SetTextureSlot(_renderer.sharedMaterial, MainTexId, texture, linear: false, isMain: true);
                return;
            }
            var request = ++textureRequest;
            // ApplyProperties and FollowAssetSlots read the properties before their first await, so the field only needs
            // the new reference for this long. It goes back at once: it says what this object draws, and the load can fail.
            var drawn = this.texture;
            this.texture = texture;
            var signature = GetSignature();
            var material = GetCachedMaterial(out var created);
            if (created)
            {
                textureLoads[material] = ApplyProperties(material, waitForTexture: true);
            }
            if (!textureLoads.TryGetValue(material, out var load))
            {
                load = FollowAssetSlots(material);
            }
            this.texture = drawn;

            var loaded = await load;
            textureLoads.Remove(material);
            if (request != textureRequest || _renderer == null) return;
            if (!loaded)
            {
                // The load took this component's asset slots with it, and this object still draws the old material.
                await FollowAssetSlots(_renderer.sharedMaterial);
                return;
            }
            this.texture = texture;
            if (GetSignature() == signature)
            {
                _renderer.sharedMaterial = material;
            }
            else
            {
                // Another property changed while the texture loaded, so this object needs the material for all of them.
                await SetupMaterial(new List<PropertyName> { PropertyName.texture });
            }
        }

        bool DrawsCachedMaterial() => cachedMaterials.Contains(_renderer.sharedMaterial);

        /// <summary>
        /// Resolves a texture reference and assigns it to one slot of <paramref name="material"/>.
        /// Accepted forms, checked in this order: empty (leave the shader default), an
        /// <c>asset_</c> registry id, a registered <see cref="IBSTextureSource"/> scheme, a web address (see <see cref="PageUrl"/>).
        /// False when the reference gave nothing to show. A source's full-size file is only waited for, and only then
        /// counts, with <paramref name="waitForTexture"/> and no preview showing.
        /// </summary>
        async Task<bool> SetTextureSlot(Material material, int propertyId, string reference, bool linear, bool isMain, bool waitForTexture = false)
        {
            try
            {
                UnfollowAsset(propertyId);
                if (material == null || !material.HasProperty(propertyId) || string.IsNullOrEmpty(reference))
                {
                    return true;
                }

                if (reference.StartsWith("asset_"))
                {
                    // It's an asset reference - look it up in the asset registry, and keep following the
                    // id so an asset registered later or replaced (a browser's texture) is assigned too.
                    FollowAsset(material, propertyId, reference, isMain);
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
                    return true;
                }

                if (TryGetTextureSource(reference, out var source, out var path))
                {
                    var previewed = false;
                    var task = source.Resolve(path, linear, true, preview =>
                    {
                        previewed |= preview != null;
                        Assign(material, propertyId, preview, isMain);
                    });
                    // The preview (when the source has one) is already showing; the full-size file
                    // must not gate the LOADED event or the world load behind a 4K download.
                    var swap = SwapWhenDone(material, propertyId, task, isMain, reference);
                    return !waitForTexture || previewed || await swap;
                }

                // Original URL-based texture loading
                if (!Uri.IsWellFormedUriString(reference, UriKind.Absolute) || PageUrl.Refuse(reference, nameof(BSMaterial)))
                {
                    return false;
                }
                var downloaded = await Get.Texture(reference, linear, generateMipMaps);
                Assign(material, propertyId, downloaded, isMain);
                return downloaded != null;
            }
            catch (Exception e)
            {
                Debug.Log("Could not get texture: " + reference);
                Debug.LogError(e);
                return false;
            }
        }

        async Task<bool> SwapWhenDone(Material material, int propertyId, Task<Texture2D> task, bool isMain, string reference)
        {
            try
            {
                var final = await task;
                Assign(material, propertyId, final, isMain);
                return final != null;
            }
            catch (Exception e)
            {
                Debug.Log("Could not get texture: " + reference);
                Debug.LogError(e);
                return false;
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

        void FollowAsset(Material material, int propertyId, string assetId, bool isMain)
        {
            var registry = BSAssetRegistry.Instance;
            Action<string> onChanged = id =>
            {
                if (id != assetId) return;
                var tex = registry.GetAsset<Texture2D>(id);
                if (tex != null) Assign(material, propertyId, tex, isMain);
            };
            Action<string, AssetType> onRegistered = (id, type) => onChanged(id);
            registry.OnAssetUpdated += onChanged;
            registry.OnAssetRegistered += onRegistered;
            // Holds the asset while this slot shows it, so it isn't evicted out from under the material.
            var owner = cid + ":" + propertyId;
            registry.AddReference(assetId, owner);
            assetFollowers[propertyId] = () =>
            {
                registry.OnAssetUpdated -= onChanged;
                registry.OnAssetRegistered -= onRegistered;
                registry.RemoveReference(assetId, owner);
            };
        }

        void UnfollowAsset(int propertyId)
        {
            if (assetFollowers.TryGetValue(propertyId, out var unfollow))
            {
                unfollow();
                assetFollowers.Remove(propertyId);
            }
        }

        internal override void DestroyStuff()
        {
            foreach (var unfollow in assetFollowers.Values) unfollow();
            assetFollowers.Clear();
            if (_renderer != null)
            {
                Destroy(_renderer);
            }
        }
    }
}