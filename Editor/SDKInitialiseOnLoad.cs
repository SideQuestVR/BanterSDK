using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace BS.SDKEditor
{
    /// <summary>
    /// The project settings the SDK needs: its layers and tags, the API compatibility level and the WebRoot
    /// folder. Nothing here changes the project on load any more: the Welcome window's setup checklist
    /// (<see cref="Setup.ProjectSetup"/>) shows what's missing and fixes it when the creator asks.
    /// </summary>
    [InitializeOnLoad]
    public static class InitialiseOnLoad
    {
        static InitialiseOnLoad()
        {
#if !GREENFIELD_PROJECT
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
#endif
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
#if !GREENFIELD_PROJECT
            // BSStarterUpper.SpawnOnPlay only exists outside the Greenfield client (it is subscribed
            // above under the same guard).
            if (change == PlayModeStateChange.ExitingEditMode)
            {
                // Only decide here. Creating it now would put it in the edit scene, which Unity saves as
                // the state to restore when play stops, so it would still be there afterwards.
                // BSStarterUpper.BeforeEditorPlay creates it once play mode has started.
                BSStarterUpper.SpawnOnPlay = Object.FindObjectOfType<BSStarterUpper>() == null;
            }
#endif
        }

        // -- API compatibility level ---------------------------------------------

        /// <summary>The platforms spaces are built for.</summary>
        static readonly NamedBuildTarget[] SdkTargets = { NamedBuildTarget.Standalone, NamedBuildTarget.Android };

        /// <summary>
        /// The SDK platforms ("Windows", "Android") whose API compatibility level isn't .NET Standard. Upstream
        /// Basis targets .NET Standard 2.1; the old Banter fork ran on NET_Unity_4_8, which breaks the upstream packages.
        /// </summary>
        public static List<string> GetWrongApiCompatibilityTargets() =>
            SdkTargets.Where(target => PlayerSettings.GetApiCompatibilityLevel(target) != ApiCompatibilityLevel.NET_Standard)
                .Select(target => target == NamedBuildTarget.Standalone ? "Windows" : target.TargetName)
                .ToList();

        /// <summary>Sets .NET Standard on the SDK targets. Returns whether anything changed (scripts then recompile).</summary>
        public static bool SetApiCompatibilityLevel()
        {
            var changed = false;
            foreach (var target in SdkTargets)
            {
                if (PlayerSettings.GetApiCompatibilityLevel(target) == ApiCompatibilityLevel.NET_Standard)
                    continue;
                PlayerSettings.SetApiCompatibilityLevel(target, ApiCompatibilityLevel.NET_Standard);
                changed = true;
            }
            return changed;
        }

        // -- WebRoot -------------------------------------------------------------

        public static string WebRootIndexPath => "Assets/" + BSStarterUpper.WEB_ROOT + "/index.html";

        /// <summary>Whether Assets/WebRoot/index.html, the space's page, exists.</summary>
        public static bool WebRootExists => File.Exists(WebRootIndexPath);

        /// <summary>Creates Assets/WebRoot with a starter index.html. Never overwrites one. Returns whether it created it.</summary>
        public static bool CreateWebRoot()
        {
            if (WebRootExists)
                return false;
            Directory.CreateDirectory(Path.GetDirectoryName(WebRootIndexPath));
            // world-asset: the space loads its one combined bundle (asset.world) from next to this page.
            File.WriteAllText(WebRootIndexPath,
                "<html world-asset>\n<head>\n  <meta charset=\"utf-8\">\n  <title>Space</title>\n</head>\n<body>\n</body>\n</html>\n");
            AssetDatabase.ImportAsset(WebRootIndexPath);
            return true;
        }

        // -- Layers and tags -----------------------------------------------------

        /// <summary>What setting up the SDK's layers and tags would change in a TagManager.</summary>
        public sealed class LayerTagPlan
        {
            /// <summary>A layer slot to name, with the name it has now ("" when it's unused).</summary>
            public struct LayerChange
            {
                public int Index;
                public string Current;
                public string Wanted;
            }

            public readonly List<LayerChange> Layers = new List<LayerChange>();
            /// <summary>SDK tags the project doesn't have, in the SDK's order.</summary>
            public readonly List<string> MissingTags = new List<string>();

            public bool IsEmpty => Layers.Count == 0 && MissingTags.Count == 0;

            /// <summary>Layer slots that already carry another name, which setting them up renames.</summary>
            public IEnumerable<LayerChange> Renames => Layers.Where(change => !string.IsNullOrEmpty(change.Current));
        }

        /// <summary>
        /// Layers go by number in a built space, so each SDK layer has to be in its own slot; whatever a slot
        /// is called now gets renamed. Tags go by name (a GameObject stores its tag as text), so a missing
        /// tag is added after the project's own tags and never replaces one.
        /// </summary>
        public static LayerTagPlan PlanLayersAndTags(IReadOnlyList<string> layers, IReadOnlyList<string> tags)
        {
            var plan = new LayerTagPlan();
            foreach (var layer in layersToAdd.OrderBy(pair => pair.Key))
            {
                var current = layer.Key < layers.Count ? layers[layer.Key] ?? "" : "";
                if (current != layer.Value)
                    plan.Layers.Add(new LayerTagPlan.LayerChange { Index = layer.Key, Current = current, Wanted = layer.Value });
            }
            var existingTags = new HashSet<string>(tags.Where(tag => !string.IsNullOrEmpty(tag)));
            foreach (var tag in tagsToAdd.OrderBy(pair => pair.Key))
            {
                if (!existingTags.Contains(tag.Value))
                    plan.MissingTags.Add(tag.Value);
            }
            return plan;
        }

        /// <summary>What setting up the layers and tags would change in this project. Changes nothing.</summary>
        public static LayerTagPlan PlanLayersAndTags()
        {
            var tagManager = LoadTagManager();
            if (tagManager == null)
                return new LayerTagPlan();
            return PlanLayersAndTags(ReadStrings(tagManager.FindProperty("layers")), ReadStrings(tagManager.FindProperty("tags")));
        }

        /// <summary>The SDK layers and tags this project doesn't have. Changes nothing.</summary>
        public static void GetMissingLayersAndTags(out List<string> missingLayers, out List<string> missingTags)
        {
            var plan = PlanLayersAndTags();
            missingLayers = plan.Layers.Select(change => change.Wanted).ToList();
            missingTags = plan.MissingTags.ToList();
        }

        /// <summary>Names the SDK's layer slots and adds its missing tags. Returns whether anything changed.</summary>
        public static bool SetupLayersAndTags()
        {
            var tagManager = LoadTagManager();
            if (tagManager == null)
            {
                Debug.LogError("[Creator SDK] Couldn't load ProjectSettings/TagManager.asset, so the SDK's layers and tags weren't set up.");
                return false;
            }
            var layers = tagManager.FindProperty("layers");
            var tags = tagManager.FindProperty("tags");
            var plan = PlanLayersAndTags(ReadStrings(layers), ReadStrings(tags));
            if (plan.IsEmpty)
                return false;

            foreach (var change in plan.Layers)
            {
                if (change.Index >= layers.arraySize)
                    layers.arraySize = change.Index + 1;
                layers.GetArrayElementAtIndex(change.Index).stringValue = change.Wanted;
            }
            foreach (var tag in plan.MissingTags)
            {
                tags.arraySize++;
                tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = tag;
            }
            tagManager.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssetIfDirty(tagManager.targetObject);

            foreach (var rename in plan.Renames)
                Debug.Log($"[Creator SDK] Layer {rename.Index} was \"{rename.Current}\"; spaces use it as \"{rename.Wanted}\".");
            Debug.Log($"[Creator SDK] Set up {plan.Layers.Count} layer(s) and added {plan.MissingTags.Count} tag(s).");
            return true;
        }

        static SerializedObject LoadTagManager()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            return assets == null || assets.Length == 0 || assets[0] == null ? null : new SerializedObject(assets[0]);
        }

        static List<string> ReadStrings(SerializedProperty array)
        {
            var values = new List<string>();
            if (array == null || !array.isArray)
                return values;
            for (var i = 0; i < array.arraySize; i++)
                values.Add(array.GetArrayElementAtIndex(i).stringValue);
            return values;
        }

        public static Dictionary<int, string> layersToAdd = new Dictionary<int, string> {
            { 3, "UserLayer1" },
            { 6, "UserLayer2" },
            { 7, "UserLayer3" },
            { 8, "UserLayer4" },
            { 9, "UserLayer5" },
            { 10, "UserLayer6" },
            { 11, "UserLayer7" },
            { 12, "UserLayer8" },
            { 13, "UserLayer9" },
            { 14, "UserLayer10" },
            { 15, "UserLayer11" },
            { 16, "UserLayer12" },
            { 17, "NetworkPlayer" },
            { 18, "RPMAvatarHead" },
            { 19, "RPMAvatarBody" },
            { 20, "Grabbable" },
            { 21, "HandColliders" },
            { 22, "Menu" },
            { 23, "PhysicsPlayer" },
            { 24, "BanterInternal1_DONTUSE" },
            { 25, "BanterInternal2_DONTUSE" },
            { 26, "BanterInternal3_DONTUSE" },
            { 27, "BanterInternal4_DONTUSE" },
            { 28, "BanterInternal5_DONTUSE" },
            { 29, "BanterInternal6_DONTUSE" },
            { 30, "BanterInternal7_DONTUSE" },
            { 31, "BanterInternal8_DONTUSE" }
        };

        public static Dictionary<int, string> tagsToAdd = new Dictionary<int, string> {
            { 0,  "__BA_NameTag" },
            { 1,  "__BA_NameTagMenu" },
            { 2,  "__BA_FootRig" },
            { 3,  "__BA_PlayerHead" },
            { 4,  "__BA_UNUSED0" },
            { 5,  "__BA_UNUSED1" },
            { 6,  "__BA_TriggerIndex" },
            { 7,  "__BA_PlayerTorso" },
            { 8,  "__BA_PlayerLegs" },
            { 9,  "__BA_LocalPlayer" },
            { 10, "__BA_PlayerLeftHand" },
            { 11, "__BA_PlayerRightHand" },
            { 12, "__BA_LocalPlayerFeet" },
            { 13, "__BA_UserTag0" },
            { 14, "__BA_UserTag1" },
            { 15, "__BA_UserTag2" },
            { 16, "__BA_UserTag3" },
            { 17, "__BA_UserTag4" },
            { 18, "__BA_UserTag5" },
            { 19, "__BA_UserTag6" },
            { 20, "__BA_UserTag7" },
            { 21, "__BA_UserTag8" },
            { 22, "__BA_UserTag9" },
            { 23, "__BA_UserTag10" },
            { 24, "__BA_UserTag11" },
            { 25, "__BA_UserTag12" },
            { 26, "__BA_UserTag13" },
            { 27, "__BA_UserTag14" },
            { 28, "MenuWorldSpace" },
            { 29, "VRPlayerContextMenu" },
            { 30, "PortalBall" },
        };
    }
}
