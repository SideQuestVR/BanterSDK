using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace BS.SDKEditor.BuildChecks
{
    /// <summary>Convex hulls on floors and walls leave players colliding with the wrong surface.</summary>
    sealed class ConvexCollidersCheck : BuildCheck
    {
        public override string Id => "scene.convex-colliders";
        public override string Title => "Convex mesh colliders";
        public override string Description =>
            "Mesh colliders on big static geometry, like floors and walls, don't have Convex ticked. Convex collides against a rough hull, " +
            "so players can end up standing on air or stuck.";
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

    /// <summary>The client only has Unity's built-in tags and the SDK's; a built space stores a tag as its position in that list.</summary>
    sealed class UnsupportedTagsCheck : BuildCheck
    {
        public override string Id => "scene.tags";
        public override string Title => "Tags";
        public override string Description => "Objects only use tags the client has: Unity's built-in ones and the SDK's.";
        public override int Order => 60;

        public override void Run(BuildCheckContext context, List<BuildCheckIssue> issues)
        {
            foreach (var use in SceneCreatorBuildCheck.CollectUnsupportedTags(context.Scene))
            {
                var replacement = SceneCreatorBuildCheck.ReplacementTag(use.Tag);
                var renamed = replacement != "Untagged";
                issues.Add(new BuildCheckIssue(Id, BuildCheckSeverity.Error,
                        $"Tag '{use.Tag}' isn't one of the client's, and {use.Objects.Count} object(s) use it",
                        (renamed ? $"'{use.Tag}' is now called '{replacement}'. " : "The client doesn't have this tag. ") +
                        "In the client these objects would get a different tag, or none, so scripts comparing against it break. " +
                        "Use Untagged, UserTag1-32 or a BSLocalCharacter tag.")
                    .WithTargets(use.Objects.Select(context.Target))
                    .WithFix(renamed ? $"Change to {replacement}" : "Remove the tag", issue =>
                    {
                        var changed = false;
                        foreach (var go in issue.Resolve<GameObject>())
                        {
                            try
                            {
                                Undo.RecordObject(go, "Change tag");
                                go.tag = replacement;
                                changed = true;
                            }
                            catch (UnityException)
                            {
                                Debug.LogWarning($"[Creator SDK] There's no '{replacement}' tag yet: run 'SDK layers and tags' in " +
                                                 "Creator SDK > Setup first.");
                                return changed;
                            }
                        }
                        return changed;
                    }));
            }
        }
    }

    /// <summary>The client uses layers 25 and up for itself; the SDK sets up the rest.</summary>
    sealed class UnsupportedLayersCheck : BuildCheck
    {
        public override string Id => "scene.layers";
        public override string Title => "Layers";
        public override string Description => "Objects only use Unity's built-in layers and the SDK's (3 and 6 to 24).";
        public override int Order => 61;

        public override void Run(BuildCheckContext context, List<BuildCheckIssue> issues)
        {
            foreach (var use in SceneCreatorBuildCheck.CollectUnsupportedLayers(context.Scene))
            {
                issues.Add(new BuildCheckIssue(Id, BuildCheckSeverity.Error,
                        $"{use.Objects.Count} object(s) are on layer {use.Layer}, which the SDK doesn't set up",
                        "Layers 25 and up belong to the client, which uses them for its own rendering and physics, so these objects " +
                        "would behave unpredictably in a space. Move them to one of the SDK's layers (Creator SDK > Setup sets them up).")
                    .WithTargets(use.Objects.Select(context.Target))
                    .WithFix("Move to Default", issue =>
                    {
                        var changed = false;
                        foreach (var go in issue.Resolve<GameObject>())
                        {
                            Undo.RecordObject(go, "Move to Default layer");
                            go.layer = 0;
                            changed = true;
                        }
                        return changed;
                    }));
            }
        }
    }

    /// <summary>Scripts that are gone do nothing, and each logs a warning when the space loads.</summary>
    sealed class MissingScriptsCheck : BuildCheck
    {
        public override string Id => "scene.missing-scripts";
        public override string Title => "Missing scripts";
        public override string Description => "No object has a component whose script was deleted or isn't in this project.";
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
        public override string Description => "Every Script Machine and State Machine has its graph asset.";
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
        public override string Description => "No scene camera draws to the screen, and the scene has no Audio Listener: the player brings both.";
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
        public override string Description =>
            "Renderers have no empty material slots, no missing or broken shaders, and no shaders made for Unity's Built-in " +
            "pipeline, its own (Standard, Legacy Shaders, Mobile, Nature...) or custom ones such as surface shaders, which render " +
            "pink in URP.";
        public override int Order => 90;

        public override void Run(BuildCheckContext context, List<BuildCheckIssue> issues)
        {
            var emptySlots = new List<GameObject>();
            var builtIn = new Problem();
            var notUrp = new Problem();
            var broken = new Problem();

            foreach (var renderer in context.Components<Renderer>())
            {
                var materials = renderer is ParticleSystemRenderer particles ? ParticleMaterials(particles) : renderer.sharedMaterials;
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
                    else if (IsBuiltInPipelineShader(shader.name) && IsUnityResource(AssetDatabase.GetAssetPath(shader)))
                        builtIn.Add(material, renderer.gameObject, shader.name);
                    else if (UrpCantDraw(shader))
                        notUrp.Add(material, renderer.gameObject, shader.name);
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
            if (notUrp.Materials.Count > 0)
            {
                issues.Add(new BuildCheckIssue(Id, BuildCheckSeverity.Warning,
                        $"{notUrp.Materials.Count} material(s) use custom shaders written for the Built-in pipeline",
                        "Spaces render with URP, which draws none of these shaders' passes (a surface shader, say), so they " +
                        "show up pink: " + string.Join(", ", notUrp.Shaders) + ". Rewrite them for URP, or rebuild them in " +
                        "Shader Graph.")
                    .WithTargets(notUrp.Targets(context)));
            }
        }

        internal static bool IsBuiltInPipelineShader(string name) =>
            name == "Standard" || name == "Standard (Specular setup)" || name == "Autodesk Interactive"
            || name.StartsWith("Legacy Shaders/") || name.StartsWith("Mobile/") || name.StartsWith("Nature/")
            || name.StartsWith("Particles/Standard");

        /// <summary>
        /// Whether a shader at <paramref name="assetPath"/> is one of Unity's own rather than a project or package file:
        /// those names are only Unity's when Unity supplies the shader (the SDK's URP Mobile/StylizedFakeLit isn't).
        /// A project shader with such a name goes through the pass test like any other.
        /// </summary>
        internal static bool IsUnityResource(string assetPath) =>
            string.IsNullOrEmpty(assetPath) || assetPath == "Resources/unity_builtin_extra" || assetPath == "Library/unity default resources";

        /// <summary>
        /// A particle renderer's material, and its second slot, the trail material, when the system draws trails (an
        /// empty one there renders pink like any other).
        /// </summary>
        static Material[] ParticleMaterials(ParticleSystemRenderer particles)
        {
            var system = particles.GetComponent<ParticleSystem>();
            if (system == null || !system.trails.enabled)
                return new[] { particles.sharedMaterial };
            var shared = particles.sharedMaterials;
            return new[] { particles.sharedMaterial, shared.Length > 1 ? shared[1] : null };
        }

        /// <summary>
        /// Whether URP has no pass of <paramref name="shader"/> to draw, judged on the SubShader Unity picked for the
        /// project's pipeline. Only answers in a URP project: under another pipeline the picked SubShader isn't the one
        /// a space uses. Skyboxes draw through the skybox, not a renderer pass.
        /// </summary>
        static bool UrpCantDraw(Shader shader)
        {
            var pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline == null || !pipeline.GetType().FullName.StartsWith("UnityEngine.Rendering.Universal")
                || shader.name.StartsWith("Skybox/"))
                return false;
            var lightMode = new ShaderTagId("LightMode");
            var lightModes = new List<string>();
            for (var pass = 0; pass < shader.passCount; pass++)
                lightModes.Add(shader.FindPassTagValue(pass, lightMode).name);
            return !HasPassUrpDraws(lightModes);
        }

        /// <summary>
        /// The LightMode tags URP's forward renderer draws: UniversalForward, UniversalForwardOnly, SRPDefaultUnlit, the old
        /// LightweightForward, and passes with none (null or empty here). A surface shader's generated passes are ForwardBase,
        /// ForwardAdd, Deferred, ShadowCaster and Meta, so it has none of them.
        /// </summary>
        internal static bool HasPassUrpDraws(IEnumerable<string> lightModes) =>
            lightModes.Any(mode => string.IsNullOrEmpty(mode) || mode == "UniversalForward" || mode == "UniversalForwardOnly"
                                   || mode == "SRPDefaultUnlit" || mode == "LightweightForward");

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
        public override string Description =>
            $"The scene fits what a Quest loads comfortably: about {TriangleBudget:N0} triangles and " +
            $"{TextureMemoryBudget / (1024 * 1024):N0} MB of textures.";
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
