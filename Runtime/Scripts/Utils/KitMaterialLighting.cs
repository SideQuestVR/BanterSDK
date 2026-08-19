using System.Collections.Generic;
using UnityEngine;

namespace BS
{
    /// <summary>
    /// Moves a freshly instantiated kit prefab's materials onto the SQ-lit
    /// "Unlit/Diffuse" family, so kit pieces receive the fake sun's shadow map and
    /// the baked AO volume exactly like the editor's primitives do.
    /// </summary>
    /// <remarks>
    /// Only plain URP surface shaders are converted (Lit / Simple Lit / Unlit /
    /// Baked Lit, plus a stray legacy Standard). The packs' ShaderGraph materials —
    /// foliage sway, water, portals, emissives — are deliberately left alone: a
    /// blanket swap would keep their colour but kill the effect that IS the
    /// material. Graph-driven pieces simply do not receive the fake lighting.
    ///
    /// One converted twin is built per SOURCE material asset and cached: the whole
    /// library shares a few hundred pack materials across ~9,500 prefabs, so this
    /// keeps material instances bounded and leaves batching intact. The twins are
    /// never destroyed — they are shared scene-wide, exactly like BSMaterial's
    /// signature cache, and outlive any one kit piece.
    ///
    /// Carried over: the main texture (_BaseMap or [MainTexture]) with its
    /// tiling/offset, the base colour (_BaseColor/_Color), the cull mode, and
    /// opaque-vs-transparent (URP _Surface picks the DiffuseTransparent twin).
    /// Dropped, by the target shader's design: normal/metallic/occlusion/emission
    /// maps — the diffuse family has no slots for them.
    /// </remarks>
    public static class KitMaterialLighting
    {
        static readonly Dictionary<Material, Material> s_converted = new Dictionary<Material, Material>();

        /// <summary>URP's stock surface shaders — the ones a diffuse swap can represent.</summary>
        static readonly HashSet<string> s_convertible = new HashSet<string>(System.StringComparer.Ordinal)
        {
            "Universal Render Pipeline/Lit",
            "Universal Render Pipeline/Simple Lit",
            "Universal Render Pipeline/Unlit",
            "Universal Render Pipeline/Baked Lit",
            "Standard",
        };

        static Shader s_opaque;
        static Shader s_transparent;
        static bool s_shadersMissingLogged;

        /// <summary>Convert every renderer under <paramref name="root"/> in place.</summary>
        public static void Convert(GameObject root)
        {
            if (root == null) return;

            if (s_opaque == null) s_opaque = Shader.Find("Unlit/Diffuse");
            if (s_transparent == null) s_transparent = Shader.Find("Unlit/DiffuseTransparent");
            if (s_opaque == null || s_transparent == null)
            {
                if (!s_shadersMissingLogged)
                {
                    s_shadersMissingLogged = true;
                    Debug.LogWarning("[KitMaterialLighting] Unlit/Diffuse shaders not found; kit assets keep their own materials.");
                }
                return;
            }

            // Renderer, not MeshRenderer: skinned pieces should receive lighting too,
            // even though the lighting registry only tracks static meshes as casters.
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                var materials = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    var twin = LitTwin(materials[i]);
                    if (twin != null && !ReferenceEquals(twin, materials[i]))
                    {
                        materials[i] = twin;
                        changed = true;
                    }
                }
                // Assigned to the INSTANTIATED renderer, never the prefab asset's —
                // the caller hands us the fresh Instantiate result.
                if (changed) renderer.sharedMaterials = materials;
            }
        }

        static Material LitTwin(Material source)
        {
            if (source == null || source.shader == null) return null;
            if (s_converted.TryGetValue(source, out var cached)) return cached;

            var shaderName = source.shader.name;
            Material twin;
            if (!s_convertible.Contains(shaderName))
            {
                // ShaderGraph or already-diffuse: keep as-is, and cache the decision
                // so the name lookup runs once per source material, not per piece.
                twin = source;
            }
            else
            {
                bool transparent = source.HasProperty("_Surface") && source.GetFloat("_Surface") > 0.5f;
                twin = new Material(transparent ? s_transparent : s_opaque)
                {
                    name = source.name + " (SQ lit)",
                    hideFlags = HideFlags.HideAndDontSave,
                };

                var texture = source.HasProperty("_BaseMap") ? source.GetTexture("_BaseMap") : source.mainTexture;
                if (texture != null)
                {
                    twin.mainTexture = texture;
                    twin.mainTextureScale = source.mainTextureScale;
                    twin.mainTextureOffset = source.mainTextureOffset;
                }

                twin.color = source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor")
                           : source.HasProperty("_Color") ? source.GetColor("_Color")
                           : Color.white;

                // URP defaults to back-face culling (2); the diffuse family defaults to
                // double-sided (0) — carry the source's intent so thin geometry keeps
                // rendering the way the pack authored it.
                twin.SetFloat("_Cull", source.HasProperty("_Cull") ? source.GetFloat("_Cull") : 2f);
            }

            s_converted[source] = twin;
            return twin;
        }
    }
}
