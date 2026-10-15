using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Records which URP light loop a bundle section's shaders were compiled for, in
/// <see cref="BasisBundleGenerated.RenderingPath"/>, so the client can render the world with a
/// matching renderer before it loads.
///
/// It has to be recorded at build time: a player can't list a shader's compiled variants, but
/// URP's stripping leaves a clean signature. Each renderer that a build's pipeline assets list
/// contributes one feature set, and a variant survives if any set needs it. A Forward+ (or
/// Deferred+) renderer keeps the <c>_CLUSTER_LIGHT_LOOP</c> variants; a Forward (or Deferred)
/// renderer keeps the ones without. So the section holds:
///   - only cluster variants when every renderer uses the cluster light loop → ForwardPlus;
///   - none when no renderer does → Forward;
///   - both when renderers disagree, or when "Strip Unused Variants" is off → Mixed.
/// The asset list is <see cref="BuildTargetExtensions.TryGetRenderPipelineAssets{T}"/>, the same
/// one URP's stripping reads for that target.
/// </summary>
[InitializeOnLoad]
internal static class UrpRenderingPathResolver
{
    static UrpRenderingPathResolver()
    {
        AssetBundleBuilder.RenderingPathResolver = Resolve;
    }

    internal static string Resolve(BuildTarget target)
    {
        var assets = new List<UniversalRenderPipelineAsset>();
        if (!target.TryGetRenderPipelineAssets(assets))
        {
            Debug.LogWarning("[Creator SDK] No URP assets for " + target + "; the bundle won't record a rendering path.");
            return null;
        }

        bool anyClusterLightLoop = false;
        bool anyWithout = false;
        foreach (var asset in assets)
        {
            if (asset == null)
                continue;
            foreach (var data in asset.rendererDataList)
            {
                if (data == null)
                    continue;
                // Renderers other than the Universal Renderer (2D) have no cluster light loop.
                if (data is UniversalRendererData universal && universal.usesClusterLightLoop)
                    anyClusterLightLoop = true;
                else
                    anyWithout = true;
            }
        }

        bool stripUnusedVariants = !GraphicsSettings.TryGetRenderPipelineSettings<URPShaderStrippingSetting>(out var stripping) ||
                                   stripping.stripUnusedVariants;
        string path = Classify(anyClusterLightLoop, anyWithout, stripUnusedVariants);
        Debug.Log("[Creator SDK] " + target + " rendering path: " + (path ?? "unknown") + ".");
        return path;
    }

    /// <summary>The recorded path for the light loops a target's renderers use. Null when there are no renderers.</summary>
    internal static string Classify(bool anyClusterLightLoop, bool anyWithout, bool stripUnusedVariants)
    {
        if (!anyClusterLightLoop && !anyWithout)
            return null;
        if (!stripUnusedVariants || (anyClusterLightLoop && anyWithout))
            return BasisBundleGenerated.RenderingPathMixed;
        return anyClusterLightLoop ? BasisBundleGenerated.RenderingPathForwardPlus : BasisBundleGenerated.RenderingPathForward;
    }
}
