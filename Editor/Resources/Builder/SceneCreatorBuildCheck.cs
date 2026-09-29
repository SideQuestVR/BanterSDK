using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.VisualScripting;

namespace BS.SDKEditor
{
    // Diagnostics only: do not rewrite tags or disable competing spawners.
    internal static class SceneCreatorBuildCheck
    {
        static readonly string[] BuiltInTags =
            { "Untagged", "Respawn", "Finish", "EditorOnly", "MainCamera", "Player", "GameController" };
        static readonly HashSet<string> SpawnGraphs = new HashSet<string>
            { "16a69254c2a6956ccba2d04b4152b34b", "a15a3dea7129c4ba4c627436cb50fc62" };

        internal static List<string> CollectIssues(Scene scene)
        {
            // Read the SDK's current list each time, including future list expansions.
            var allowed = new HashSet<string>(BuiltInTags.Concat(InitialiseOnLoad.tagsToAdd.Values));
            var unsupported = new Dictionary<string, List<string>>();
            var spawners = new List<string>();
            foreach (var root in scene.GetRootGameObjects()) Inspect(root.transform, root.name);
            var issues = unsupported.OrderBy(pair => pair.Key).Select(pair =>
                "Unsupported custom tag '" + pair.Key + "' used by " + pair.Value.Count +
                " object(s): " + string.Join(", ", pair.Value.Take(3)) +
                ". The client may not define this tag. Tags are left unchanged.").ToList();
            if (spawners.Count > 1)
                issues.Add("Multiple active Creator Conveniences startup spawners: " + string.Join(", ", spawners.Take(5)) +
                    ". Each can teleport the local player on load; the last event wins. Enable only the intended startup spawner. Nothing was disabled.");
            return issues;

            void Inspect(Transform transform, string path)
            {
                var obj = transform.gameObject;
                if (obj.CompareTag("EditorOnly")) return; // Unity excludes the whole subtree from builds.
                if (!allowed.Contains(obj.tag))
                {
                    if (!unsupported.TryGetValue(obj.tag, out var objects))
                        unsupported.Add(obj.tag, objects = new List<string>());
                    objects.Add(path);
                }
                if (obj.activeInHierarchy)
                {
                    foreach (var machine in obj.GetComponents<ScriptMachine>())
                        if (machine.enabled && machine.nest.source == GraphSource.Macro && machine.nest.macro != null &&
                            SpawnGraphs.Contains(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(machine.nest.macro))))
                            spawners.Add(path);
                }
                foreach (Transform child in transform) Inspect(child, path + "/" + child.name);
            }
        }

        internal static bool ConfirmBeforeSceneBuild(string scenePath, Action<string> report)
        {
            Scene preview = default;
            List<string> issues;
            try
            {
                // Inspect the actual scene selected in the builder, not an unrelated open scene.
                preview = EditorSceneManager.OpenPreviewScene(scenePath);
                issues = CollectIssues(preview);
            }
            catch (Exception error)
            {
                string message = "Could not inspect scene tags/spawners: " + error.Message;
                Debug.LogError(message);
                report?.Invoke(message);
                return false;
            }
            finally
            {
                if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
            }
            if (issues.Count == 0) return true;
            string warning = string.Join("\n\n", issues);
            Debug.LogWarning("[Creator SDK] " + warning);
            report?.Invoke(warning);
            return Application.isBatchMode || EditorUtility.DisplayDialog("Scene build warnings", warning +
                "\n\nBuild anyway without changing the scene?", "Build anyway", "Cancel");
        }
    }
}
