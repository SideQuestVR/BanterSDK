using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BS.SDKEditor
{
    // Diagnostics only: do not rewrite tags.
    internal static class SceneCreatorBuildCheck
    {
        static readonly string[] BuiltInTags =
            { "Untagged", "Respawn", "Finish", "EditorOnly", "MainCamera", "Player", "GameController" };

        /// <summary>A custom tag the client may not define, and the objects that use it.</summary>
        internal sealed class TagUse
        {
            public string Tag;
            public readonly List<GameObject> Objects = new List<GameObject>();
            public readonly List<string> Paths = new List<string>();
        }

        internal static List<TagUse> CollectUnsupportedTags(Scene scene)
        {
            // Read the SDK's current list each time, including future list expansions.
            var allowed = new HashSet<string>(BuiltInTags.Concat(InitialiseOnLoad.tagsToAdd.Values));
            var unsupported = new Dictionary<string, TagUse>();
            foreach (var root in scene.GetRootGameObjects()) Inspect(root.transform, root.name);
            return unsupported.OrderBy(pair => pair.Key).Select(pair => pair.Value).ToList();

            void Inspect(Transform transform, string path)
            {
                var obj = transform.gameObject;
                if (obj.CompareTag("EditorOnly")) return; // Unity excludes the whole subtree from builds.
                if (!allowed.Contains(obj.tag))
                {
                    if (!unsupported.TryGetValue(obj.tag, out var use))
                        unsupported.Add(obj.tag, use = new TagUse { Tag = obj.tag });
                    use.Objects.Add(obj);
                    use.Paths.Add(path);
                }
                foreach (Transform child in transform) Inspect(child, path + "/" + child.name);
            }
        }

        internal static string Describe(TagUse use) =>
            "Unsupported custom tag '" + use.Tag + "' used by " + use.Paths.Count +
            " object(s): " + string.Join(", ", use.Paths.Take(3)) +
            ". The client may not define this tag. Tags are left unchanged.";

        internal static List<string> CollectIssues(Scene scene) => CollectUnsupportedTags(scene).Select(Describe).ToList();

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
                string message = "Could not inspect scene tags: " + error.Message;
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
