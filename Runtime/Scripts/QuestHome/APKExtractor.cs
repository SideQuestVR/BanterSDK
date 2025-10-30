using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEngine;

namespace Banter.SDK
{
    /// <summary>
    /// Represents extracted Quest Home asset data
    /// </summary>
    public class QuestHomeAssets
    {
        public byte[] gltfData;
        public byte[] binData;
        public Dictionary<string, byte[]> textures = new Dictionary<string, byte[]>();
        public byte[] audioData;
        public string gltfJson;
    }

    /// <summary>
    /// Extractor for Quest Home APK files
    /// Handles nested ZIP extraction: APK → assets/scene.zip → *.ovrscene → GLTF files
    /// </summary>
    public static class APKExtractor
    {
        /// <summary>
        /// Extract all Quest Home assets from APK data
        /// </summary>
        /// <param name="apkData">Raw APK file data</param>
        /// <returns>Extracted Quest Home assets</returns>
        public static QuestHomeAssets ExtractAll(byte[] apkData)
        {
            try
            {
                Debug.Log("Starting APK extraction...");

                // Step 1: Extract scene.zip from APK
                byte[] sceneZipData = ExtractSceneZip(apkData);
                if (sceneZipData == null)
                {
                    throw new Exception("Failed to extract scene.zip from APK");
                }

                Debug.Log($"Extracted scene.zip ({sceneZipData.Length} bytes)");

                // Step 2: Extract .ovrscene and audio from scene.zip
                var sceneContents = ExtractSceneContents(sceneZipData);
                if (sceneContents.ovrsceneData == null)
                {
                    throw new Exception("Failed to extract .ovrscene from scene.zip");
                }

                Debug.Log($"Extracted .ovrscene ({sceneContents.ovrsceneData.Length} bytes)");

                // Step 3: Extract GLTF files from .ovrscene
                var assets = ExtractGLTFFiles(sceneContents.ovrsceneData);
                assets.audioData = sceneContents.audioData;

                Debug.Log($"Extraction complete: GLTF={assets.gltfData != null}, BIN={assets.binData != null}, Textures={assets.textures.Count}, Audio={assets.audioData != null}");

                return assets;
            }
            catch (Exception ex)
            {
                Debug.LogError($"APK extraction failed: {ex.Message}\n{ex.StackTrace}");
                throw;
            }
        }

        /// <summary>
        /// Extract scene.zip from APK (first level ZIP)
        /// </summary>
        private static byte[] ExtractSceneZip(byte[] apkData)
        {
            using (var stream = new MemoryStream(apkData))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                // Find assets/scene.zip
                var sceneZipEntry = archive.Entries.FirstOrDefault(e =>
                    e.FullName.Equals("assets/scene.zip", StringComparison.OrdinalIgnoreCase));

                if (sceneZipEntry == null)
                {
                    Debug.LogError("assets/scene.zip not found in APK. Available entries:");
                    foreach (var entry in archive.Entries.Take(20))
                    {
                        Debug.Log($"  - {entry.FullName}");
                    }
                    throw new FileNotFoundException("assets/scene.zip not found in APK");
                }

                using (var entryStream = sceneZipEntry.Open())
                using (var memStream = new MemoryStream())
                {
                    entryStream.CopyTo(memStream);
                    return memStream.ToArray();
                }
            }
        }

        /// <summary>
        /// Extract .ovrscene and audio from scene.zip (second level ZIP)
        /// </summary>
        private static (byte[] ovrsceneData, byte[] audioData) ExtractSceneContents(byte[] sceneZipData)
        {
            using (var stream = new MemoryStream(sceneZipData))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                byte[] ovrsceneData = null;
                byte[] audioData = null;

                foreach (var entry in archive.Entries)
                {
                    // Find .ovrscene file (typically _WORLD_MODEL.gltf.ovrscene)
                    if (entry.FullName.EndsWith(".ovrscene", StringComparison.OrdinalIgnoreCase))
                    {
                        using (var entryStream = entry.Open())
                        using (var memStream = new MemoryStream())
                        {
                            entryStream.CopyTo(memStream);
                            ovrsceneData = memStream.ToArray();
                        }
                        Debug.Log($"Found .ovrscene: {entry.FullName}");
                    }
                    // Find background audio
                    else if (entry.FullName.Equals("_BACKGROUND_LOOP.ogg", StringComparison.OrdinalIgnoreCase))
                    {
                        using (var entryStream = entry.Open())
                        using (var memStream = new MemoryStream())
                        {
                            entryStream.CopyTo(memStream);
                            audioData = memStream.ToArray();
                        }
                        Debug.Log($"Found background audio: {entry.FullName}");
                    }
                }

                return (ovrsceneData, audioData);
            }
        }

        /// <summary>
        /// Extract GLTF files from .ovrscene (third level ZIP)
        /// </summary>
        private static QuestHomeAssets ExtractGLTFFiles(byte[] ovrsceneData)
        {
            var assets = new QuestHomeAssets();

            using (var stream = new MemoryStream(ovrsceneData))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                Debug.Log($".ovrscene contains {archive.Entries.Count} files:");

                foreach (var entry in archive.Entries)
                {
                    Debug.Log($"  - {entry.FullName} ({entry.Length} bytes)");

                    using (var entryStream = entry.Open())
                    using (var memStream = new MemoryStream())
                    {
                        entryStream.CopyTo(memStream);
                        byte[] data = memStream.ToArray();

                        if (entry.FullName.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase))
                        {
                            assets.gltfData = data;
                            assets.gltfJson = System.Text.Encoding.UTF8.GetString(data);
                            Debug.Log($"Extracted GLTF: {entry.FullName}");
                        }
                        else if (entry.FullName.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
                        {
                            assets.binData = data;
                            Debug.Log($"Extracted BIN: {entry.FullName}");
                        }
                        else if (entry.FullName.EndsWith(".ktx", StringComparison.OrdinalIgnoreCase))
                        {
                            // Extract texture name without path
                            string textureName = Path.GetFileName(entry.FullName);
                            assets.textures[textureName] = data;
                            Debug.Log($"Extracted texture: {textureName}");
                        }
                        else if (entry.FullName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                 entry.FullName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                 entry.FullName.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
                        {
                            // Some Quest homes may have PNG/JPG textures
                            string textureName = Path.GetFileName(entry.FullName);
                            assets.textures[textureName] = data;
                            Debug.Log($"Extracted image texture: {textureName}");
                        }
                    }
                }
            }

            return assets;
        }

        /// <summary>
        /// Validate that extracted assets are complete
        /// </summary>
        public static bool ValidateAssets(QuestHomeAssets assets)
        {
            if (assets == null)
            {
                Debug.LogError("Assets are null");
                return false;
            }

            if (assets.gltfData == null || assets.gltfData.Length == 0)
            {
                Debug.LogError("No GLTF data found");
                return false;
            }

            if (assets.binData == null || assets.binData.Length == 0)
            {
                Debug.LogWarning("No BIN data found (may be embedded in GLTF)");
            }

            if (assets.textures.Count == 0)
            {
                Debug.LogWarning("No textures found");
            }

            Debug.Log($"Assets validation: GLTF={assets.gltfData.Length} bytes, BIN={assets.binData?.Length ?? 0} bytes, Textures={assets.textures.Count}");

            return true;
        }
    }
}
