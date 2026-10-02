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
    /// folder. Nothing here changes the project on load any more: the Setup panel's checklist
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
            File.WriteAllText(WebRootIndexPath, BSStarterUpper.STARTER_PAGE);
            AssetDatabase.ImportAsset(WebRootIndexPath);
            return true;
        }

        // -- Layers and tags -----------------------------------------------------

        /// <summary>What setting up the SDK's layers and tags would change in a TagManager.</summary>
        public sealed class LayerTagPlan
        {
            /// <summary>A layer slot to name or clear, with the name it has now ("" when it's unused).</summary>
            public struct LayerChange
            {
                public int Index;
                public string Current;
                /// <summary>The SDK's name for the slot, or "" to clear a layer the SDK doesn't define.</summary>
                public string Wanted;
            }

            public readonly List<LayerChange> Layers = new List<LayerChange>();
            /// <summary>SDK tags the project doesn't have, in the SDK's order.</summary>
            public readonly List<string> MissingTags = new List<string>();
            /// <summary>Tags the SDK doesn't define, which setting up removes.</summary>
            public readonly List<string> ExtraTags = new List<string>();
            /// <summary>The whole tag list after setting up: the SDK's tags in the client's order.</summary>
            public readonly List<string> WantedTags = new List<string>();
            /// <summary>Whether the tag list differs from <see cref="WantedTags"/> at all, order included.</summary>
            public bool TagsChange;

            public bool IsEmpty => Layers.Count == 0 && !TagsChange;

            /// <summary>Tags that are all there but in the wrong slots.</summary>
            public bool TagsOutOfOrder => TagsChange && MissingTags.Count == 0 && ExtraTags.Count == 0;

            /// <summary>Layer slots that already carry another name, which setting them up renames.</summary>
            public IEnumerable<LayerChange> Renames =>
                Layers.Where(change => !string.IsNullOrEmpty(change.Current) && !string.IsNullOrEmpty(change.Wanted));

            /// <summary>Layers the SDK doesn't define, which setting up clears.</summary>
            public IEnumerable<LayerChange> Removals => Layers.Where(change => string.IsNullOrEmpty(change.Wanted));
        }

        /// <summary>Unity's own layer slots, which can't be renamed.</summary>
        static readonly HashSet<int> BuiltInLayerSlots = new HashSet<int> { 0, 1, 2, 4, 5 };

        /// <summary>
        /// Whether setting up removes layers and tags the SDK doesn't define. Not in the client, which owns
        /// layers 25 and up (and the SDK's list is its own list there anyway).
        /// </summary>
        public static bool RemovesExtras
        {
            get
            {
#if GREENFIELD_PROJECT
                return false;
#else
                return true;
#endif
            }
        }

        /// <summary>
        /// A built space stores both an object's layer and its tag as a number: the layer's slot, and the tag's
        /// position in the tag list. The client reads those numbers against its own lists, so every SDK layer
        /// has to be in its slot and the tags have to be the SDK's, in the SDK's order. Whatever a layer slot is
        /// called now gets renamed; with <paramref name="removeExtras"/>, layers and tags the SDK doesn't
        /// define are removed (otherwise extra tags are kept after the SDK's).
        /// </summary>
        public static LayerTagPlan PlanLayersAndTags(IReadOnlyList<string> layers, IReadOnlyList<string> tags, bool removeExtras = true)
        {
            var plan = new LayerTagPlan();
            for (var slot = 0; slot < 32; slot++)
            {
                if (BuiltInLayerSlots.Contains(slot))
                    continue;
                var current = slot < layers.Count ? layers[slot] ?? "" : "";
                if (layersToAdd.TryGetValue(slot, out var wanted))
                {
                    if (current != wanted)
                        plan.Layers.Add(new LayerTagPlan.LayerChange { Index = slot, Current = current, Wanted = wanted });
                }
                else if (removeExtras && current != "")
                {
                    plan.Layers.Add(new LayerTagPlan.LayerChange { Index = slot, Current = current, Wanted = "" });
                }
            }

            var sdkTags = tagsToAdd.OrderBy(pair => pair.Key).Select(pair => pair.Value).ToList();
            var existing = tags.Where(tag => !string.IsNullOrEmpty(tag)).ToList();
            plan.MissingTags.AddRange(sdkTags.Where(tag => !existing.Contains(tag)));
            var extras = existing.Where(tag => !sdkTags.Contains(tag)).Distinct().ToList();
            plan.WantedTags.AddRange(sdkTags);
            if (removeExtras)
                plan.ExtraTags.AddRange(extras);
            else
                plan.WantedTags.AddRange(extras);
            plan.TagsChange = !tags.Select(tag => tag ?? "").SequenceEqual(plan.WantedTags);
            return plan;
        }

        /// <summary>What setting up the layers and tags would change in this project. Changes nothing.</summary>
        public static LayerTagPlan PlanLayersAndTags()
        {
            var tagManager = LoadTagManager();
            if (tagManager == null)
                return new LayerTagPlan();
            return PlanLayersAndTags(ReadStrings(tagManager.FindProperty("layers")), ReadStrings(tagManager.FindProperty("tags")), RemovesExtras);
        }

        /// <summary>The SDK layers and tags this project doesn't have. Changes nothing.</summary>
        public static void GetMissingLayersAndTags(out List<string> missingLayers, out List<string> missingTags)
        {
            var plan = PlanLayersAndTags();
            missingLayers = plan.Layers.Where(change => change.Wanted != "").Select(change => change.Wanted).ToList();
            missingTags = plan.MissingTags.ToList();
        }

        /// <summary>
        /// Names the SDK's layer slots, clears the layers it doesn't define and makes the tag list exactly the
        /// SDK's. Returns whether anything changed.
        /// </summary>
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
            var plan = PlanLayersAndTags(ReadStrings(layers), ReadStrings(tags), RemovesExtras);
            if (plan.IsEmpty)
                return false;

            foreach (var change in plan.Layers)
            {
                if (change.Index >= layers.arraySize)
                    layers.arraySize = change.Index + 1;
                layers.GetArrayElementAtIndex(change.Index).stringValue = change.Wanted;
            }
            if (plan.TagsChange)
            {
                tags.arraySize = plan.WantedTags.Count;
                for (var i = 0; i < plan.WantedTags.Count; i++)
                    tags.GetArrayElementAtIndex(i).stringValue = plan.WantedTags[i];
            }
            tagManager.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssetIfDirty(tagManager.targetObject);

            foreach (var rename in plan.Renames)
                Debug.Log($"[Creator SDK] Layer {rename.Index} was \"{rename.Current}\"; spaces use it as \"{rename.Wanted}\".");
            foreach (var removal in plan.Removals)
                Debug.Log($"[Creator SDK] Removed layer {removal.Index} \"{removal.Current}\": the SDK doesn't use that slot.");
            if (plan.ExtraTags.Count > 0)
                Debug.Log("[Creator SDK] Removed tags the SDK doesn't have: " + string.Join(", ", plan.ExtraTags) +
                          ". If one comes back, an object still uses it; the Builder's checklist lists them.");
            Debug.Log($"[Creator SDK] Set up {plan.Layers.Count} layer(s); the tag list is now the SDK's {plan.WantedTags.Count}.");
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
            { 17, "UserLayer13" },
            { 18, "UserLayer14" },
            { 19, "UserLayer15" },
            { 20, "Grabbable" },
            { 21, "Invisible" },
            { 22, "Menu" },
            { 23, "CharacterColliders" },
            { 24, "CharacterHandColliders" },
            // 25 and up are the client's own: setting up clears them in creator projects.
        };

        /// <summary>
        /// The client's tags, in the client's order: a built space stores a tag as its position in this list.
        /// UserTag1-32 are the creator's; the BSLocalCharacter tags mark the local player's body, hands, head and
        /// feet so triggers, portals and teleporters can tell it apart.
        /// </summary>
        public static Dictionary<int, string> tagsToAdd = new Dictionary<int, string> {
            { 0,  "UserTag1" },
            { 1,  "UserTag2" },
            { 2,  "UserTag3" },
            { 3,  "UserTag4" },
            { 4,  "UserTag5" },
            { 5,  "UserTag6" },
            { 6,  "UserTag7" },
            { 7,  "UserTag8" },
            { 8,  "UserTag9" },
            { 9,  "UserTag10" },
            { 10, "UserTag11" },
            { 11, "UserTag12" },
            { 12, "UserTag13" },
            { 13, "UserTag14" },
            { 14, "UserTag15" },
            { 15, "UserTag16" },
            { 16, "UserTag17" },
            { 17, "UserTag18" },
            { 18, "UserTag19" },
            { 19, "UserTag20" },
            { 20, "UserTag21" },
            { 21, "UserTag22" },
            { 22, "UserTag23" },
            { 23, "UserTag24" },
            { 24, "UserTag25" },
            { 25, "UserTag26" },
            { 26, "UserTag27" },
            { 27, "UserTag28" },
            { 28, "UserTag29" },
            { 29, "UserTag30" },
            { 30, "UserTag31" },
            { 31, "UserTag32" },
            { 32, "BSLocalCharacter" },
            { 33, "BSLocalCharacterLeftHand" },
            { 34, "BSLocalCharacterRightHand" },
            { 35, "BSLocalCharacterHead" },
            { 36, "BSLocalCharacterFeet" },
        };
    }
}
