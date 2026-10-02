using System.Collections.Generic;
using System.Linq;
using UnityEditor;
#if !GREENFIELD_PROJECT
using BS.SDKEditor.Setup;
#endif

namespace BS.SDKEditor.BuildChecks
{
    /// <summary>The client only runs Visual Scripting nodes on its allow-list.</summary>
    sealed class VisualScriptingCheck : BuildCheck
    {
        public override string Id => "project.visual-scripting";
        // Not "Visual Scripting nodes": that's the Setup panel's node-library item.
        public override string Title => "Visual Scripting node support";
        public override string Description =>
            "Visual Scripting graphs only use nodes the client can run: graph assets anywhere in the project, graphs on prefabs' root " +
            "objects, and every graph in this scene.";
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

#if !GREENFIELD_PROJECT
    /// <summary>
    /// The Setup panel's required items as one row, so the project setup is listed there and not twice. Missing build
    /// support can't be built past; anything else is a warning the creator can build past.
    /// </summary>
    sealed class ProjectSetupBuildCheck : BuildCheck
    {
        public override string Id => "project.setup";
        public override string Title => "Project setup";
        public override string Description => "Everything Creator SDK > Setup marks Required is done, such as build support, the SDK's layers and tags, and URP.";
        public override int Order => 0;

        public override void Run(BuildCheckContext context, List<BuildCheckIssue> issues)
        {
            var open = ProjectSetup.Checks
                .Where(check => check.Importance == SetupImportance.Required)
                // A build creates a missing page itself, so don't ask to build past it. Re-check and Upload still list it.
                .Where(check => !(context.ForBuild && check.Id == "sdk.webroot"))
                .Select(check => (check, status: ProjectSetup.Evaluate(check)))
                .Where(item => item.status.NeedsAttention)
                .ToList();

            var buildSupport = open.FirstOrDefault(item => item.check.Id == "sdk.build-support");
            if (buildSupport.check != null)
            {
                issues.Add(new BuildCheckIssue(Id, BuildCheckSeverity.Error, buildSupport.status.Summary,
                    "Spaces are built for Android (Quest) and Windows together. " + buildSupport.status.Details)
                {
                    Overridable = false,
                });
                open.Remove(buildSupport);
            }
            if (open.Count == 0)
                return;

            var title = open.Count == 1 ? $"{open[0].check.Title}: {open[0].status.Summary}" : $"{open.Count} setup items need attention";
            issues.Add(new BuildCheckIssue(Id, BuildCheckSeverity.Warning, title,
                    string.Join("\n", open.Select(item => $"• {item.check.Title}: {item.status.Summary}")) +
                    "\nThe Setup panel explains each one and fixes it.")
                .WithFix("Open Setup", _ =>
                {
                    SdkSetupWindow.Open();
                    return false;
                }, needsLoadedScene: false, interactive: true));
        }
    }
#else
    // Greenfield has no Setup panel, so its builds keep checking these directly.

    /// <summary>Every platform a space ships to needs its build module installed.</summary>
    sealed class BuildModulesCheck : BuildCheck
    {
        public override string Id => "project.build-modules";
        public override string Title => "Build modules";
        public override string Description => "Unity has the Android and Windows build modules a space is built with.";
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

    /// <summary>SDK Play Mode needs both input systems; the built space doesn't care.</summary>
    sealed class ActiveInputHandlingBuildCheck : BuildCheck
    {
        public override string Id => "project.input-handling";
        public override string Title => "Active Input Handling";
        public override string Description => "Active Input Handling is Both, which SDK Play mode needs for its controls.";
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
                }, needsLoadedScene: false, interactive: true));
        }
    }

    /// <summary>Spaces are tuned for Forward on Android and Forward+ on Windows.</summary>
    sealed class UrpRendererCheck : BuildCheck
    {
        public override string Id => "project.urp-renderer";
        public override string Title => "URP renderer";
        public override string Description => "The URP renderers use Forward on Android and Forward+ on Windows, as the client does.";
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
#endif
}
