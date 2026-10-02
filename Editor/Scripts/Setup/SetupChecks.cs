using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace BS.SDKEditor.Setup
{
    // The SDK's own setup checklist items. Other editor assemblies can add theirs by deriving SetupCheck.

    /// <summary>Spaces are built for Quest and Windows together, so both build modules have to be installed.</summary>
    sealed class BuildSupportSetupCheck : SetupCheck
    {
        public override string Id => "sdk.build-support";
        public override string Title => "Android and Windows build support";
        public override int Order => 5;
        public override string Why => "Every space is built for Quest (Android) and Windows at the same time.";
        public override string WithoutIt => "The Builder can't build or upload your space.";
        public override string ManualActionLabel => "How to add modules";

        public override SetupStatus Evaluate()
        {
            var missing = new List<string>();
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
                missing.Add("Android Build Support");
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows))
                missing.Add("Windows Build Support");
            if (missing.Count == 0)
                return SetupStatus.Done("Both are installed.");
            return SetupStatus.NeedsManualFix(string.Join(" and ", missing) + (missing.Count == 1 ? " isn't" : " aren't") + " installed",
                $"Add it in Unity Hub: Installs, then this version's menu (Unity {Application.unityVersion}) > Add modules. Restart Unity afterwards.");
        }

        public override bool Fix() => false;

        public override void ManualAction() => Application.OpenURL("https://docs.unity3d.com/hub/manual/AddModules.html");
    }

    sealed class LayersAndTagsSetupCheck : SetupCheck
    {
        public override string Id => "sdk.layers-tags";
        public override string Title => "SDK layers and tags";
        public override int Order => 10;
        public override string FixLabel => "Set up";
        public override string Why =>
            "A built space stores layers and tags as numbers: the layer's slot, and the tag's position in the tag list. So each SDK layer " +
            "has to be in the slot the client uses (Grabbable is 20, Menu 22...), the tags have to be the client's in the client's order, " +
            "and nothing else can be in the lists. The BSLocalCharacter tags are how teleporters, portals and triggers recognise the player.";
        public override string WithoutIt =>
            "Grab handles that aren't on the client's Grabbable layer can't be grabbed, layer masks don't line up with the client's, " +
            "objects end up with a different tag in the client, and in Play mode portals and teleporters don't see the player.";
        public override string FixChanges =>
            "Names layers 3 and 6 to 24 the way the client does, renaming any slot that has another name, and removes every other layer " +
            "name (25 and up belong to the client). Replaces the tag list with the SDK's, in the client's order, removing any other tags " +
            "(the item's details list them). Objects still using a removed tag or layer are listed by the Builder's checklist.";

        public override SetupStatus Evaluate()
        {
            var plan = InitialiseOnLoad.PlanLayersAndTags();
            if (plan.IsEmpty)
                return SetupStatus.Done("All of the SDK's layers and tags are set up, and nothing else.");

            var parts = new List<string>();
            var removals = plan.Removals.ToList();
            var layerFixes = plan.Layers.Count - removals.Count;
            if (layerFixes > 0)
                parts.Add(Count(layerFixes, "layer") + " to set up");
            if (removals.Count > 0)
                parts.Add(Count(removals.Count, "extra layer") + " to remove");
            if (plan.MissingTags.Count > 0)
                parts.Add(Count(plan.MissingTags.Count, "tag") + " to add");
            if (plan.ExtraTags.Count > 0)
                parts.Add(Count(plan.ExtraTags.Count, "extra tag") + " to remove");
            if (plan.TagsOutOfOrder)
                parts.Add("tags to put in order");

            var details = new StringBuilder();
            var renames = plan.Renames.ToList();
            foreach (var rename in renames.Take(8))
                details.AppendLine($"Layer {rename.Index} \"{rename.Current}\" becomes \"{rename.Wanted}\".");
            if (renames.Count > 8)
                details.AppendLine($"...and {renames.Count - 8} more renamed layers.");
            if (removals.Count > 0)
                details.AppendLine("Removes layers: " + string.Join(", ", removals.Select(removal => $"{removal.Index} \"{removal.Current}\"")) + ".");
            if (plan.MissingTags.Count > 0)
                details.AppendLine("Adds tags: " + List(plan.MissingTags));
            if (plan.ExtraTags.Count > 0)
                details.AppendLine("Removes tags: " + List(plan.ExtraTags));
            if (plan.TagsChange)
                details.AppendLine("Puts the tags in the client's order: UserTag1-32, then the BSLocalCharacter tags.");
            return SetupStatus.NeedsFix(Capitalise(string.Join(", ", parts)), details.ToString().TrimEnd());
        }

        static string List(List<string> items) =>
            string.Join(", ", items.Take(6)) + (items.Count > 6 ? $" and {items.Count - 6} more." : ".");

        static string Capitalise(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);

        public override bool Fix() => InitialiseOnLoad.SetupLayersAndTags();

        static string Count(int count, string noun) => count + " " + noun + (count == 1 ? "" : "s");
    }

    sealed class ApiCompatibilitySetupCheck : SetupCheck
    {
        public override string Id => "sdk.api-compatibility";
        public override string Title => "API compatibility level";
        public override int Order => 20;
        public override string FixLabel => "Use .NET Standard";
        public override string Why => "The SDK, and the Basis packages it builds on, target .NET Standard 2.1, as the client does.";
        public override string WithoutIt => "On .NET Framework, the SDK's Basis packages don't compile or run as they're meant to.";
        public override string FixChanges => "Sets Player Settings > API Compatibility Level to .NET Standard 2.1 for Windows and Android. Unity then recompiles scripts.";

        public override SetupStatus Evaluate()
        {
            var wrong = InitialiseOnLoad.GetWrongApiCompatibilityTargets();
            if (wrong.Count == 0)
                return SetupStatus.Done(".NET Standard 2.1 for Windows and Android.");
            return SetupStatus.NeedsFix(string.Join(" and ", wrong) + (wrong.Count == 1 ? " doesn't" : " don't") + " use .NET Standard 2.1");
        }

        public override bool Fix() => InitialiseOnLoad.SetApiCompatibilityLevel();
    }

    sealed class WebRootSetupCheck : SetupCheck
    {
        public override string Id => "sdk.webroot";
        public override string Title => "Space page (WebRoot)";
        public override int Order => 30;
        public override string FixLabel => "Create";
        public override string Why =>
            "Assets/WebRoot/index.html is your space's web page: Play mode serves it to the SDK's browser, " +
            "and the Builder builds the space into the same folder and uploads it from there.";
        public override string WithoutIt =>
            "Your space has no page to add scripts and content to. A world uploaded without its page doesn't load, so building and " +
            "pressing Play also create this starter page when there's none.";
        public override string FixChanges => "Creates Assets/WebRoot/index.html with a minimal page. An existing page is never changed.";

        public override SetupStatus Evaluate() =>
            InitialiseOnLoad.WebRootExists
                ? SetupStatus.Done(InitialiseOnLoad.WebRootIndexPath)
                : SetupStatus.NeedsFix(InitialiseOnLoad.WebRootIndexPath + " doesn't exist");

        public override bool Fix() => InitialiseOnLoad.CreateWebRoot();
    }

    sealed class TextMeshProSetupCheck : SetupCheck
    {
        public override string Id => "sdk.textmeshpro";
        public override string Title => "TextMesh Pro essentials";
        public override int Order => 40;
        public override string FixLabel => "Import";
        public override string Why => "BS Text, and any other TextMesh Pro text, needs TextMesh Pro's default font and settings in the project.";
        public override string WithoutIt =>
            "Text has no font to draw with, and Unity stops to show its TMP Importer window the first time text appears, " +
            "often in the middle of Play mode.";
        public override string FixChanges => "Imports TextMesh Pro's Essential Resources into Assets/TextMesh Pro. Nothing else changes.";

        // The import finishes after Fix returns.
        static bool s_Importing;

        public override SetupStatus Evaluate()
        {
            if (AssetDatabase.FindAssets("t:" + nameof(TMPro.TMP_Settings)).Length > 0)
                return SetupStatus.Done("Imported.");
            return s_Importing ? SetupStatus.Working("Importing...") : SetupStatus.NeedsFix("The Essential Resources aren't imported");
        }

        public override bool Fix()
        {
            AssetDatabase.importPackageCompleted -= ImportEnded;
            AssetDatabase.importPackageCompleted += ImportEnded;
            AssetDatabase.importPackageFailed -= ImportFailed;
            AssetDatabase.importPackageFailed += ImportFailed;
            AssetDatabase.importPackageCancelled -= ImportEnded;
            AssetDatabase.importPackageCancelled += ImportEnded;
            s_Importing = true;
            TMPro.TMP_PackageResourceImporter.ImportResources(importEssentials: true, importExamples: false, interactive: false);
            return true;
        }

        static void ImportFailed(string package, string error) => ImportEnded(package);

        static void ImportEnded(string package)
        {
            AssetDatabase.importPackageCompleted -= ImportEnded;
            AssetDatabase.importPackageFailed -= ImportFailed;
            AssetDatabase.importPackageCancelled -= ImportEnded;
            s_Importing = false;
            // As TMP's own importer window does. Text that woke up before the import is waiting for this event, and
            // stays blank without it until the scene reloads.
            if (package == "TMP Essential Resources" && AssetDatabase.FindAssets("t:" + nameof(TMPro.TMP_Settings)).Length > 0)
            {
                TMPro.TMPro_EventManager.ON_RESOURCES_LOADED();
                SettingsService.NotifySettingsProviderChanged();
            }
            ProjectSetup.NotifyChanged();
        }
    }

    sealed class UrpSetupCheck : SetupCheck
    {
        static readonly BuildTarget[] Targets = { BuildTarget.Android, BuildTarget.StandaloneWindows };

        public override string Id => "sdk.urp";
        public override string Title => "Universal Render Pipeline";
        public override int Order => 50;
        public override string FixLabel => "Set rendering modes";
        public override string ManualActionLabel => "Open Graphics settings";
        public override string Why =>
            "The client renders with the Universal Render Pipeline: Forward on Quest (Android) and Forward+ on Windows. " +
            "Matching it here shows your space the way players will see it.";
        public override string WithoutIt =>
            "Without URP, materials made for the built-in pipeline render pink in the client. With other rendering modes, " +
            "lights look different here than in the client (Forward limits the lights per object; Forward+ doesn't).";
        public override string FixChanges =>
            "Sets the rendering mode on the Universal Renderer assets your Android and Windows quality levels use. " +
            "Switching a project to URP isn't done here: use Project Settings > Graphics and Unity's Render Pipeline Converter.";

        public override SetupStatus Evaluate()
        {
            var mismatches = UrpRendererBuildCheck.FindMismatches(Targets, Targets.Select(_ => true).ToArray(), out var sharedRendererConflict);
            if (mismatches.Count == 0)
                return SetupStatus.Done("Forward on Android, Forward+ on Windows.");
            var details = string.Join("\n", mismatches.Select(mismatch => "• " + mismatch.Description));
            if (mismatches.Any(mismatch => mismatch.Renderer == null))
                return SetupStatus.NeedsManualFix("This project doesn't render with URP everywhere", details);
            if (!UrpRendererBuildCheck.TryPlanChange(mismatches, sharedRendererConflict, out _))
                return SetupStatus.NeedsManualFix("The renderer modes differ, and these assets can't be changed here",
                    details + "\nThey're read-only, in a package, or one renderer is shared by both platforms.");
            return SetupStatus.NeedsFix("The renderer modes differ from the client's", details);
        }

        public override bool Fix()
        {
            var mismatches = UrpRendererBuildCheck.FindMismatches(Targets, Targets.Select(_ => true).ToArray(), out var sharedRendererConflict);
            if (mismatches.Count == 0 || !UrpRendererBuildCheck.TryPlanChange(mismatches, sharedRendererConflict, out var plan))
                return false;
            UrpRendererBuildCheck.ApplyModes(plan);
            return true;
        }

        public override void ManualAction() => SettingsService.OpenProjectSettings("Project/Graphics");
    }

    /// <summary>
    /// A space carries shaders only for the graphics APIs its project builds for: Android on Auto (Vulkan, then OpenGL ES 3,
    /// which Quest runs) and Windows on the list the client is built with.
    /// </summary>
    sealed class GraphicsApiSetupCheck : SetupCheck
    {
        static readonly GraphicsDeviceType[] WindowsApis = { GraphicsDeviceType.Direct3D11, GraphicsDeviceType.Direct3D12, GraphicsDeviceType.Vulkan };
        // Both Windows targets share one setting; both are set so neither can disagree.
        static readonly BuildTarget[] WindowsTargets = { BuildTarget.StandaloneWindows64, BuildTarget.StandaloneWindows };

        public override string Id => "sdk.graphics-apis";
        public override string Title => "Graphics APIs";
        public override int Order => 52;
        public override string FixLabel => "Set graphics APIs";
        public override string Why =>
            "A space only carries shaders for the graphics APIs your project builds for. Quest runs Vulkan, with OpenGL ES 3 as a fallback, " +
            "which is what Auto Graphics API picks for Android. The Windows client is built for Direct3D 11, Direct3D 12 and Vulkan.";
        public override string WithoutIt =>
            "Without Vulkan shaders a space renders pink on Quest. An Android list of your own works while it keeps Vulkan; Auto keeps it to " +
            "what Unity picks for Quest. On Windows, materials render pink whenever the client runs on a graphics API the space has no shaders for.";
        public override string FixChanges =>
            "Turns on Auto Graphics API for Android and sets Windows to Direct3D 11, Direct3D 12 and Vulkan, in Player Settings > Other Settings. " +
            "Builds take a little longer, since shaders are compiled for each API. On the Windows build target the editor itself uses " +
            "Direct3D 11 from its next start.";

        public override SetupStatus Evaluate()
        {
            var problems = new List<string>();
            var androidAuto = PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.Android);
            var androidVulkan = PlayerSettings.GetGraphicsAPIs(BuildTarget.Android).Contains(GraphicsDeviceType.Vulkan);
            if (!androidAuto)
                problems.Add(androidVulkan ? "Android has a list of its own, not Auto Graphics API" : "Android has no Vulkan, and isn't on Auto Graphics API");
            if (WindowsTargets.Any(NeedsClientList))
                problems.Add("Windows doesn't use the client's list");

            var current = "Android: " + Describe(BuildTarget.Android) + ". Windows: " + Describe(BuildTarget.StandaloneWindows64) + ".";
            if (problems.Count > 0)
                return SetupStatus.NeedsFix(string.Join("; ", problems), "Now: " + current);
            // Can't happen on Unity 6000.3, whose Auto list for Android is Vulkan then OpenGL ES 3. This item's fix only
            // turns Auto on, so a Unity whose Auto list leaves Vulkan out needs a list made by hand.
            if (!androidVulkan)
                return SetupStatus.NeedsManualFix("Unity's Auto Graphics API for Android has no Vulkan",
                    "Quest needs Vulkan shaders. In Player Settings > Other Settings, turn off Auto Graphics API for Android and put Vulkan " +
                    "first. Now: " + current);
            return SetupStatus.Done(current);
        }

        public override bool Fix()
        {
            var changed = false;
            if (!PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.Android))
            {
                PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, true);
                changed = true;
            }
            // Only a Windows list Evaluate rejected: one that already has the client's three, in any order or with
            // more, is left as it is.
            foreach (var target in WindowsTargets.Where(NeedsClientList))
            {
                PlayerSettings.SetUseDefaultGraphicsAPIs(target, false);
                PlayerSettings.SetGraphicsAPIs(target, WindowsApis);
                changed = true;
            }
            return changed;
        }

        static bool NeedsClientList(BuildTarget target) =>
            PlayerSettings.GetUseDefaultGraphicsAPIs(target) || WindowsApis.Any(api => !PlayerSettings.GetGraphicsAPIs(target).Contains(api));

        static string Describe(BuildTarget target) =>
            (PlayerSettings.GetUseDefaultGraphicsAPIs(target) ? "Auto (" : "") +
            string.Join(", ", PlayerSettings.GetGraphicsAPIs(target).Select(Name)) +
            (PlayerSettings.GetUseDefaultGraphicsAPIs(target) ? ")" : "");

        static string Name(GraphicsDeviceType api)
        {
            switch (api)
            {
                case GraphicsDeviceType.OpenGLES3: return "OpenGL ES 3";
                case GraphicsDeviceType.Direct3D11: return "Direct3D 11";
                case GraphicsDeviceType.Direct3D12: return "Direct3D 12";
                default: return api.ToString();
            }
        }
    }

    sealed class ColorSpaceSetupCheck : SetupCheck
    {
        public override string Id => "sdk.color-space";
        public override string Title => "Linear color space";
        public override int Order => 55;
        public override string FixLabel => "Use Linear";
        public override string Why => "The client renders in Linear color space.";
        public override string WithoutIt =>
            "In Gamma, colors and lighting look different here than in the client, and lighting you bake comes out too bright or too dark there.";
        public override string FixChanges =>
            "Sets Player Settings > Color Space to Linear. Unity updates the assets that depend on it, which can take a while in a big project; " +
            "bake lighting again afterwards.";

        public override SetupStatus Evaluate() =>
            PlayerSettings.colorSpace == ColorSpace.Linear
                ? SetupStatus.Done("Linear.")
                : SetupStatus.NeedsFix("The project uses " + PlayerSettings.colorSpace + " color space");

        public override bool Fix()
        {
            if (PlayerSettings.colorSpace == ColorSpace.Linear)
                return false;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            return true;
        }
    }

    sealed class VisualScriptingSetupCheck : SetupCheck
    {
        public override string Id => "sdk.visual-scripting";
        public override string Title => "Visual Scripting nodes";
        public override int Order => 60;
        public override string FixLabel => "Generate nodes";
        public override string Why =>
            "Visual Scripting only offers nodes for the assemblies and types in its node library. The SDK's list adds its components " +
            "(BS Text, BS Portal...) and its own nodes, and keeps to the Unity APIs the client can run.";
        public override string WithoutIt =>
            "BS components have no nodes in the fuzzy finder, so graphs can't read or change them, and nodes added by an SDK update don't appear.";
        public override string FixChanges =>
            "Replaces the node library in Project Settings > Visual Scripting with the SDK's list and rebuilds the node database, " +
            "which takes a minute or so. The first time, Visual Scripting also runs its own initial build.";

        public override SetupStatus Evaluate()
        {
            if (!VsNodeGeneration.TryGetMissingOptions(out var assemblies, out var types))
                return SetupStatus.NeedsFix("Visual Scripting isn't set up in this project yet");
            if (assemblies.Count > 0 || types.Count > 0)
            {
                var details = new StringBuilder();
                if (assemblies.Count > 0)
                    details.AppendLine("Missing assemblies: " + string.Join(", ", assemblies));
                if (types.Count > 0)
                    details.AppendLine("Missing types: " + string.Join(", ", types.Take(8).Select(type => type.Name)) +
                                       (types.Count > 8 ? $" and {types.Count - 8} more" : ""));
                return SetupStatus.NeedsFix("The node library is missing parts of the SDK's list", details.ToString().TrimEnd());
            }
            if (!VsNodeGeneration.NodeDatabaseExists)
                return SetupStatus.NeedsFix("The node database hasn't been built yet");
            if (!VsNodeGeneration.NodesBuiltForThisVersion)
            {
                var built = VsNodeGeneration.NodesBuiltForVersion;
                var current = PackageManagerUtility.currentVersion;
                return SetupStatus.NeedsFix(built == null ? "The nodes haven't been generated for this SDK yet"
                    : built == current ? "The SDK's node list changed since the nodes were generated"
                    : $"The nodes were generated for SDK {built}, not {current}");
            }
            return SetupStatus.Done("BS components and the SDK's nodes are in the fuzzy finder.");
        }

        public override bool Fix() => VsNodeGeneration.SetVSTypesAndAssemblies();
    }

    sealed class InputHandlingSetupCheck : SetupCheck
    {
        public override string Id => "sdk.input-handling";
        public override string Title => "Active Input Handling";
        // Last: it's the one fix that needs a restart.
        public override int Order => 90;
        public override string FixLabel => "Set to Both";
        public override bool FixNeedsRestart => true;
        public override string Why =>
            "SDK Play mode uses both of Unity's input systems: the Input System package for the fly camera, mouse grabbing and UI clicks, " +
            "and the old Input Manager for typing into in-world browsers.";
        public override string WithoutIt =>
            "With only the new Input System, typing into browsers throws errors; with only the old Input Manager, the fly camera, grabbing " +
            "and UI clicks get no input. It only affects testing here, not the built space.";
        public override string FixChanges => "Sets Player Settings > Active Input Handling to Both. Unity has to restart before it takes effect.";

        public override SetupStatus Evaluate()
        {
            var current = ActiveInputHandlingCheck.Current;
            if (current == ActiveInputHandlingCheck.InputHandling.Both)
                return SetupStatus.Done("Both.");
            if (current == ActiveInputHandlingCheck.InputHandling.Unknown)
                return SetupStatus.NeedsManualFix("Couldn't read it from Player Settings",
                    "Set it to Both under Edit > Project Settings > Player > Other Settings, then restart Unity.");
            return SetupStatus.NeedsFix($"It's \"{ActiveInputHandlingCheck.Label(current)}\"");
        }

        public override bool Fix() => ActiveInputHandlingCheck.ApplyBoth();
    }
}
