using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace BS.SDKEditor.BuildChecks
{
    /// <summary>Every platform a space ships to needs its build module installed.</summary>
    sealed class BuildModulesCheck : BuildCheck
    {
        public override string Id => "project.build-modules";
        public override string Title => "Build modules";
        public override int Order => 0;

        public override void Run(BuildCheckContext context, List<BuildCheckIssue> issues)
        {
            foreach (var target in context.Targets)
            {
                if (BuildPipeline.IsBuildTargetSupported(BuildPipeline.GetBuildTargetGroup(target), target))
                    continue;
                var platform = target == BuildTarget.Android ? "Android" : "Windows";
                issues.Add(new BuildCheckIssue(Id, BuildCheckSeverity.Error,
                    $"The {platform} build module isn't installed",
                    $"Spaces are built for Android (Quest) and Windows together. In Unity Hub, add \"{platform} Build Support\" " +
                    "to this Unity version (Installs > the version's menu > Add modules), then restart Unity.")
                {
                    Overridable = false,
                });
            }
        }
    }

    /// <summary>The client only runs Visual Scripting nodes on its allow-list.</summary>
    sealed class VisualScriptingCheck : BuildCheck
    {
        public override string Id => "project.visual-scripting";
        public override string Title => "Visual Scripting nodes";
        public override int Order => 10;
        // Not knowing is as bad as knowing they're there: the space would break in the client.
        public override bool BlockOnException => true;

        public override void Run(BuildCheckContext context, List<BuildCheckIssue> issues)
        {
            var disallowed = ValidateVisualScripting.CollectDisallowedElements(context.Roots, refresh: context.ForBuild);
            if (disallowed.Count == 0)
                return;
            var listed = string.Join("\n", disallowed.Take(15).Select(id => "• " + id));
            if (disallowed.Count > 15)
                listed += $"\n• ...and {disallowed.Count - 15} more";
            issues.Add(new BuildCheckIssue(Id, BuildCheckSeverity.Error,
                $"{disallowed.Count} Visual Scripting node type(s) aren't allowed in spaces",
                "The client won't run graphs that use these. Remove or replace them; graphs anywhere in the project " +
                "count, not just this scene's.\n" + listed)
            {
                Overridable = false,
            });
        }
    }

    /// <summary>SDK Play Mode needs both input systems; the built space doesn't care.</summary>
    sealed class ActiveInputHandlingBuildCheck : BuildCheck
    {
        public override string Id => "project.input-handling";
        public override string Title => "Active Input Handling";
        public override int Order => 20;

        public override void Run(BuildCheckContext context, List<BuildCheckIssue> issues)
        {
            var current = ActiveInputHandlingCheck.Current;
            if (current == ActiveInputHandlingCheck.InputHandling.Both)
                return;
            issues.Add(new BuildCheckIssue(Id, BuildCheckSeverity.Warning,
                    $"Active Input Handling is \"{ActiveInputHandlingCheck.Label(current)}\", not \"Both\"",
                    "SDK Play Mode needs \"Both\" for the fly camera, mouse grabbing, UI clicks and browser typing. " +
                    "It's only needed to test here: it doesn't change the built space, and changing it doesn't affect the client.")
                .WithFix("Set to Both (restarts Unity)", _ =>
                {
                    // Asks first, then saves scenes and restarts.
                    ActiveInputHandlingCheck.PromptFromMenu();
                    return false;
                }, needsLoadedScene: false));
        }
    }

#if !GREENFIELD_PROJECT
    /// <summary>Spaces depend on the SDK's layers (Grabbable, UI, Menu...) and tags.</summary>
    sealed class LayersAndTagsCheck : BuildCheck
    {
        public override string Id => "project.layers-tags";
        public override string Title => "SDK layers and tags";
        public override int Order => 30;

        public override void Run(BuildCheckContext context, List<BuildCheckIssue> issues)
        {
            InitialiseOnLoad.GetMissingLayersAndTags(out var layers, out var tags);
            if (layers.Count == 0 && tags.Count == 0)
                return;
            var details = "Spaces rely on them, e.g. layer 20 (Grabbable) for grabbing and the __BA_LocalPlayer tag for teleporters.";
            if (layers.Count > 0)
                details += "\nMissing layers: " + string.Join(", ", layers);
            if (tags.Count > 0)
                details += "\nMissing tags: " + string.Join(", ", tags);
            issues.Add(new BuildCheckIssue(Id, BuildCheckSeverity.Warning, "The SDK's layers and tags aren't all set up", details)
                .WithFix("Set up layers and tags", _ =>
                {
                    InitialiseOnLoad.SetupLayersAndTags();
                    return true;
                }, needsLoadedScene: false));
        }
    }
#endif

    /// <summary>Spaces are tuned for Forward on Android and Forward+ on Windows.</summary>
    sealed class UrpRendererCheck : BuildCheck
    {
        public override string Id => "project.urp-renderer";
        public override string Title => "URP renderer";
        public override int Order => 40;

        public override void Run(BuildCheckContext context, List<BuildCheckIssue> issues)
        {
            var selected = context.Targets.Select(_ => true).ToArray();
            var mismatches = UrpRendererBuildCheck.FindMismatches(context.Targets, selected, out var sharedRendererConflict);
            if (mismatches.Count == 0)
                return;

            var details = "Suggested rendering modes: Windows Forward+, Android Forward.\n" +
                          string.Join("\n", mismatches.Select(mismatch => "• " + mismatch.Description));
            var canChange = UrpRendererBuildCheck.TryPlanChange(mismatches, sharedRendererConflict, out var plan);
            if (!canChange)
                details += "\nSome of these assets are missing, read-only, or shared by both platforms; review them by hand.";

            var issue = new BuildCheckIssue(Id, BuildCheckSeverity.Warning, "The URP renderers aren't set up the way spaces expect", details)
                .WithTargets(mismatches.Select(mismatch => mismatch.AssetToOpen).Where(asset => asset != null).Distinct()
                    .Select(BuildCheckTarget.ForAsset));
            if (canChange)
            {
                issue.WithFix("Set rendering modes", _ =>
                {
                    UrpRendererBuildCheck.ApplyModes(plan);
                    return true;
                }, needsLoadedScene: false);
            }
            issues.Add(issue);
        }
    }
}
