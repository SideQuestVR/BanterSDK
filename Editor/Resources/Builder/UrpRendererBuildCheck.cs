using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

internal static class UrpRendererBuildCheck
{
    internal sealed class Mismatch
    {
        internal string Description;
        internal UnityEngine.Object AssetToOpen;
        internal UniversalRendererData Renderer;
        internal RenderingMode Expected;
    }

    internal static bool ConfirmBeforeSceneBuild(BuildTarget[] targets, bool[] selected, Action<string> report)
    {
        var mismatches = FindMismatches(targets, selected, out bool sharedRendererConflict);
        if (mismatches.Count == 0)
            return true;

        bool canChange = TryPlanChange(mismatches, sharedRendererConflict, out var requestedModes);

        var details = string.Join("\n", mismatches.Take(8).Select(x => "• " + x.Description));
        if (mismatches.Count > 8)
            details += "\n• …and " + (mismatches.Count - 8) + " more (see Console)";
        Debug.LogWarning("[Creator SDK] Scene build URP renderer check:\n" +
                         string.Join("\n", mismatches.Select(x => x.Description)));

        // Unattended builds cannot consent to modifying renderer assets.
        if (Application.isBatchMode)
        {
            report("URP renderer warning: unattended build keeps the current settings.");
            return true;
        }

        int choice = EditorUtility.DisplayDialogComplex(
            "Scene build renderer check",
            "Suggested modes: Windows Forward+, Android Forward.\n\n" + details +
            (canChange ? "\n\nChange the listed renderer assets?" :
                         "\n\nSome assets are missing, read-only, or shared by both targets. Review them manually."),
            canChange ? "Change and Build" : "Inspect Setup",
            "Build Anyway",
            canChange ? "Inspect Renderer" : "Cancel Build");

        if (choice == 1)
        {
            report("URP renderer check: building with the current settings.");
            return true;
        }
        if (choice == 2 || !canChange)
        {
            if ((choice == 2 && canChange) || (choice == 0 && !canChange))
                OpenFirstMismatch(mismatches[0]);
            report("Scene build cancelled at the URP renderer check.");
            return false;
        }

        ApplyModes(requestedModes);

        if (FindMismatches(targets, selected, out _).Count != 0)
        {
            report("URP renderer modes still differ. Review the renderer assets before building.");
            return false;
        }

        report("URP renderer modes updated for the selected scene build targets.");
        return true;
    }

    /// <summary>
    /// The renderer assets to change and the mode for each. False when they can't simply be changed:
    /// missing, read-only or package assets, or one renderer that two targets want in different modes.
    /// </summary>
    internal static bool TryPlanChange(List<Mismatch> mismatches, bool sharedRendererConflict,
        out Dictionary<UniversalRendererData, RenderingMode> requestedModes)
    {
        requestedModes = new Dictionary<UniversalRendererData, RenderingMode>();
        if (sharedRendererConflict)
            return false;
        foreach (var mismatch in mismatches)
        {
            string assetPath = mismatch.Renderer == null ? null : AssetDatabase.GetAssetPath(mismatch.Renderer);
            if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith("Assets/", StringComparison.Ordinal) ||
                !AssetDatabase.IsOpenForEdit(assetPath) ||
                (requestedModes.TryGetValue(mismatch.Renderer, out var prior) && prior != mismatch.Expected))
            {
                return false;
            }
            requestedModes[mismatch.Renderer] = mismatch.Expected;
        }
        return true;
    }

    internal static void ApplyModes(Dictionary<UniversalRendererData, RenderingMode> requestedModes)
    {
        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Set scene build URP renderer modes");
        foreach (var pair in requestedModes)
        {
            Undo.RecordObject(pair.Key, "Set URP rendering mode");
            pair.Key.renderingMode = pair.Value;
            EditorUtility.SetDirty(pair.Key);
        }
        Undo.CollapseUndoOperations(undoGroup);
        AssetDatabase.SaveAssets();
    }

    internal static List<Mismatch> FindMismatches(BuildTarget[] targets, bool[] selected,
        out bool sharedRendererConflict)
    {
        var result = new List<Mismatch>();
        var expectedByRenderer = new Dictionary<UniversalRendererData, RenderingMode>();
        sharedRendererConflict = false;
        for (int targetIndex = 0; targetIndex < targets.Length; targetIndex++)
        {
            if (targetIndex >= selected.Length || !selected[targetIndex])
                continue;

            BuildTarget target = targets[targetIndex];
            RenderingMode expected = target == BuildTarget.Android ? RenderingMode.Forward : RenderingMode.ForwardPlus;
            string platform = target == BuildTarget.Android ? "Android" : "Windows";
            string targetName = NamedBuildTarget.FromBuildTargetGroup(BuildPipeline.GetBuildTargetGroup(target)).TargetName;
            int[] levels = QualitySettings.GetActiveQualityLevelsForPlatform(targetName);
            if (levels.Length == 0)
            {
                result.Add(new Mismatch { Description = platform + ": no active quality level", Expected = expected });
                continue;
            }

            foreach (int level in levels)
            {
                string quality = level >= 0 && level < QualitySettings.names.Length
                    ? QualitySettings.names[level] : "quality " + level;
                RenderPipelineAsset pipeline = QualitySettings.GetRenderPipelineAssetAt(level) ??
                                               GraphicsSettings.defaultRenderPipeline;
                var urp = pipeline as UniversalRenderPipelineAsset;
                string label = platform + " / " + quality + ": ";
                if (urp == null)
                {
                    result.Add(new Mismatch
                    {
                        Description = label + (pipeline == null ? "no render pipeline asset" :
                            pipeline.name + " is not a URP asset") + " (suggested " + ModeName(expected) + ")",
                        AssetToOpen = pipeline,
                        Expected = expected
                    });
                    continue;
                }

                var serialized = new SerializedObject(urp);
                var dataList = serialized.FindProperty("m_RendererDataList");
                var defaultIndex = serialized.FindProperty("m_DefaultRendererIndex");
                if (dataList == null || defaultIndex == null || defaultIndex.intValue < 0 ||
                    defaultIndex.intValue >= dataList.arraySize)
                {
                    result.Add(new Mismatch
                    {
                        Description = label + urp.name + " has no valid default renderer",
                        AssetToOpen = urp,
                        Expected = expected
                    });
                    continue;
                }

                var data = dataList.GetArrayElementAtIndex(defaultIndex.intValue).objectReferenceValue;
                if (!(data is UniversalRendererData renderer))
                {
                    result.Add(new Mismatch
                    {
                        Description = label + urp.name + " does not use a Universal Renderer",
                        AssetToOpen = data != null ? data : urp,
                        Expected = expected
                    });
                    continue;
                }
                if (expectedByRenderer.TryGetValue(renderer, out var priorMode) && priorMode != expected)
                    sharedRendererConflict = true;
                else
                    expectedByRenderer[renderer] = expected;
                if (renderer.renderingMode != expected)
                {
                    result.Add(new Mismatch
                    {
                        Description = label + renderer.name + " is " + ModeName(renderer.renderingMode) +
                                      " (suggested " + ModeName(expected) + ")",
                        AssetToOpen = renderer,
                        Renderer = renderer,
                        Expected = expected
                    });
                }
            }
        }
        return result;
    }

    private static string ModeName(RenderingMode mode) => mode == RenderingMode.ForwardPlus ? "Forward+" : mode.ToString();

    private static void OpenFirstMismatch(Mismatch mismatch)
    {
        if (mismatch.AssetToOpen != null)
        {
            Selection.activeObject = mismatch.AssetToOpen;
            EditorGUIUtility.PingObject(mismatch.AssetToOpen);
        }
        else
        {
            SettingsService.OpenProjectSettings("Project/Quality");
        }
    }
}
