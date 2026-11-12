using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BS.SDKEditor
{
    // Diagnostics only: nothing here rewrites tags or layers. The checklist's fixes do, when clicked.
    internal static class SceneCreatorBuildCheck
    {
        static readonly string[] BuiltInTags =
            { "Untagged", "Respawn", "Finish", "EditorOnly", "MainCamera", "Player", "GameController" };

        /// <summary>Unity's own layer slots.</summary>
        static readonly int[] BuiltInLayers = { 0, 1, 2, 4, 5 };

        /// <summary>A custom tag the client may not define, and the objects that use it.</summary>
        internal sealed class TagUse
        {
            public string Tag;
            public readonly List<GameObject> Objects = new List<GameObject>();
            public readonly List<string> Paths = new List<string>();
        }

        /// <summary>A layer slot the SDK doesn't set up, and the objects on it.</summary>
        internal sealed class LayerUse
        {
            public int Layer;
            public readonly List<GameObject> Objects = new List<GameObject>();
        }

        /// <summary>
        /// The SDK tag an old Banter tag became (__BA_UserTag0 is UserTag1, __BA_LocalPlayer is BSLocalCharacter...),
        /// or Untagged for any other tag.
        /// </summary>
        internal static string ReplacementTag(string tag)
        {
            switch (tag)
            {
                case "__BA_LocalPlayer": return "BSLocalCharacter";
                case "__BA_PlayerLeftHand": return "BSLocalCharacterLeftHand";
                case "__BA_PlayerRightHand": return "BSLocalCharacterRightHand";
                case "__BA_PlayerHead": return "BSLocalCharacterHead";
                case "__BA_LocalPlayerFeet": return "BSLocalCharacterFeet";
            }
            const string oldUserTag = "__BA_UserTag";
            if (tag.StartsWith(oldUserTag, StringComparison.Ordinal) &&
                int.TryParse(tag.Substring(oldUserTag.Length), out var index) && index >= 0 && index < 32)
                return "UserTag" + (index + 1);
            return "Untagged";
        }

        internal static List<LayerUse> CollectUnsupportedLayers(Scene scene)
        {
            var allowed = new HashSet<int>(BuiltInLayers.Concat(InitialiseOnLoad.layersToAdd.Keys));
            var unsupported = new Dictionary<int, LayerUse>();
            foreach (var root in scene.GetRootGameObjects()) Inspect(root.transform);
            return unsupported.OrderBy(pair => pair.Key).Select(pair => pair.Value).ToList();

            void Inspect(Transform transform)
            {
                var obj = transform.gameObject;
                if (obj.CompareTag("EditorOnly")) return;
                if (!allowed.Contains(obj.layer))
                {
                    if (!unsupported.TryGetValue(obj.layer, out var use))
                        unsupported.Add(obj.layer, use = new LayerUse { Layer = obj.layer });
                    use.Objects.Add(obj);
                }
                foreach (Transform child in transform) Inspect(child);
            }
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
