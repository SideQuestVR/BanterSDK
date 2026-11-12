using System;
using System.Collections.Generic;
using System.Linq;
using BS;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Leaves <c>assets.manifest.json</c> in a world's scene as it builds: see <see cref="BSAssetManifest"/> for why a scene
/// and not the bundle. Runs on the build copy, so the creator's scene doesn't change.
/// </summary>
static class AssetManifestStamp
{
    /// <summary>Past this a world is listed by walking its scene, as one built before manifests.</summary>
    internal const int MaxAssets = 8192;

    internal static void Apply(Scene scene)
    {
        var manifest = Collect(scene);
        if (manifest.Assets.Count > MaxAssets)
        {
            Debug.LogWarning($"[Creator SDK] {scene.name} uses {manifest.Assets.Count} assets, over the {MaxAssets} a manifest lists. It builds without one.");
            return;
        }
        var json = manifest.ToJson(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"), Application.unityVersion,
            GraphicsSettings.currentRenderPipeline != null ? "URP" : "BuiltIn", PlayerSettings.colorSpace.ToString());

        var holder = new GameObject(BSAssetManifest.HolderName);
        SceneManager.MoveGameObjectToScene(holder, scene);
        holder.SetActive(false);
        foreach (var chunk in BSAssetManifest.ToChunks(json))
        {
            var part = new GameObject(chunk);
            SceneManager.MoveGameObjectToScene(part, scene);
            part.transform.SetParent(holder.transform, false);
        }
    }

    /// <summary>What the scene depends on, by asset path. Scene objects themselves aren't assets, and go unlisted.</summary>
    internal static BSAssetManifest Collect(Scene scene)
    {
        var manifest = new BSAssetManifest();
        var seen = new HashSet<UnityEngine.Object>();
        foreach (var dependency in EditorUtility.CollectDependencies(scene.GetRootGameObjects().Cast<UnityEngine.Object>().ToArray()))
        {
            var type = TypeOf(dependency);
            if (type == null || !AssetDatabase.Contains(dependency) || !seen.Add(dependency)) continue;
            var path = AssetDatabase.GetAssetPath(dependency).ToLowerInvariant();
            // A sub-asset, such as a mesh inside a model, shares its file's path.
            if (AssetDatabase.IsSubAsset(dependency)) path += "/" + dependency.name.ToLowerInvariant();
            manifest.Assets.Add(new BSAssetManifest.Item
            {
                Name = dependency.name,
                Path = path,
                Guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(dependency)),
                Type = type,
                Size = Profiler.GetRuntimeMemorySizeLong(dependency),
            });
        }
        manifest.Assets.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
        return manifest;
    }

    static AssetType? TypeOf(UnityEngine.Object asset)
    {
        switch (asset)
        {
            case Texture2D _: return AssetType.Texture2D;
            case AudioClip _: return AssetType.AudioClip;
            case Material _: return AssetType.Material;
            case Mesh _: return AssetType.Mesh;
            default: return null;
        }
    }
}
