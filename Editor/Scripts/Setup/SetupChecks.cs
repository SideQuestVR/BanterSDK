using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

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
            "Scenes store layers by number, so each of the SDK's layers has to be in the slot the client uses (Grabbable is 20, NetworkPlayer 17...). " +
            "Its tags are how teleporters, portals and triggers recognise the player.";
        public override string WithoutIt =>
            "Grab handles that aren't on the client's Grabbable layer can't be grabbed, layer masks don't line up with the client's, " +
            "and in Play mode portals and teleporters don't see the player.";
        public override string FixChanges =>
            "Names layers 3 and 6 to 31 the way the client does, renaming any slot that has another name (the item's details list them). " +
            "Adds the SDK's tags after your own; existing tags are kept.";

        public override SetupStatus Evaluate()
        {
            var plan = InitialiseOnLoad.PlanLayersAndTags();
            if (plan.IsEmpty)
                return SetupStatus.Done("All of the SDK's layers and tags are set up.");

            var parts = new List<string>();
            if (plan.Layers.Count > 0)
                parts.Add(Count(plan.Layers.Count, "layer"));
            if (plan.MissingTags.Count > 0)
                parts.Add(Count(plan.MissingTags.Count, "tag"));

            var details = new StringBuilder();
            var renames = plan.Renames.ToList();
            foreach (var rename in renames.Take(8))
                details.AppendLine($"Layer {rename.Index} \"{rename.Current}\" becomes \"{rename.Wanted}\".");
            if (renames.Count > 8)
                details.AppendLine($"...and {renames.Count - 8} more renamed layers.");
            if (plan.MissingTags.Count > 0)
                details.AppendLine("Adds tags: " + string.Join(", ", plan.MissingTags.Take(6)) +
                                   (plan.MissingTags.Count > 6 ? $" and {plan.MissingTags.Count - 6} more." : "."));
            return SetupStatus.NeedsFix(string.Join(" and ", parts) + " to set up", details.ToString().TrimEnd());
        }

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
        public override string WithoutIt => "The Builder has no page to upload, and Play mode creates a bare one the first time you press Play.";
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
        public override SetupImportance Importance => SetupImportance.Recommended;
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
            ProjectSetup.NotifyChanged();
        }
    }

    sealed class UrpSetupCheck : SetupCheck
    {
        static readonly BuildTarget[] Targets = { BuildTarget.Android, BuildTarget.StandaloneWindows };

        public override string Id => "sdk.urp";
        public override string Title => "Universal Render Pipeline";
        public override SetupImportance Importance => SetupImportance.Recommended;
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

    sealed class ColorSpaceSetupCheck : SetupCheck
    {
        public override string Id => "sdk.color-space";
        public override string Title => "Linear color space";
        public override SetupImportance Importance => SetupImportance.Recommended;
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

        public override bool Fix()
        {
            VsNodeGeneration.SetVSTypesAndAssemblies();
            return true;
        }
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
