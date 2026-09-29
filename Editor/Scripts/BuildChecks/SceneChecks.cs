using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;

namespace BS.SDKEditor.BuildChecks
{
    /// <summary>Convex hulls on floors and walls leave players colliding with the wrong surface.</summary>
    sealed class ConvexCollidersCheck : BuildCheck
    {
        public override string Id => "scene.convex-colliders";
        public override string Title => "Convex mesh colliders";
        public override int Order => 50;

        public override void Run(BuildCheckContext context, List<BuildCheckIssue> issues)
        {
            var findings = new List<ConvexColliderValidation.Finding>();
            ConvexColliderValidation.FindInto(context.Roots.Where(root => !root.CompareTag("EditorOnly")), null, findings);
            if (findings.Count == 0)
                return;
            issues.Add(new BuildCheckIssue(Id, BuildCheckSeverity.Error,
                    $"{findings.Count} mesh collider(s) have Convex ticked on large static geometry",
                    ConvexColliderValidation.Explanation)
                .WithTargets(findings.Select(finding => context.Target(finding.Collider.gameObject)))
                .WithFix("Untick Convex", issue =>
                {
                    var current = new List<ConvexColliderValidation.Finding>();
                    ConvexColliderValidation.FindInto(issue.Resolve<GameObject>(), null, current);
                    current = current.GroupBy(finding => finding.Collider).Select(group => group.First()).ToList();
                    if (current.Count == 0)
                        return false;
                    ConvexColliderValidation.Untick(current);
                    return true;
                }));
        }
    }

    /// <summary>Tags outside the SDK's list may not exist in the client.</summary>
    sealed class UnsupportedTagsCheck : BuildCheck
    {
        public override string Id => "scene.tags";
        public override string Title => "Tags";
        public override int Order => 60;

        public override void Run(BuildCheckContext context, List<BuildCheckIssue> issues)
        {
            foreach (var use in SceneCreatorBuildCheck.CollectUnsupportedTags(context.Scene))
            {
                issues.Add(new BuildCheckIssue(Id, BuildCheckSeverity.Warning,
                        $"Custom tag '{use.Tag}' is used by {use.Objects.Count} object(s)",
                        "The client may not define this tag, so scripts comparing against it may not work. " +
                        "Use one of the SDK's tags instead. Tags are left unchanged.")
                    .WithTargets(use.Objects.Select(context.Target)));
            }
        }
    }

    /// <summary>Scripts that are gone do nothing, and each logs a warning when the space loads.</summary>
    sealed class MissingScriptsCheck : BuildCheck
    {
        public override string Id => "scene.missing-scripts";
        public override string Title => "Missing scripts";
        public override int Order => 70;

        public override void Run(BuildCheckContext context, List<BuildCheckIssue> issues)
        {
            var broken = context.AllObjects()
                .Where(go => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go) > 0)
                .ToList();
            if (broken.Count == 0)
                return;
            issues.Add(new BuildCheckIssue(Id, BuildCheckSeverity.Warning,
                    $"{broken.Count} object(s) have missing scripts",
                    "Their scripts were deleted or aren't in this project. They do nothing in the space, and Unity " +
                    "warns about each one when it loads.")
                .WithTargets(broken.Select(context.Target))
                .WithFix("Remove missing scripts", issue =>
                {
                    var removed = 0;
                    foreach (var go in issue.Resolve<GameObject>())
                    {
                        Undo.RegisterCompleteObjectUndo(go, "Remove missing scripts");
                        removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
                    }
                    return removed > 0;
                }));
        }
    }

    /// <summary>Visual Scripting machines whose graph asset is gone, like the old convenience graphs.</summary>
    sealed class MissingGraphsCheck : BuildCheck
    {
        public override string Id => "scene.missing-graphs";
        public override string Title => "Visual Scripting graphs";
        public override int Order => 75;

        public override void Run(BuildCheckContext context, List<BuildCheckIssue> issues)
        {
            var machines = context.Components<ScriptMachine>()
                .Where(machine => machine.nest != null && machine.nest.source == GraphSource.Macro && machine.nest.macro == null)
                .Cast<Component>()
                .Concat(context.Components<StateMachine>()
                    .Where(machine => machine.nest != null && machine.nest.source == GraphSource.Macro && machine.nest.macro == null))
                .ToList();
            if (machines.Count == 0)
                return;
            issues.Add(new BuildCheckIssue(Id, BuildCheckSeverity.Warning,
                    $"{machines.Count} Visual Scripting machine(s) have no graph",
                    "Their graph asset is missing (deleted, or not in this project), so they do nothing. The old Creator " +
                    "Conveniences graphs (Spawn Point, Spawn Range, Seat, Local Space Teleporter) were replaced by components: " +
                    "swap those objects for the ones in GameObject > BS > Player.")
                .WithTargets(context.TargetsOf(machines)));
        }
    }

    /// <summary>The player brings the camera and the audio listener.</summary>
    sealed class CamerasAndListenersCheck : BuildCheck
    {
        public override string Id => "scene.cameras-listeners";
        public override string Title => "Cameras and audio listeners";
        public override int Order => 80;

        public override void Run(BuildCheckContext context, List<BuildCheckIssue> issues)
        {
            var cameras = context.Components<Camera>()
                .Where(camera => camera.enabled && camera.gameObject.activeInHierarchy && camera.targetTexture == null)
                .ToList();
            if (cameras.Count > 0)
            {
                issues.Add(new BuildCheckIssue(Id, BuildCheckSeverity.Warning,
                        $"{cameras.Count} camera(s) in the scene render to the screen",
                        "The player brings their own camera. A scene camera that renders to the screen draws the space a second " +
                        "time and can cover the player's view. Give it a Render Texture (for mirrors and screens) or disable it. " +
                        "SDK Play Mode hides this by switching scene cameras off.")
                    .WithTargets(context.TargetsOf(cameras))
                    .WithFix("Disable these cameras", issue =>
                    {
                        var changed = false;
                        foreach (var camera in issue.Resolve<GameObject>().SelectMany(go => go.GetComponents<Camera>()))
                        {
                            if (!camera.enabled || camera.targetTexture != null)
                                continue;
                            Undo.RecordObject(camera, "Disable camera");
                            camera.enabled = false;
                            changed = true;
                        }
                        return changed;
                    }));
            }

            var listeners = context.Components<AudioListener>().ToList();
            if (listeners.Count > 0)
            {
                issues.Add(new BuildCheckIssue(Id, BuildCheckSeverity.Warning,
                        $"{listeners.Count} Audio Listener(s) in the scene",
                        "The player brings their own listener, and the client removes the scene's. With two, Unity warns and " +
                        "sound may be heard from the wrong place.")
                    .WithTargets(context.TargetsOf(listeners))
                    .WithFix("Remove these Audio Listeners", issue =>
                    {
                        var removed = false;
                        foreach (var listener in issue.Resolve<GameObject>().SelectMany(go => go.GetComponents<AudioListener>()).ToList())
                        {
                            Undo.DestroyObjectImmediate(listener);
                            removed = true;
                        }
                        return removed;
                    }));
            }
        }
    }

    /// <summary>Materials that will render pink: empty slots, broken shaders, Built-in pipeline shaders.</summary>
    sealed class MaterialsCheck : BuildCheck
    {
        public override string Id => "render.materials";
        public override string Title => "Materials";
        public override int Order => 90;

        public override void Run(BuildCheckContext context, List<BuildCheckIssue> issues)
        {
            var emptySlots = new List<GameObject>();
            var builtIn = new Problem();
            var broken = new Problem();

            foreach (var renderer in context.Components<Renderer>())
            {
                // A particle renderer's second slot is its optional trail material.
                var materials = renderer is ParticleSystemRenderer ? new[] { renderer.sharedMaterial } : renderer.sharedMaterials;
                foreach (var material in materials)
                {
                    if (material == null)
                    {
                        if (!emptySlots.Contains(renderer.gameObject))
                            emptySlots.Add(renderer.gameObject);
                        continue;
                    }
                    var shader = material.shader;
                    if (shader == null || shader.name == "Hidden/InternalErrorShader" || ShaderUtil.ShaderHasError(shader))
                        broken.Add(material, renderer.gameObject, shader != null ? shader.name : "(none)");
                    else if (IsBuiltInPipelineShader(shader.name))
                        builtIn.Add(material, renderer.gameObject, shader.name);
                }
            }

            if (emptySlots.Count > 0)
            {
                issues.Add(new BuildCheckIssue(Id, BuildCheckSeverity.Warning,
                        $"{emptySlots.Count} renderer(s) have an empty material slot",
                        "Empty slots render pink, or not at all.")
                    .WithTargets(emptySlots.Select(context.Target)));
            }
            if (broken.Materials.Count > 0)
            {
                issues.Add(new BuildCheckIssue(Id, BuildCheckSeverity.Warning,
                        $"{broken.Materials.Count} material(s) use a shader that is missing or has errors",
                        "They render pink. Shaders: " + string.Join(", ", broken.Shaders))
                    .WithTargets(broken.Targets(context)));
            }
            if (builtIn.Materials.Count > 0)
            {
                issues.Add(new BuildCheckIssue(Id, BuildCheckSeverity.Warning,
                        $"{builtIn.Materials.Count} material(s) use Built-in pipeline shaders",
                        "Spaces render with URP, where these shaders show up pink: " + string.Join(", ", builtIn.Shaders) +
                        ". Switch them to a URP shader such as Universal Render Pipeline/Lit, or select them and use " +
                        "Edit > Rendering > Materials > Convert Selected Built-in Materials to URP.")
                    .WithTargets(builtIn.Targets(context)));
            }
        }

        internal static bool IsBuiltInPipelineShader(string name) =>
            name == "Standard" || name == "Standard (Specular setup)" || name == "Autodesk Interactive"
            || name.StartsWith("Legacy Shaders/") || name.StartsWith("Mobile/") || name.StartsWith("Nature/")
            || name.StartsWith("Particles/Standard");

        sealed class Problem
        {
            public readonly List<Material> Materials = new List<Material>();
            public readonly List<GameObject> Instances = new List<GameObject>();
            public readonly SortedSet<string> Shaders = new SortedSet<string>();

            public void Add(Material material, GameObject user, string shaderName)
            {
                Shaders.Add(shaderName);
                if (Materials.Contains(material))
                    return;
                Materials.Add(material);
                // Materials made at edit time and saved in the scene have no asset to point at.
                if (!AssetDatabase.Contains(material))
                    Instances.Add(user);
            }

            public IEnumerable<BuildCheckTarget> Targets(BuildCheckContext context) =>
                Materials.Where(AssetDatabase.Contains).Select(material => BuildCheckTarget.ForAsset(material))
                    .Concat(Instances.Select(context.Target));
        }
    }

    /// <summary>A rough size check against what a Quest can load comfortably.</summary>
    sealed class SceneBudgetCheck : BuildCheck
    {
        // Rough, whole-space budgets for Quest. Tune here.
        internal const long TriangleBudget = 1_000_000;
        internal const long TextureMemoryBudget = 1024L * 1024 * 1024;

        public override string Id => "scene.budget";
        public override string Title => "Scene size";
        public override int Order => 200;

        public override void Run(BuildCheckContext context, List<BuildCheckIssue> issues)
        {
            if (string.IsNullOrEmpty(context.ScenePath))
                return;
            var stats = SceneQuickStats.Compute(new[] { context.ScenePath });
            if (stats.TriangleCount <= TriangleBudget && stats.TextureMemoryBytes <= TextureMemoryBudget)
                return;
            issues.Add(new BuildCheckIssue(Id, BuildCheckSeverity.Warning,
                "The scene is heavy for Quest: " + SceneQuickStats.FormatSummary(stats),
                $"A comfortable budget for a whole space on Quest is about {TriangleBudget:N0} triangles and " +
                $"{TextureMemoryBudget / (1024 * 1024):N0} MB of textures (estimated from the scene's assets). Heavy spaces load slowly " +
                "and run badly on headsets. ANALYZE BUNDLE shows what takes the space."));
        }
    }
}
