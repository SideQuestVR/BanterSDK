using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BS
{
    /// <summary>
    /// Resolves shader names coming from world script (BanterMaterial), asset bundles and
    /// glTF imports into shaders that actually render in the active pipeline.
    ///
    /// Worlds published before the URP migration ask for built-in pipeline shaders by name,
    /// "Standard" being by far the most common. Those names still resolve through
    /// Shader.Find, but the shaders behind them have no UniversalPipeline SubShader, so
    /// everything using them renders magenta. Creators cannot be asked to redeploy their
    /// scripts, so the names are remapped here instead.
    /// </summary>
    public static class BSShaderResolver
    {
        public const string DefaultShader = "Unlit/Diffuse";
        public const string UrpLit = "Universal Render Pipeline/Lit";
        public const string UrpSimpleLit = "Universal Render Pipeline/Simple Lit";
        public const string UrpUnlit = "Universal Render Pipeline/Unlit";

        /// <summary>
        /// Built-in pipeline shader names that render magenta under URP, and what to use
        /// in their place. "Standard" maps to URP/Lit to match Unity's own built-in to URP
        /// material upgrader; the cheaper legacy shaders map to Simple Lit.
        /// </summary>
        static readonly Dictionary<string, string> LegacyShaderMap = new Dictionary<string, string>
        {
            { "Standard", UrpLit },
            { "Standard (Specular setup)", UrpLit },
            { "Standard (Roughness setup)", UrpLit },
            { "Autodesk Interactive", UrpLit },

            { "Diffuse", UrpSimpleLit },
            { "Specular", UrpSimpleLit },
            { "Bumped Diffuse", UrpSimpleLit },
            { "Bumped Specular", UrpSimpleLit },
            { "VertexLit", UrpSimpleLit },
            { "Legacy Shaders/Diffuse", UrpSimpleLit },
            { "Legacy Shaders/Specular", UrpSimpleLit },
            { "Legacy Shaders/Bumped Diffuse", UrpSimpleLit },
            { "Legacy Shaders/Bumped Specular", UrpSimpleLit },
            { "Legacy Shaders/VertexLit", UrpSimpleLit },
            { "Mobile/Diffuse", UrpSimpleLit },
            { "Mobile/Bumped Diffuse", UrpSimpleLit },
            { "Mobile/VertexLit", UrpSimpleLit },
        };

        // Shader.Find is not cheap and these names repeat across every object in a world.
        static readonly Dictionary<string, Shader> cache = new Dictionary<string, Shader>();

        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary>
        /// Never returns null as long as URP is installed, so callers can construct a
        /// Material without a null-shader check.
        /// </summary>
        public static Shader Find(string shaderName, string fallbackName = DefaultShader)
        {
            if (string.IsNullOrEmpty(shaderName))
            {
                shaderName = fallbackName;
            }

            // Cached entries can be destroyed on scene teardown, so re-resolve if that happened.
            if (cache.TryGetValue(shaderName, out var cached) && cached != null)
            {
                return cached;
            }

            var shader = Resolve(shaderName, fallbackName);
            cache[shaderName] = shader;
            return shader;
        }

        public static void ClearCache()
        {
            cache.Clear();
        }

        static Shader Resolve(string shaderName, string fallbackName)
        {
            // Only remap while a scriptable pipeline is driving rendering. Under the
            // built-in pipeline the original names are the correct ones.
            if (GraphicsSettings.defaultRenderPipeline != null
                && LegacyShaderMap.TryGetValue(shaderName, out var replacement))
            {
                // Simple Lit is only present in a player build if something references
                // it, whereas the pipeline asset always references Lit, so fall through
                // to Lit rather than letting a stripped shader drop us back to magenta.
                var mapped = Shader.Find(replacement) ?? Shader.Find(UrpLit);
                if (mapped != null)
                {
                    Debug.Log($"[BSShaderResolver] '{shaderName}' is a built-in pipeline shader and cannot render under URP. Using '{replacement}' instead.");
                    return mapped;
                }
            }

            var found = Shader.Find(shaderName);
            if (found != null)
            {
                return found;
            }

            Debug.LogWarning($"[BSShaderResolver] Shader '{shaderName}' was not found. Falling back to '{fallbackName}'.");

            return Shader.Find(fallbackName) ?? Shader.Find(UrpUnlit) ?? Shader.Find(UrpLit);
        }

        /// <summary>
        /// Applies a colour across both naming conventions. Custom Banter shaders expose
        /// _Color, while URP's own shaders read _BaseColor and keep _Color only as an
        /// obsolete upgrade stub that the shader itself ignores, so setting just one of
        /// them leaves half the shaders untinted.
        /// </summary>
        public static void SetColor(Material material, Color color)
        {
            if (material == null)
            {
                return;
            }

            if (material.HasProperty(BaseColorId))
            {
                material.SetColor(BaseColorId, color);
            }
            if (material.HasProperty(ColorId))
            {
                material.SetColor(ColorId, color);
            }
        }
    }
}
