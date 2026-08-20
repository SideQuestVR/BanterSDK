using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SideQuest.BundleAnalyzer
{
    /// <summary>
    /// Estimates the AssetBundle footprint of a Scene or Prefab's dependencies without
    /// building anything - AssetDatabase.GetDependencies gives the exact dependency graph
    /// instantly, and per-asset size is estimated from each asset's actual per-platform
    /// import settings (texture compression format, audio compression, mesh compression)
    /// where those apply, falling back to on-disk file size everywhere else.
    /// </summary>
    public static class DependencySizeAnalyzer
    {
        static readonly Dictionary<TextureImporterFormat, double> BitsPerPixel = new()
        {
            { TextureImporterFormat.ASTC_4x4, 8.0 },
            { TextureImporterFormat.ASTC_5x5, 5.12 },
            { TextureImporterFormat.ASTC_6x6, 3.56 },
            { TextureImporterFormat.ASTC_8x8, 2.0 },
            { TextureImporterFormat.ASTC_10x10, 1.28 },
            { TextureImporterFormat.ASTC_12x12, 0.89 },
            { TextureImporterFormat.ETC2_RGB4, 4.0 },
            { TextureImporterFormat.ETC2_RGBA8, 8.0 },
            { TextureImporterFormat.ETC_RGB4, 4.0 },
            { TextureImporterFormat.DXT1, 4.0 },
            { TextureImporterFormat.DXT5, 8.0 },
            { TextureImporterFormat.BC7, 8.0 },
            { TextureImporterFormat.BC4, 4.0 },
            { TextureImporterFormat.BC5, 8.0 },
            { TextureImporterFormat.RGBA32, 32.0 },
            { TextureImporterFormat.RGB24, 24.0 },
            { TextureImporterFormat.RGBA16, 16.0 },
            { TextureImporterFormat.RGB16, 16.0 },
            { TextureImporterFormat.Alpha8, 8.0 },
            { TextureImporterFormat.PVRTC_RGB2, 2.0 },
            { TextureImporterFormat.PVRTC_RGBA2, 2.0 },
            { TextureImporterFormat.PVRTC_RGB4, 4.0 },
            { TextureImporterFormat.PVRTC_RGBA4, 4.0 },
        };

        /// <summary>
        /// All assets the given Scene/Prefab depends on (recursive), excluding the root asset
        /// itself and folders. Instant - just reads Unity's already-indexed dependency graph.
        /// </summary>
        public static string[] GetDependencyPaths(string rootAssetPath)
        {
            return AssetDatabase.GetDependencies(rootAssetPath, true)
                .Where(p => p != rootAssetPath && !AssetDatabase.IsValidFolder(p))
                .ToArray();
        }

        public static AssetDependencyEntry EstimateEntry(string path, string platform)
        {
            var mainType = AssetDatabase.GetMainAssetTypeAtPath(path);
            string className = mainType != null ? mainType.Name : Path.GetExtension(path).TrimStart('.');

            var importer = AssetImporter.GetAtPath(path);
            long size;
            bool estimated;

            if (importer is TextureImporter texImporter)
            {
                size = EstimateTextureBytes(texImporter, platform);
                estimated = true;
            }
            else if (importer is AudioImporter audioImporter)
            {
                size = EstimateAudioBytes(audioImporter, path, platform);
                estimated = true;
            }
            else if (importer is ModelImporter modelImporter)
            {
                size = EstimateMeshBytes(modelImporter, path);
                estimated = true;
            }
            else
            {
                size = SafeFileLength(path);
                estimated = false;
            }

            return new AssetDependencyEntry
            {
                AssetPath = path,
                ClassName = className,
                Name = Path.GetFileNameWithoutExtension(path),
                EstimatedSizeBytes = size,
                IsSizeEstimated = estimated,
            };
        }

        static long EstimateTextureBytes(TextureImporter importer, string platform)
        {
            importer.GetSourceTextureWidthAndHeight(out int srcWidth, out int srcHeight);

            var platformSettings = importer.GetPlatformTextureSettings(platform);
            int maxSize = platformSettings.overridden ? platformSettings.maxTextureSize : importer.maxTextureSize;
            TextureImporterFormat format = platformSettings.overridden ? platformSettings.format : TextureImporterFormat.Automatic;

            int width = srcWidth, height = srcHeight;
            int longEdge = Mathf.Max(width, height);
            if (longEdge > maxSize && longEdge > 0)
            {
                double scale = maxSize / (double)longEdge;
                width = Mathf.Max(1, (int)(width * scale));
                height = Mathf.Max(1, (int)(height * scale));
            }

            double bpp = ResolveBitsPerPixel(format, platform, importer);
            double mipFactor = importer.mipmapEnabled ? 1.333 : 1.0;

            return (long)(bpp / 8.0 * width * height * mipFactor);
        }

        static double ResolveBitsPerPixel(TextureImporterFormat format, string platform, TextureImporter importer)
        {
            if (BitsPerPixel.TryGetValue(format, out var bpp))
                return bpp;

            // Automatic (or an unresolved format) - assume a typical default for the platform.
            // Flagged as an estimate to the caller regardless via IsSizeEstimated.
            if (platform == "Android" || platform == "iPhone")
                return BitsPerPixel[TextureImporterFormat.ASTC_6x6];

            bool hasAlpha = importer.DoesSourceTextureHaveAlpha();
            return hasAlpha ? BitsPerPixel[TextureImporterFormat.BC7] : BitsPerPixel[TextureImporterFormat.DXT1];
        }

        static long EstimateAudioBytes(AudioImporter importer, string path, string platform)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null)
                return SafeFileLength(path);

            AudioImporterSampleSettings settings;
            try
            {
                settings = importer.GetOverrideSampleSettings(platform);
            }
            catch
            {
                settings = importer.defaultSampleSettings;
            }

            double seconds = clip.length;
            switch (settings.compressionFormat)
            {
                case AudioCompressionFormat.PCM:
                    return (long)(seconds * clip.frequency * clip.channels * 2);
                case AudioCompressionFormat.ADPCM:
                    return (long)(seconds * clip.frequency * clip.channels * 2 / 4.0);
                default: // Vorbis, MP3, and anything else compressed - approximate via quality-derived bitrate.
                    double kbps = Mathf.Lerp(45f, 320f, Mathf.Clamp01(settings.quality));
                    return (long)(seconds * kbps * 1000.0 / 8.0);
            }
        }

        static long EstimateMeshBytes(ModelImporter importer, string path)
        {
            var meshes = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().ToList();
            if (meshes.Count == 0)
                return SafeFileLength(path);

            long total = 0;
            foreach (var mesh in meshes)
                total += EstimateSingleMeshBytes(mesh);

            double factor = importer.meshCompression switch
            {
                ModelImporterMeshCompression.Low => 0.7,
                ModelImporterMeshCompression.Medium => 0.5,
                ModelImporterMeshCompression.High => 0.35,
                _ => 1.0,
            };

            return (long)(total * factor);
        }

        static long EstimateSingleMeshBytes(Mesh mesh)
        {
            long bytesPerVertex = 0;
            foreach (var attr in mesh.GetVertexAttributes())
            {
                int compSize = attr.format switch
                {
                    VertexAttributeFormat.Float32 or VertexAttributeFormat.UInt32 or VertexAttributeFormat.SInt32 => 4,
                    VertexAttributeFormat.Float16 or VertexAttributeFormat.UNorm16 or VertexAttributeFormat.SNorm16
                        or VertexAttributeFormat.UInt16 or VertexAttributeFormat.SInt16 => 2,
                    _ => 1,
                };
                bytesPerVertex += (long)attr.dimension * compSize;
            }

            long vertexBytes = bytesPerVertex * mesh.vertexCount;

            long indexCount = 0;
            for (int i = 0; i < mesh.subMeshCount; i++)
                indexCount += mesh.GetIndexCount(i);

            long indexBytes = (mesh.indexFormat == UnityEngine.Rendering.IndexFormat.UInt16 ? 2 : 4) * indexCount;

            return vertexBytes + indexBytes;
        }

        static long SafeFileLength(string path)
        {
            try { return new FileInfo(path).Length; }
            catch { return 0; }
        }
    }
}
