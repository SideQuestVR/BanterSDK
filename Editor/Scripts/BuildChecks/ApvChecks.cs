using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BS.SDKEditor.BuildChecks
{
    /// <summary>
    /// Baked Adaptive Probe Volumes (APV) data in a scene makes the client light the world with APV, whatever the
    /// project uses. The data must be wanted, and loadable: the client copies it without bounds checks, so data that
    /// doesn't match its own descriptors crashes players (Duke's Cinema, October 2026: APV data left in the scene after
    /// the project went back to Light Probe Groups).
    /// </summary>
    sealed class ApvDataCheck : BuildCheck
    {
        public override string Id => "render.apv-data";
        public override string Title => "Adaptive Probe Volumes data";
        public override string Description =>
            "Baked Adaptive Probe Volumes data is only in the scene when the project lights with Adaptive Probe Volumes, and it is complete. " +
            "Players' clients light any scene that carries it with Adaptive Probe Volumes.";
        public override int Order => 95;

        /// <summary>
        /// The name URP gives the object it adds to a scene baked with APV (ProbeVolumeBakingSet.EnsurePerSceneData).
        /// It hides it from the Hierarchy, so creators never see it.
        /// </summary>
        internal const string PerSceneDataObjectName = "ProbeVolumePerSceneData";

        /// <summary>
        /// The SH bands of the app's APV pipelines (L1 on Windows and Quest). The data is checked against what the app
        /// reads, not against the creator's own pipeline: a bake always writes the L2 file, which an L1 app never reads.
        /// </summary>
        internal const ProbeVolumeSHBands AppShBands = ProbeVolumeSHBands.SphericalHarmonicsL1;

        public override void Run(BuildCheckContext context, List<BuildCheckIssue> issues)
        {
            // The client only counts per-scene data that is active and has a baking set.
            var data = context.Components<ProbeVolumePerSceneData>()
                .Where(d => d.bakingSet != null && d.gameObject.activeInHierarchy)
                .ToList();
            if (data.Count == 0)
                return;
            Find(data, ProjectUsesApv(context.Targets), context.Target, issues);
        }

        /// <summary>What <paramref name="data"/> means for a project that does or doesn't light with APV.</summary>
        internal static void Find(IReadOnlyList<ProbeVolumePerSceneData> data, bool projectUsesApv,
            System.Func<GameObject, BuildCheckTarget> target, List<BuildCheckIssue> issues)
        {
            var sets = data.Select(d => d.bakingSet).Distinct().ToList();
            string setNames = string.Join(", ", sets.Select(set => $"'{set.name}'"));

            if (!projectUsesApv)
            {
                issues.Add(new BuildCheckIssue("render.apv-data", BuildCheckSeverity.Error,
                        "The scene still has Adaptive Probe Volumes data, but the project uses Light Probe Groups",
                        $"An earlier Adaptive Probe Volumes bake left a hidden '{PerSceneDataObjectName}' object and the baking set {setNames} " +
                        "in the scene. Rebaking with Light Probe Groups doesn't remove them. Players' clients see the data and light the world " +
                        "with Adaptive Probe Volumes anyway, and data left over from an older bake can crash them. Remove it; you can then " +
                        "delete the baking set and its data files from the project if nothing else uses them.")
                    .WithTargets(data.Select(d => target(d.gameObject)))
                    .WithTargets(sets.Select(set => BuildCheckTarget.ForAsset(set)))
                    .WithFix("Remove the Adaptive Probe Volumes data", issue => RemovePerSceneData(issue.Resolve<GameObject>())));
                return;
            }

            // A scene the baking set has no cells for: the app refuses it APV (loading it would throw every frame).
            foreach (var sceneData in data)
            {
                if (ApvBakedData.HasCellsForScene(sceneData) != false)
                    continue;
                issues.Add(new BuildCheckIssue("render.apv-data", BuildCheckSeverity.Error,
                        $"Baking set '{sceneData.bakingSet.name}' has no Adaptive Probe Volumes cells for this scene",
                        "The scene was probably copied or renamed after its lighting was baked. Players' clients would light the world " +
                        "without Adaptive Probe Volumes. Rebake the lighting (Window > Rendering > Lighting > Generate Lighting).")
                    .WithTargets(new[] { target(sceneData.gameObject), BuildCheckTarget.ForAsset(sceneData.bakingSet) }));
            }

            if (!ApvBakedData.CanCheck)
            {
                issues.Add(new BuildCheckIssue("render.apv-data", BuildCheckSeverity.Info,
                    "Adaptive Probe Volumes data wasn't checked",
                    "This URP version's probe volume internals changed, so the SDK can't check the baked data here."));
                return;
            }

            foreach (var set in sets)
            {
                string problem = ApvBakedData.FindProblem(set, AppShBands);
                if (problem == null)
                    continue;
                issues.Add(new BuildCheckIssue("render.apv-data", BuildCheckSeverity.Error,
                        $"Baking set '{set.name}' has Adaptive Probe Volumes data that can't be loaded",
                        $"The baking set's data can't be loaded safely: {problem} Players' clients would light the world without it, and " +
                        "older clients can crash on it. Rebake the lighting (Window > Rendering > Lighting > Generate Lighting).")
                    .WithTargets(new[] { BuildCheckTarget.ForAsset(set) }));
            }
        }

        /// <summary>
        /// Whether any URP asset the build targets use lights with APV: the same asset list URP's own build stripping
        /// reads. (Greenfield itself always says yes: its quality levels hold the app's APV pipelines.)
        /// </summary>
        static bool ProjectUsesApv(BuildTarget[] targets)
        {
            var assets = new List<UniversalRenderPipelineAsset>();
            foreach (var buildTarget in targets.Length > 0 ? targets : new[] { EditorUserBuildSettings.activeBuildTarget })
            {
                assets.Clear();
                if (buildTarget.TryGetRenderPipelineAssets(assets) &&
                    assets.Any(asset => asset != null && asset.lightProbeSystem == LightProbeSystem.ProbeVolumes))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Removes the per-scene data, with undo: the whole object when it is URP's own (nothing but the data on it),
        /// otherwise just the component.
        /// </summary>
        internal static bool RemovePerSceneData(IEnumerable<GameObject> objects)
        {
            bool changed = false;
            foreach (var go in objects.ToList())
            {
                if (go == null)
                    continue;
                var data = go.GetComponents<ProbeVolumePerSceneData>();
                if (data.Length == 0)
                    continue;
                bool onlyTheData = go.transform.childCount == 0 &&
                                   go.GetComponents<Component>().All(c => c is Transform || c is ProbeVolumePerSceneData);
                if (onlyTheData)
                    Undo.DestroyObjectImmediate(go);
                else
                {
                    foreach (var component in data)
                        Undo.DestroyObjectImmediate(component);
                }
                changed = true;
            }
            return changed;
        }
    }
}
