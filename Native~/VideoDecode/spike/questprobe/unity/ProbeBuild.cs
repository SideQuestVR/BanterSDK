// Batch build for the probe project: marks libvprobe.so as a preloaded arm64 plugin, makes an empty scene,
// gives the app its own package id and debug signing, logs the XR configuration and builds Build/vprobe.apk.
// Unity -batchmode -buildTarget Android -projectPath <proj> -executeMethod ProbeBuild.Build -logFile <log>
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEngine;
using UnityEngine.XR.OpenXR;

public static class ProbeBuild
{
    const string Plugin = "Assets/Plugins/Android/libvprobe.so";
    const string Scene = "Assets/Probe.unity";

    public static void Build()
    {
        try
        {
            AssetDatabase.ImportAsset(Plugin, ImportAssetOptions.ForceUpdate);
            var imp = (PluginImporter)AssetImporter.GetAtPath(Plugin);
            imp.SetCompatibleWithAnyPlatform(false);
            imp.SetCompatibleWithEditor(false);
            imp.SetCompatibleWithPlatform(BuildTarget.Android, true);
            imp.SetPlatformData(BuildTarget.Android, "CPU", "ARM64");
            imp.isPreloaded = true;
            imp.SaveAndReimport();

            if (!File.Exists(Scene))
            {
                var s = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                EditorSceneManager.SaveScene(s, Scene);
            }

            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.sidequest.vprobe");
            PlayerSettings.productName = "VProbe";
            PlayerSettings.Android.useCustomKeystore = false;
            PlayerSettings.Android.bundleVersionCode = 1;
            EditorUserBuildSettings.buildAppBundle = false;

            Debug.Log($"[ProbeBuild] graphics APIs {string.Join(",", PlayerSettings.GetGraphicsAPIs(BuildTarget.Android))}, " +
                      $"arch {PlayerSettings.Android.targetArchitectures}, backend {PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android)}, " +
                      $"min SDK {PlayerSettings.Android.minSdkVersion}, colour space {PlayerSettings.colorSpace}, " +
                      $"plugin preloaded {imp.isPreloaded}");
            var gs = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
            Debug.Log($"[ProbeBuild] XR init on start {gs?.InitManagerOnStart}, loaders " +
                      string.Join(",", gs?.Manager?.activeLoaders?.Select(l => l ? l.name : "null") ?? Array.Empty<string>()));
            var ox = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            if (ox != null)
            {
                Debug.Log($"[ProbeBuild] OpenXR render mode {ox.renderMode}");
                foreach (var f in ox.GetFeatures())
                    if (f && f.enabled) Debug.Log($"[ProbeBuild] OpenXR feature on: {f.GetType().FullName} ({f.name})");
            }

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { Scene },
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                locationPathName = "Build/vprobe.apk",
                options = BuildOptions.None,
            });
            Debug.Log($"[ProbeBuild] RESULT {report.summary.result}, {report.summary.totalSize / 1048576f:F1} MB, {report.summary.totalTime}");
            EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }
        catch (Exception e)
        {
            Debug.LogError("[ProbeBuild] FAILED " + e);
            EditorApplication.Exit(1);
        }
    }
}
