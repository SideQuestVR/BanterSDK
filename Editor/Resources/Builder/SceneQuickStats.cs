using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;

/// <summary>
/// Quick, dependency-graph-based triangle/texture-memory summary for whatever Scene or
/// Prefab(s) were just dropped into Altspace Builder - works on unopened Scene/Prefab assets
/// (no need to load the scene), same technique as Bundle Analyzer's dependency walker. Not a
/// full breakdown (see Bundle Analyzer for that) - just enough for an at-a-glance sanity check
/// right after dropping something in.
/// </summary>
public static class SceneQuickStats
{
    public readonly struct Stats
    {
        public readonly long TriangleCount;
        public readonly long TextureMemoryBytes;
        public readonly int MeshCount;
        public readonly int TextureCount;

        public Stats(long triangleCount, long textureMemoryBytes, int meshCount, int textureCount)
        {
            TriangleCount = triangleCount;
            TextureMemoryBytes = textureMemoryBytes;
            MeshCount = meshCount;
            TextureCount = textureCount;
        }
    }

    public static Stats Compute(IEnumerable<string> rootAssetPaths)
    {
        var meshAssetPaths = new HashSet<string>();
        var texturePaths = new HashSet<string>();

        foreach (var root in rootAssetPaths)
        {
            if (string.IsNullOrEmpty(root))
                continue;

            foreach (var dep in AssetDatabase.GetDependencies(root, true))
            {
                if (dep == root || AssetDatabase.IsValidFolder(dep))
                    continue;

                var mainType = AssetDatabase.GetMainAssetTypeAtPath(dep);
                if (mainType != null && typeof(Texture).IsAssignableFrom(mainType))
                {
                    texturePaths.Add(dep);
                    continue;
                }

                // Meshes embedded in a model file (FBX etc.) have a main type other than Mesh
                // (usually GameObject), so check the importer rather than the asset type.
                if (AssetImporter.GetAtPath(dep) is ModelImporter || (mainType != null && typeof(Mesh).IsAssignableFrom(mainType)))
                    meshAssetPaths.Add(dep);
            }
        }

        long triangleCount = 0;
        int meshCount = 0;
        foreach (var path in meshAssetPaths)
        {
            foreach (var mesh in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>())
            {
                meshCount++;
                long indexCount = 0;
                for (int i = 0; i < mesh.subMeshCount; i++)
                    indexCount += mesh.GetIndexCount(i);
                triangleCount += indexCount / 3;
            }
        }

        long textureMemoryBytes = 0;
        int textureCount = 0;
        foreach (var path in texturePaths)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture>(path);
            if (tex == null)
                continue;
            textureCount++;
            textureMemoryBytes += Profiler.GetRuntimeMemorySizeLong(tex);
        }

        return new Stats(triangleCount, textureMemoryBytes, meshCount, textureCount);
    }

    public static string FormatSummary(Stats stats)
    {
        return $"{FormatTriangles(stats.TriangleCount)} triangles ({stats.MeshCount} meshes)  ·  " +
               $"{FormatBytes(stats.TextureMemoryBytes)} texture memory ({stats.TextureCount} textures)";
    }

    static string FormatTriangles(long count)
    {
        if (count >= 1_000_000) return $"{count / 1_000_000.0:0.##}M";
        if (count >= 1_000) return $"{count / 1_000.0:0.#}K";
        return count.ToString();
    }

    static string FormatBytes(long bytes)
    {
        double size = bytes;
        string[] units = { "B", "KB", "MB", "GB" };
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return unit == 0 ? $"{size:0} {units[unit]}" : $"{size:0.0} {units[unit]}";
    }
}
