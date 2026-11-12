using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Unity.VisualScripting;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

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

    /// <summary>Unity can't simulate a concave mesh on a moving body: it logs an error and the collider doesn't collide.</summary>
    sealed class DynamicConcaveCollidersCheck : BuildCheck
    {
        public override string Id => "scene.dynamic-concave-colliders";
        public override string Title => "Mesh colliders on moving bodies";
        public override string Description => "Mesh colliders on a non-kinematic Rigidbody or BS Rigidbody have Convex ticked.";
        public override int Order => 51;

        public override void Run(BuildCheckContext context, List<BuildCheckIssue> issues)
        {
            var found = context.Components<MeshCollider>()
                .Where(collider => !collider.convex && collider.sharedMesh != null && MovesUnderPhysics(collider))
                .ToList();
            if (found.Count == 0)
                return;
            issues.Add(new BuildCheckIssue(Id, BuildCheckSeverity.Warning,
                    $"{found.Count} concave mesh collider(s) are on a moving Rigidbody",
                    "A mesh collider on a Rigidbody that physics moves must be Convex. Otherwise Unity logs \"Concave Mesh Colliders are " +
                    "not supported when used with dynamic Rigidbody\" in the space and the collider doesn't collide, so the object can " +
                    "fall through the world. Tick Convex, make the body kinematic, or use primitive colliders.")
                .WithTargets(context.TargetsOf(found))
                .WithFix("Tick Convex", issue =>
                {
                    var changed = false;
                    foreach (var collider in issue.Resolve<GameObject>().SelectMany(go => go.GetComponents<MeshCollider>()))
                    {
                        if (collider.convex || !MovesUnderPhysics(collider))
                            continue;
                        Undo.RecordObject(collider, "Tick Convex");
                        collider.convex = true;
                        changed = true;
                    }
                    return changed;
                }));
        }

        /// <summary>
        /// Whether physics moves the body this collider belongs to, which is the nearest Rigidbody or BS Rigidbody at or above
        /// it. A BS Rigidbody with no Rigidbody adds one when the space loads: set up from its own fields, or left at Unity's
        /// defaults (not kinematic) when its mass is 0.
        /// </summary>
        internal static bool MovesUnderPhysics(MeshCollider collider)
        {
            for (var transform = collider.transform; transform != null; transform = transform.parent)
            {
                var body = transform.GetComponent<Rigidbody>();
                if (body != null)
                    return !body.isKinematic;
                var bsBody = transform.GetComponent<BSRigidbody>();
                if (bsBody == null)
                    continue;
                using (var fields = new SerializedObject(bsBody))
                    return !(fields.FindProperty("isKinematic")?.boolValue == true && fields.FindProperty("mass")?.floatValue > 0f);
            }
            return false;
        }
    }

    /// <summary>Terrains need their data, an enabled collider on the same data, and their tree prefabs.</summary>
    sealed class TerrainCheck : BuildCheck
    {
        public override string Id => "scene.terrain";
        public override string Title => "Terrains";
        public override string Description =>
            "Every Terrain has its Terrain Data, an enabled Terrain Collider using that same data, and no tree types whose prefab is missing.";
        public override int Order => 52;

        public override void Run(BuildCheckContext context, List<BuildCheckIssue> issues)
        {
            var terrains = context.Components<Terrain>().ToList();
            if (terrains.Count == 0)
                return;

            var noData = terrains.Where(terrain => terrain.terrainData == null).ToList();
            if (noData.Count > 0)
                issues.Add(new BuildCheckIssue(Id, BuildCheckSeverity.Warning,
                        $"{noData.Count} Terrain(s) have no Terrain Data",
                        "Their Terrain Data asset is missing or was never assigned, so nothing shows. Assign it, or remove the Terrain.")
                    .WithTargets(context.TargetsOf(noData)));

            var withData = terrains.Where(terrain => terrain.terrainData != null).ToList();

            var noCollider = withData.Where(terrain =>
            {
                var collider = ColliderOf(terrain);
                return collider == null || !collider.enabled;
            }).ToList();
            if (noCollider.Count > 0)
                issues.Add(new BuildCheckIssue(Id, BuildCheckSeverity.Warning,
                        $"{noCollider.Count} Terrain(s) have no enabled Terrain Collider",
                        "Players and objects fall straight through them. Leave it off only on a terrain nobody can reach.")
                    .WithTargets(context.TargetsOf(noCollider))
                    .WithFix("Add a Terrain Collider", MatchColliders));

            var mismatched = withData.Where(terrain =>
            {
                var collider = ColliderOf(terrain);
                return collider != null && collider.enabled && ColliderData(collider) != terrain.terrainData;
            }).ToList();
            if (mismatched.Count > 0)
                issues.Add(new BuildCheckIssue(Id, BuildCheckSeverity.Warning,
                        $"{mismatched.Count} Terrain Collider(s) have no Terrain Data, or another terrain's",
                        "Players would collide with a shape they can't see, or with nothing. Use the Terrain's own data.")
                    .WithTargets(context.TargetsOf(mismatched))
                    .WithFix("Use the Terrain's data", MatchColliders));

            var missingTrees = withData
                .Select(terrain => (terrain, indices: MissingTreePrefabs(terrain.terrainData)))
                .Where(item => item.indices.Count > 0)
                .ToList();
            if (missingTrees.Count > 0)
                issues.Add(new BuildCheckIssue(Id, BuildCheckSeverity.Warning,
                        $"{missingTrees.Count} Terrain(s) have tree types with no prefab",
                        "Those trees don't appear, and every player's log fills with \"Tree prefab at index N is missing\". " +
                        string.Join("; ", missingTrees.Select(item => $"{item.terrain.name}: tree type {string.Join(", ", item.indices)}")) +
                        ". Assign the prefabs on the Terrain's Paint Trees tab, or remove those tree types.")
                    .WithTargets(missingTrees.Select(item => context.Target(item.terrain.gameObject))));
        }

        internal static List<int> MissingTreePrefabs(TerrainData data) =>
            data.treePrototypes
                .Select((prototype, index) => (prototype, index))
                .Where(item => item.prototype == null || item.prototype.prefab == null)
                .Select(item => item.index)
                .ToList();

        // The Terrain Collider is reached by name: its module is optional in a project, and the SDK doesn't depend on it.
        static Collider ColliderOf(Terrain terrain) => terrain.GetComponent("TerrainCollider") as Collider;

        static Object ColliderData(Collider collider)
        {
            using (var fields = new SerializedObject(collider))
                return fields.FindProperty("m_TerrainData")?.objectReferenceValue;
        }

        static bool MatchColliders(BuildCheckIssue issue)
        {
            var changed = false;
            foreach (var go in issue.Resolve<GameObject>())
            {
                var terrain = go.GetComponent<Terrain>();
                if (terrain == null || terrain.terrainData == null)
                    continue;
                var collider = ColliderOf(terrain);
                if (collider == null)
                {
                    var type = Type.GetType("UnityEngine.TerrainCollider, UnityEngine.TerrainPhysicsModule");
                    collider = type != null ? Undo.AddComponent(go, type) as Collider : null;
                    if (collider == null)
                        continue;
                }
                Undo.RecordObject(collider, "Fix Terrain Collider");
                collider.enabled = true;
                using (var fields = new SerializedObject(collider))
                {
                    var data = fields.FindProperty("m_TerrainData");
                    if (data != null)
                    {
                        data.objectReferenceValue = terrain.terrainData;
                        fields.ApplyModifiedProperties();
                    }
                }
                changed = true;
            }
            return changed;
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
        internal static bool UrpCantDraw(Shader shader)
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
        /// ForwardAdd, Deferred, ShadowCaster and Meta, so it has none of them. Compared ignoring case: Unity reports some tags
        /// upper-cased (SRPDEFAULTUNLIT), which flagged working URP shaders as Built-in ones.
        /// </summary>
        internal static bool HasPassUrpDraws(IEnumerable<string> lightModes) =>
            lightModes.Any(mode => string.IsNullOrEmpty(mode) || UrpLightModes.Contains(mode));

        static readonly HashSet<string> UrpLightModes = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        {
            "UniversalForward", "UniversalForwardOnly", "SRPDefaultUnlit", "LightweightForward",
        };

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

    /// <summary>The headset draws both eyes in one pass: a hand-written shader that doesn't pass the eye on shows in one eye only.</summary>
    sealed class StereoShadersCheck : BuildCheck
    {
        public override string Id => "render.stereo-shaders";
        public override string Title => "VR shaders";
        public override string Description =>
            "Hand-written shaders pass the eye through (UNITY_VERTEX_OUTPUT_STEREO and friends) and read screen textures per eye, " +
            "as the headset draws both eyes at once.";
        public override int Order => 91;

        [Flags]
        internal enum Gap { None = 0, NoStereoOutput = 1, ScreenTextureNotPerEye = 2 }

        static readonly Regex Comments = new Regex(@"/\*.*?\*/|//[^\n]*", RegexOptions.Singleline);
        static readonly Regex VertexProgram = new Regex(@"#\s*pragma\s+vertex\b");
        // HLSL semantics ignore case, and SV_Position is as common as SV_POSITION.
        static readonly Regex ClipPosition = new Regex(@"\bSV_POSITION\b", RegexOptions.IgnoreCase);
        static readonly Regex FlatScreenTexture =
            new Regex(@"\b(sampler2D(_float|_half)?|TEXTURE2D(_FLOAT|_HALF)?)\s*\(?\s*_Camera(Opaque|Depth)Texture\b");

        public override void Run(BuildCheckContext context, List<BuildCheckIssue> issues)
        {
            var gaps = new Dictionary<Shader, Gap>();
            var users = new Dictionary<Shader, HashSet<GameObject>>();
            foreach (var renderer in context.Components<Renderer>())
            {
                foreach (var material in renderer.sharedMaterials)
                {
                    var shader = material != null ? material.shader : null;
                    if (shader == null)
                        continue;
                    if (!gaps.TryGetValue(shader, out var gap))
                    {
                        gaps[shader] = gap = GapsOf(shader);
                        users[shader] = new HashSet<GameObject>();
                    }
                    if (gap != Gap.None)
                        users[shader].Add(renderer.gameObject);
                }
            }

            foreach (var pair in gaps.Where(item => item.Value != Gap.None))
            {
                var why = new List<string>();
                if ((pair.Value & Gap.NoStereoOutput) != 0)
                    why.Add("its vertex program doesn't pass the eye on: add UNITY_VERTEX_INPUT_INSTANCE_ID, UNITY_VERTEX_OUTPUT_STEREO, " +
                            "UNITY_SETUP_INSTANCE_ID and UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO");
                if ((pair.Value & Gap.ScreenTextureNotPerEye) != 0)
                    why.Add("it reads _CameraOpaqueTexture or _CameraDepthTexture as a 2D texture: use TEXTURE2D_X and SAMPLE_TEXTURE2D_X, " +
                            "or SampleSceneColor and SampleSceneDepth");
                issues.Add(new BuildCheckIssue(Id, BuildCheckSeverity.Warning,
                        $"Shader '{pair.Key.name}' may render in one eye only in the headset",
                        "The headset draws both eyes in one pass. In this shader " + string.Join("; ", why) + ". Shader Graph does this for you.")
                    .WithTargets(new[] { BuildCheckTarget.ForAsset(pair.Key) }.Concat(users[pair.Key].Select(context.Target))));
            }
        }

        /// <summary>
        /// Judges a shader on its own source. Shader Graph, Unity's and the SDK's shaders pass the eye through, and
        /// <see cref="MaterialsCheck"/> reports shaders that are broken or that URP can't draw.
        /// </summary>
        static Gap GapsOf(Shader shader)
        {
            var path = AssetDatabase.GetAssetPath(shader);
            if (!path.EndsWith(".shader", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("Packages/com.unity.", StringComparison.Ordinal)
                || path.StartsWith("Packages/com.sidequest.creator-sdk/", StringComparison.Ordinal)
                || ShaderUtil.ShaderHasError(shader) || MaterialsCheck.UrpCantDraw(shader))
                return Gap.None;
            try
            {
                return Gaps(File.ReadAllText(FileUtil.GetPhysicalPath(path)));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return Gap.None;
            }
        }

        /// <summary>
        /// Text only, so it can be tested without compiling. A shader is only judged when its own text has the vertex program's
        /// output: one that keeps it in an include is skipped, never wrongly warned about.
        /// </summary>
        internal static Gap Gaps(string source)
        {
            source = Comments.Replace(source, "");
            var gap = Gap.None;
            if (VertexProgram.IsMatch(source) && ClipPosition.IsMatch(source)
                && !(source.Contains("UNITY_VERTEX_OUTPUT_STEREO") && source.Contains("UNITY_SETUP_INSTANCE_ID")
                     && source.Contains("UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO")))
                gap |= Gap.NoStereoOutput;
            if (FlatScreenTexture.IsMatch(source))
                gap |= Gap.ScreenTextureNotPerEye;
            return gap;
        }
    }

    /// <summary>
    /// Quest doesn't update realtime probes, and probe blending and box projection only reach the client if the project's URP
    /// assets keep their shader variants.
    /// </summary>
    sealed class ReflectionProbesCheck : BuildCheck
    {
        public override string Id => "render.reflection-probes";
        public override string Title => "Reflection probes";
        public override string Description =>
            "Realtime reflection probes are pointed out, as they don't update on Quest, and the URP assets keep reflection probe " +
            "blending and box projection when the scene uses them.";
        public override int Order => 92;

        public override void Run(BuildCheckContext context, List<BuildCheckIssue> issues)
        {
            var probes = context.Components<ReflectionProbe>().Where(probe => probe.enabled).ToList();
            if (probes.Count == 0)
                return;

            var realtime = probes.Where(probe => probe.mode == ReflectionProbeMode.Realtime).ToList();
            if (realtime.Count > 0 && context.Targets.Contains(BuildTarget.Android))
                issues.Add(new BuildCheckIssue(Id, BuildCheckSeverity.Warning,
                        $"{realtime.Count} realtime reflection probe(s) don't update on Quest",
                        "The client's Quest quality level has realtime reflection probes off, so surfaces won't reflect what these probes " +
                        "show on Windows. Set Type to Baked and bake them if the reflections matter on Quest.")
                    .WithTargets(context.TargetsOf(realtime)));

            var wantsBlending = probes.Count > 1;
            var wantsBox = probes.Any(probe => probe.boxProjection);
            var off = new List<UniversalRenderPipelineAsset>();
            foreach (var target in context.Targets)
            {
                var assets = new List<UniversalRenderPipelineAsset>();
                if (!target.TryGetRenderPipelineAssets(assets))
                    continue;
                assets.RemoveAll(asset => asset == null);
                if (assets.Count > 0 && VariantsLeftOut(assets, wantsBlending, wantsBox))
                    off.AddRange(assets);
            }
            off = off.Distinct().ToList();
            if (off.Count == 0)
                return;

            var issue = new BuildCheckIssue(Id, BuildCheckSeverity.Warning,
                    "Reflection probe blending or box projection is off in the URP assets",
                    "Their shader variants are left out of the world, so probes don't blend, or reflections ignore the probe's box, in the " +
                    "client. Turn on Probe Blending and Box Projection under Lighting > Reflection Probes in: " +
                    string.Join(", ", off.Select(asset => asset.name)))
                .WithTargets(off.Select(asset => BuildCheckTarget.ForAsset(asset)));
            var editable = off.Where(asset => AssetDatabase.GetAssetPath(asset).StartsWith("Assets/", StringComparison.Ordinal)).ToList();
            if (editable.Count > 0)
            {
                issue.WithFix("Turn them on", _ =>
                {
                    var changed = false;
                    foreach (var asset in editable.Where(asset => asset != null))
                    {
                        // The setters are internal to URP.
                        using (var fields = new SerializedObject(asset))
                        {
                            SetTrue(fields, "m_ReflectionProbeBlending", wantsBlending);
                            SetTrue(fields, "m_ReflectionProbeBoxProjection", wantsBox);
                            changed |= fields.ApplyModifiedProperties();
                        }
                    }
                    AssetDatabase.SaveAssets();
                    return changed;
                }, needsLoadedScene: false);
            }
            issues.Add(issue);
        }

        static void SetTrue(SerializedObject fields, string name, bool wanted)
        {
            var field = fields.FindProperty(name);
            if (wanted && field != null)
                field.boolValue = true;
        }

        /// <summary>URP keeps a variant when any of a target's assets needs it.</summary>
        internal static bool VariantsLeftOut(IReadOnlyCollection<UniversalRenderPipelineAsset> assets, bool wantsBlending, bool wantsBox) =>
            (wantsBlending && !assets.Any(asset => asset.reflectionProbeBlending || UsesGpuResidentDrawer(asset)))
            || (wantsBox && !assets.Any(asset => asset.reflectionProbeBoxProjection));

        /// <summary>URP always blends with the GPU Resident Drawer. Its mode type is in an assembly the SDK doesn't reference.</summary>
        static bool UsesGpuResidentDrawer(UniversalRenderPipelineAsset asset)
        {
            using (var fields = new SerializedObject(asset))
                return fields.FindProperty("m_GPUResidentDrawerMode")?.intValue > 0;
        }
    }

    /// <summary>Velocity over Lifetime's X, Y and Z must share one curve mode.</summary>
    sealed class ParticleCurveModesCheck : BuildCheck
    {
        public override string Id => "scene.particle-curve-modes";
        public override string Title => "Particle velocity curves";
        public override string Description => "Particle systems that use Velocity over Lifetime use one curve mode for X, Y and Z.";
        public override int Order => 96;

        public override void Run(BuildCheckContext context, List<BuildCheckIssue> issues)
        {
            var mixed = context.Components<ParticleSystem>().Where(system =>
            {
                var velocity = system.velocityOverLifetime;
                return velocity.enabled && (velocity.x.mode != velocity.y.mode || velocity.y.mode != velocity.z.mode);
            }).ToList();
            if (mixed.Count == 0)
                return;
            issues.Add(new BuildCheckIssue(Id, BuildCheckSeverity.Warning,
                    $"{mixed.Count} particle system(s) mix curve modes in Velocity over Lifetime",
                    "X, Y and Z must all be Constant, all Curve, or all one of the Random modes. Otherwise the space logs \"Particle " +
                    "Velocity curves must all be in the same mode\" over and over, which costs frame time on Quest. Set the three to one mode.")
                .WithTargets(context.TargetsOf(mixed)));
        }
    }

    /// <summary>
    /// AnimationClip.SampleAnimation samples any clip in the editor, but in the app only a Legacy clip or one on a GameObject
    /// with an Animator; a legacy Animation component only plays Legacy clips.
    /// </summary>
    sealed class AnimationClipsCheck : BuildCheck
    {
        public override string Id => "scene.animation-clips";
        public override string Title => "Animation clips";
        public override string Description => "Legacy Animation components only hold Legacy clips, and graphs that sample clips are pointed out.";
        public override int Order => 97;

        public override void Run(BuildCheckContext context, List<BuildCheckIssue> issues)
        {
            var nonLegacy = context.Components<Animation>()
                .Where(animation => AnimationUtility.GetAnimationClips(animation.gameObject).Any(clip => clip != null && !clip.legacy))
                .ToList();
            if (nonLegacy.Count > 0)
                issues.Add(new BuildCheckIssue(Id, BuildCheckSeverity.Warning,
                        $"{nonLegacy.Count} Animation component(s) hold clips that aren't Legacy",
                        "The old Animation component only plays clips marked Legacy, so these don't play in the space. Use an Animator, " +
                        "or mark the clips Legacy (the clip's Inspector, in Debug mode).")
                    .WithTargets(context.TargetsOf(nonLegacy)));

            var sampling = context.Components<ScriptMachine>().Where(machine => Samples(() => machine.graph)).Cast<Component>()
                .Concat(context.Components<StateMachine>().Where(machine => Samples(() => machine.graph)))
                .ToList();
            if (sampling.Count > 0)
                issues.Add(new BuildCheckIssue(Id, BuildCheckSeverity.Warning,
                        $"{sampling.Count} Visual Scripting machine(s) call AnimationClip.SampleAnimation",
                        "In the editor it samples any clip. In the space it only samples a Legacy clip or a GameObject that has an " +
                        "Animator; otherwise it does nothing and logs \"Non-Legacy animations cannot be sampled outside the Editor\" " +
                        "each time. Add an Animator to the GameObject being sampled, or mark the clip Legacy.")
                    .WithTargets(context.TargetsOf(sampling)));
        }

        // A graph that can't be read isn't this check's to report: the Visual Scripting node check is.
        static bool Samples(Func<IGraph> graph)
        {
            try
            {
                return Samples(graph(), new HashSet<IGraph>());
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Whether the graph, or one nested in it, invokes AnimationClip.SampleAnimation.</summary>
        internal static bool Samples(IGraph graph, HashSet<IGraph> seen)
        {
            if (graph == null || !seen.Add(graph))
                return false;
            foreach (var element in graph.elements)
            {
                if (element is MemberUnit unit && unit.member != null && unit.member.targetType == typeof(AnimationClip)
                    && unit.member.name == nameof(AnimationClip.SampleAnimation))
                    return true;
                if (element is IGraphParentElement parent && Samples(parent.childGraph, seen))
                    return true;
            }
            return false;
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
