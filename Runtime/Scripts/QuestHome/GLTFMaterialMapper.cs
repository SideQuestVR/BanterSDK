using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Banter.SDK
{
    /// <summary>
    /// Represents a texture-to-material mapping from GLTF
    /// </summary>
    public class TextureMapping
    {
        public int materialIndex;
        public string materialName;
        public string textureType; // "baseColor" or "emissive"
        public string imageUri;    // e.g., "texture0.ktx"
        public int textureIndex;
        public string alphaMode;   // "OPAQUE", "MASK", or "BLEND"
        public float alphaCutoff;  // Used with MASK mode (default 0.5)
    }

    /// <summary>
    /// Helper class for parsing GLTF materials and applying textures
    /// Handles Quest Home-specific material requirements (unlit materials)
    /// </summary>
    public static class GLTFMaterialMapper
    {
        /// <summary>
        /// Parse GLTF JSON to extract texture-to-material mappings
        /// </summary>
        /// <param name="gltfJson">GLTF JSON string</param>
        /// <returns>List of texture mappings</returns>
        public static List<TextureMapping> ParseGLTF(string gltfJson)
        {
            var mappings = new List<TextureMapping>();

            try
            {
                JObject gltf = JObject.Parse(gltfJson);

                var materials = gltf["materials"] as JArray;
                var textures = gltf["textures"] as JArray;
                var images = gltf["images"] as JArray;

                if (materials == null || textures == null || images == null)
                {
                    Debug.LogWarning("GLTF missing materials, textures, or images arrays");
                    return mappings;
                }

                for (int matIndex = 0; matIndex < materials.Count; matIndex++)
                {
                    var material = materials[matIndex] as JObject;
                    string materialName = material["name"]?.ToString() ?? $"Material_{matIndex}";

                    // Extract alpha mode (OPAQUE, MASK, or BLEND)
                    string alphaMode = material["alphaMode"]?.ToString() ?? "OPAQUE";
                    float alphaCutoff = material["alphaCutoff"]?.Value<float>() ?? 0.5f;

                    // Check for base color texture
                    var pbrMetallicRoughness = material["pbrMetallicRoughness"] as JObject;
                    if (pbrMetallicRoughness != null)
                    {
                        var baseColorTexture = pbrMetallicRoughness["baseColorTexture"] as JObject;
                        if (baseColorTexture != null)
                        {
                            int textureIndex = baseColorTexture["index"]?.Value<int>() ?? -1;
                            if (textureIndex >= 0 && textureIndex < textures.Count)
                            {
                                var texture = textures[textureIndex] as JObject;
                                int imageIndex = texture["source"]?.Value<int>() ?? -1;
                                if (imageIndex >= 0 && imageIndex < images.Count)
                                {
                                    var image = images[imageIndex] as JObject;
                                    string imageUri = image["uri"]?.ToString();

                                    if (!string.IsNullOrEmpty(imageUri))
                                    {
                                        mappings.Add(new TextureMapping
                                        {
                                            materialIndex = matIndex,
                                            materialName = materialName,
                                            textureType = "baseColor",
                                            imageUri = imageUri,
                                            textureIndex = textureIndex,
                                            alphaMode = alphaMode,
                                            alphaCutoff = alphaCutoff
                                        });

                                        Debug.Log($"Material '{materialName}' uses base color texture: {imageUri} (alphaMode: {alphaMode})");
                                    }
                                }
                            }
                        }
                    }

                    // Check for emissive texture (fallback if no base color)
                    var emissiveTexture = material["emissiveTexture"] as JObject;
                    if (emissiveTexture != null)
                    {
                        int textureIndex = emissiveTexture["index"]?.Value<int>() ?? -1;
                        if (textureIndex >= 0 && textureIndex < textures.Count)
                        {
                            var texture = textures[textureIndex] as JObject;
                            int imageIndex = texture["source"]?.Value<int>() ?? -1;
                            if (imageIndex >= 0 && imageIndex < images.Count)
                            {
                                var image = images[imageIndex] as JObject;
                                string imageUri = image["uri"]?.ToString();

                                if (!string.IsNullOrEmpty(imageUri))
                                {
                                    // Only add if no base color texture exists
                                    if (!mappings.Any(m => m.materialIndex == matIndex && m.textureType == "baseColor"))
                                    {
                                        mappings.Add(new TextureMapping
                                        {
                                            materialIndex = matIndex,
                                            materialName = materialName,
                                            textureType = "emissive",
                                            imageUri = imageUri,
                                            textureIndex = textureIndex,
                                            alphaMode = alphaMode,
                                            alphaCutoff = alphaCutoff
                                        });

                                        Debug.Log($"Material '{materialName}' uses emissive texture: {imageUri} (alphaMode: {alphaMode})");
                                    }
                                }
                            }
                        }
                    }
                }

                Debug.Log($"Parsed {mappings.Count} texture mappings from GLTF");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to parse GLTF JSON: {ex.Message}");
            }

            return mappings;
        }

        /// <summary>
        /// Apply textures to materials in loaded GLTF model
        /// </summary>
        /// <param name="model">Root GameObject of loaded GLTF</param>
        /// <param name="textures">Dictionary of texture name → Texture2D</param>
        /// <param name="mappings">Texture mappings from ParseGLTF</param>
        public static void ApplyTextures(GameObject model, Dictionary<string, Texture2D> textures, List<TextureMapping> mappings)
        {
            if (model == null || textures == null || mappings == null)
            {
                Debug.LogWarning("Cannot apply textures: null parameters");
                return;
            }

            // Build material name → Material dictionary and alpha mode tracking
            var materialsByName = new Dictionary<string, List<Material>>();
            var alphaModeLookup = new Dictionary<string, (string alphaMode, float alphaCutoff)>();
            var renderers = model.GetComponentsInChildren<Renderer>(true);

            foreach (var renderer in renderers)
            {
                var materials = renderer.sharedMaterials;
                foreach (var material in materials)
                {
                    if (material == null) continue;

                    if (!materialsByName.ContainsKey(material.name))
                    {
                        materialsByName[material.name] = new List<Material>();
                    }
                    materialsByName[material.name].Add(material);
                }
            }

            Debug.Log($"Found {materialsByName.Count} unique materials in model");

            // Apply textures based on mappings and track alpha modes
            int texturesApplied = 0;
            foreach (var mapping in mappings)
            {
                // Get texture by imageUri (e.g., "texture0.ktx")
                if (!textures.TryGetValue(mapping.imageUri, out Texture2D texture))
                {
                    Debug.LogWarning($"Texture '{mapping.imageUri}' not found in texture dictionary");
                    continue;
                }

                // Find materials by name
                if (!materialsByName.TryGetValue(mapping.materialName, out List<Material> materials))
                {
                    Debug.LogWarning($"Material '{mapping.materialName}' not found in model");
                    continue;
                }

                // Track alpha mode for this material
                if (!alphaModeLookup.ContainsKey(mapping.materialName))
                {
                    alphaModeLookup[mapping.materialName] = (mapping.alphaMode, mapping.alphaCutoff);
                }

                // Check if this is a KTX texture (needs vertical flip)
                bool isKTX = mapping.imageUri.EndsWith(".ktx", System.StringComparison.OrdinalIgnoreCase);

                // Apply texture to all instances of this material
                foreach (var material in materials)
                {
                    if (mapping.textureType == "baseColor")
                    {
                        material.mainTexture = texture;
                        if (material.HasProperty("_BaseMap"))
                        {
                            material.SetTexture("_BaseMap", texture);
                        }

                        // Flip KTX textures vertically (coordinate system difference)
                        if (isKTX)
                        {
                            material.mainTextureScale = new Vector2(1, -1);
                            material.mainTextureOffset = new Vector2(0, 1);

                            if (material.HasProperty("_BaseMap"))
                            {
                                material.SetTextureScale("_BaseMap", new Vector2(1, -1));
                                material.SetTextureOffset("_BaseMap", new Vector2(0, 1));
                            }
                        }

                        texturesApplied++;
                    }
                    else if (mapping.textureType == "emissive")
                    {
                        if (material.HasProperty("_EmissionMap"))
                        {
                            material.SetTexture("_EmissionMap", texture);
                            material.EnableKeyword("_EMISSION");

                            // Flip KTX textures vertically
                            if (isKTX)
                            {
                                material.SetTextureScale("_EmissionMap", new Vector2(1, -1));
                                material.SetTextureOffset("_EmissionMap", new Vector2(0, 1));
                            }
                        }
                        // Also set as main texture if no base color
                        if (material.mainTexture == null)
                        {
                            material.mainTexture = texture;

                            // Flip KTX textures vertically
                            if (isKTX)
                            {
                                material.mainTextureScale = new Vector2(1, -1);
                                material.mainTextureOffset = new Vector2(0, 1);
                            }
                        }
                        texturesApplied++;
                    }
                }

                Debug.Log($"Applied texture '{mapping.imageUri}' to material '{mapping.materialName}' ({materials.Count} instances)");
            }

            Debug.Log($"Applied {texturesApplied} textures to materials");

            // Apply correct shaders based on alpha mode
            int shadersApplied = 0;
            foreach (var kvp in alphaModeLookup)
            {
                string materialName = kvp.Key;
                string alphaMode = kvp.Value.alphaMode;
                float alphaCutoff = kvp.Value.alphaCutoff;

                if (materialsByName.TryGetValue(materialName, out List<Material> materials))
                {
                    foreach (var material in materials)
                    {
                        ApplyShaderForAlphaMode(material, alphaMode, alphaCutoff);
                        shadersApplied++;
                    }
                }
            }

            Debug.Log($"Applied alpha-aware shaders to {shadersApplied} material instances");
        }

        /// <summary>
        /// Apply the appropriate unlit shader based on GLTF alpha mode
        /// </summary>
        /// <param name="material">Material to update</param>
        /// <param name="alphaMode">GLTF alpha mode (OPAQUE, MASK, or BLEND)</param>
        /// <param name="alphaCutoff">Alpha cutoff value for MASK mode</param>
        private static void ApplyShaderForAlphaMode(Material material, string alphaMode, float alphaCutoff)
        {
            if (material == null) return;

            // Store current texture and color
            Texture mainTex = material.mainTexture;
            Color color = material.HasProperty("_Color") ? material.color : Color.white;
            Vector2 textureScale = material.mainTextureScale;
            Vector2 textureOffset = material.mainTextureOffset;

            Shader targetShader = null;

            switch (alphaMode)
            {
                case "MASK":
                    // Alpha clipping - use Transparent Cutout
                    targetShader = Shader.Find("Unlit/Transparent Cutout");
                    if (targetShader != null)
                    {
                        material.shader = targetShader;
                        if (material.HasProperty("_Cutoff"))
                        {
                            material.SetFloat("_Cutoff", alphaCutoff);
                        }
                        Debug.Log($"Applied Unlit/Transparent Cutout to material '{material.name}' (cutoff: {alphaCutoff})");
                    }
                    break;

                case "BLEND":
                    // Alpha blending - use Transparent
                    targetShader = Shader.Find("Unlit/Transparent");
                    if (targetShader != null)
                    {
                        material.shader = targetShader;
                        // Enable transparency
                        if (material.HasProperty("_Mode"))
                        {
                            material.SetFloat("_Mode", 3); // Transparent mode
                        }
                        Debug.Log($"Applied Unlit/Transparent to material '{material.name}'");
                    }
                    break;

                case "OPAQUE":
                default:
                    // Opaque - use regular Unlit/Texture
                    targetShader = Shader.Find("Unlit/Texture");
                    if (targetShader != null)
                    {
                        material.shader = targetShader;
                        Debug.Log($"Applied Unlit/Texture to material '{material.name}'");
                    }
                    break;
            }

            // Restore texture and properties
            if (mainTex != null)
            {
                material.mainTexture = mainTex;
                material.mainTextureScale = textureScale;
                material.mainTextureOffset = textureOffset;
            }
            if (material.HasProperty("_Color"))
            {
                material.color = color;
            }
        }

        /// <summary>
        /// Convert material to unlit shader (Quest Home optimization)
        /// </summary>
        /// <param name="material">Material to convert</param>
        public static void ConvertToUnlit(Material material)
        {
            if (material == null) return;

            // Try to find Unlit/Texture shader
            Shader unlitShader = Shader.Find("Unlit/Texture");
            if (unlitShader == null)
            {
                // Fallback to standard unlit
                unlitShader = Shader.Find("Unlit/Color");
            }

            if (unlitShader != null)
            {
                // Store current texture
                Texture mainTex = material.mainTexture;
                Color color = material.HasProperty("_Color") ? material.color : Color.white;

                // Switch shader
                material.shader = unlitShader;

                // Restore texture and color
                if (mainTex != null)
                {
                    material.mainTexture = mainTex;
                }
                if (material.HasProperty("_Color"))
                {
                    material.color = color;
                }
            }
            else
            {
                Debug.LogWarning("Unlit shader not found, keeping original shader");
            }
        }

        /// <summary>
        /// Convert all materials in model to unlit shaders
        /// </summary>
        /// <param name="model">Root GameObject</param>
        public static void ConvertAllToUnlit(GameObject model)
        {
            if (model == null) return;

            var renderers = model.GetComponentsInChildren<Renderer>(true);
            int converted = 0;
            int skipped = 0;

            foreach (var renderer in renderers)
            {
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material != null)
                    {
                        // Skip materials that already have alpha-aware unlit shaders
                        string shaderName = material.shader.name;
                        if (shaderName.StartsWith("Unlit/", System.StringComparison.OrdinalIgnoreCase))
                        {
                            // Material already has an unlit shader (Texture, Transparent, Transparent Cutout, etc.)
                            skipped++;
                            continue;
                        }

                        ConvertToUnlit(material);
                        converted++;
                    }
                }
            }

            Debug.Log($"Converted {converted} materials to unlit shaders ({skipped} already had unlit shaders)");
        }

        /// <summary>
        /// Check if material is opaque (for collider generation)
        /// </summary>
        /// <param name="material">Material to check</param>
        /// <returns>True if material is opaque</returns>
        public static bool IsOpaqueMaterial(Material material)
        {
            if (material == null) return false;

            // Check if material is transparent
            if (material.HasProperty("_Mode"))
            {
                float mode = material.GetFloat("_Mode");
                if (mode == 3) return false; // Transparent mode
            }

            // Check alpha
            if (material.HasProperty("_Color"))
            {
                Color color = material.color;
                if (color.a < 1.0f) return false;
            }

            // Check render queue (transparent materials typically use queue > 2500)
            if (material.renderQueue > 2500) return false;

            return true;
        }
    }
}
