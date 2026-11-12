using System.Collections.Generic;
using System.Linq;
using BS.SDKEditor.BuildChecks;
using NUnit.Framework;
using Unity.VisualScripting;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// The content checks: concave colliders on moving bodies, terrains, particle curves, animation clips, stereo shaders,
    /// reflection probes and shader stripping, on hierarchies built in a preview scene.
    /// </summary>
    public class BuilderContentChecksTests
    {
        Scene _scene;
        readonly List<Object> _assets = new List<Object>();

        [SetUp]
        public void SetUp() => _scene = EditorSceneManager.NewPreviewScene();

        [TearDown]
        public void TearDown()
        {
            EditorSceneManager.ClosePreviewScene(_scene);
            foreach (var asset in _assets)
                if (asset != null)
                    Object.DestroyImmediate(asset);
            _assets.Clear();
        }

        GameObject Create(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent != null)
                go.transform.SetParent(parent, false);
            else
                SceneManager.MoveGameObjectToScene(go, _scene);
            return go;
        }

        List<BuildCheckIssue> Run(BuildCheck check, params BuildTarget[] targets)
        {
            var issues = new List<BuildCheckIssue>();
            var used = targets.Length > 0 ? targets : new[] { BuildTarget.Android, BuildTarget.StandaloneWindows };
            using (var context = BuildCheckContext.ForScene(_scene, used, "Assets/Test.unity"))
                check.Run(context, issues);
            return issues;
        }

        Mesh Triangle()
        {
            var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
            _assets.Add(mesh);
            return mesh;
        }

        GameObject Hull(string name, Transform parent)
        {
            var go = Create(name, parent);
            go.AddComponent<MeshCollider>().sharedMesh = Triangle();
            return go;
        }

        // ---- concave colliders on moving bodies ----

        [Test]
        public void ConcaveCollider_OnDynamicBody_IsFlagged_OnKinematicIsNot()
        {
            var dynamicBody = Create("dynamic");
            dynamicBody.AddComponent<Rigidbody>();
            Hull("hull", dynamicBody.transform);
            var kinematicBody = Create("kinematic");
            kinematicBody.AddComponent<Rigidbody>().isKinematic = true;
            Hull("hull", kinematicBody.transform);

            var issue = Run(new DynamicConcaveCollidersCheck()).Single();
            Assert.AreEqual(BuildCheckSeverity.Warning, issue.Severity);
            Assert.AreEqual(1, issue.Targets.Count);
            StringAssert.Contains("dynamic", string.Join("/", issue.Targets[0].NamePath));
        }

        [Test]
        public void ConcaveCollider_WithoutABody_OrWithoutAMesh_Passes()
        {
            Hull("static", null);
            Create("empty").AddComponent<Rigidbody>().gameObject.AddComponent<MeshCollider>();
            Assert.IsEmpty(Run(new DynamicConcaveCollidersCheck()));
        }

        [Test]
        public void ConcaveCollider_TheNearestBodyDecides()
        {
            var outer = Create("outer");
            outer.AddComponent<Rigidbody>();
            var inner = Create("inner", outer.transform);
            inner.AddComponent<Rigidbody>().isKinematic = true;
            Hull("hull", inner.transform);
            Assert.IsEmpty(Run(new DynamicConcaveCollidersCheck()));
        }

        // Mass 0 is a component from before these fields had defaults: it gets Unity's default Rigidbody, which isn't kinematic.
        [TestCase(1f, false, true)]
        [TestCase(1f, true, false)]
        [TestCase(0f, true, true)]
        public void ConcaveCollider_UnderABSRigidbody_FollowsTheBodyItWillAdd(float mass, bool isKinematic, bool flagged)
        {
            var go = Create("body");
            var body = go.AddComponent<BSRigidbody>();
            body.mass = mass;
            body.isKinematic = isKinematic;
            var hull = Hull("hull", go.transform);
            Assert.AreEqual(flagged, DynamicConcaveCollidersCheck.MovesUnderPhysics(hull.GetComponent<MeshCollider>()));
        }

        [Test]
        public void ConvexCollider_UnderABSRigidbody_IsNotReportedAsStaticGeometry()
        {
            var mesh = new Mesh
            {
                vertices = new[] { new Vector3(-10, 0, -10), new Vector3(-10, 0, 10), new Vector3(10, 0, 10), new Vector3(10, 0, -10) },
                triangles = new[] { 0, 1, 2, 0, 2, 3 },
            };
            _assets.Add(mesh);
            var go = Create("body");
            var collider = go.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            collider.convex = true;
            Assert.IsTrue(ConvexColliderValidation.IsSuspect(collider), "without a body, it's a large static collider");
            go.AddComponent<BSRigidbody>();
            Assert.IsFalse(ConvexColliderValidation.IsSuspect(collider));
        }

        // ---- terrains ----

        TerrainData TerrainDataWith(params TreePrototype[] trees)
        {
            var data = new TerrainData { treePrototypes = trees };
            _assets.Add(data);
            return data;
        }

        [Test]
        public void Terrain_WithoutData_IsFlagged()
        {
            Create("terrain").AddComponent<Terrain>();
            Assert.That(Run(new TerrainCheck()).Single().Title, Does.Contain("no Terrain Data"));
        }

        [Test]
        public void Terrain_WithMissingTreePrefab_AndNoCollider_IsReported()
        {
            LogAssert.ignoreFailingMessages = true; // Unity may complain about the empty tree type.
            var data = TerrainDataWith(new TreePrototype());
            Assume.That(data.treePrototypes.Length, Is.EqualTo(1), "Unity dropped the tree type");
            Create("terrain").AddComponent<Terrain>().terrainData = data;

            var titles = Run(new TerrainCheck()).Select(issue => issue.Title).ToList();
            Assert.That(titles, Has.Some.Contains("no enabled Terrain Collider"));
            Assert.That(titles, Has.Some.Contains("tree types with no prefab"));
        }

        [Test]
        public void Terrain_WithItsOwnDataOnItsCollider_Passes()
        {
            var data = TerrainDataWith();
            var go = Create("terrain");
            go.AddComponent<Terrain>().terrainData = data;
            go.AddComponent<TerrainCollider>().terrainData = data;
            Assert.IsEmpty(Run(new TerrainCheck()));
        }

        [Test]
        public void Terrain_ColliderOnOtherData_OrDisabled_IsFlagged()
        {
            var go = Create("terrain");
            go.AddComponent<Terrain>().terrainData = TerrainDataWith();
            var collider = go.AddComponent<TerrainCollider>();
            collider.terrainData = TerrainDataWith();
            Assert.That(Run(new TerrainCheck()).Single().Title, Does.Contain("another terrain's"));

            collider.enabled = false;
            Assert.That(Run(new TerrainCheck()).Single().Title, Does.Contain("no enabled Terrain Collider"));
        }

        // ---- particles ----

        ParticleSystem.VelocityOverLifetimeModule Velocity(string name)
        {
            var velocity = Create(name).AddComponent<ParticleSystem>().velocityOverLifetime;
            velocity.enabled = true;
            return velocity;
        }

        [Test]
        public void ParticleVelocity_MixedModes_IsFlagged()
        {
            LogAssert.ignoreFailingMessages = true;
            var velocity = Velocity("particles");
            velocity.x = new ParticleSystem.MinMaxCurve(1f);
            velocity.y = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0, 1, 1));
            velocity.z = new ParticleSystem.MinMaxCurve(1f);
            Assert.AreEqual(1, Run(new ParticleCurveModesCheck()).Count);
        }

        [Test]
        public void ParticleVelocity_OneMode_OrModuleOff_Passes()
        {
            LogAssert.ignoreFailingMessages = true;
            var same = Velocity("same");
            same.x = same.y = same.z = new ParticleSystem.MinMaxCurve(2f);
            var off = Velocity("off");
            off.x = new ParticleSystem.MinMaxCurve(1f);
            off.y = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0, 1, 1));
            off.enabled = false;
            Assert.IsEmpty(Run(new ParticleCurveModesCheck()));
        }

        // ---- animation clips ----

        AnimationClip Clip(bool legacy)
        {
            var clip = new AnimationClip { legacy = legacy };
            _assets.Add(clip);
            return clip;
        }

        [Test]
        public void AnimationComponent_WithANonLegacyClip_IsFlagged_LegacyIsNot()
        {
            AnimationUtility.SetAnimationClips(Create("modern").AddComponent<Animation>(), new[] { Clip(legacy: false) });
            AnimationUtility.SetAnimationClips(Create("legacy").AddComponent<Animation>(), new[] { Clip(legacy: true) });

            var issue = Run(new AnimationClipsCheck()).Single();
            Assert.AreEqual(1, issue.Targets.Count);
            StringAssert.Contains("modern", issue.Targets[0].Label);
        }

        static FlowGraph GraphWith(params IUnit[] units)
        {
            var graph = new FlowGraph();
            foreach (var unit in units)
            {
                graph.units.Add(unit);
                unit.EnsureDefined();
            }
            return graph;
        }

        static InvokeMember SampleClip() =>
            new InvokeMember(new Member(typeof(AnimationClip), nameof(AnimationClip.SampleAnimation), new[] { typeof(GameObject), typeof(float) }));

        static bool Samples(IGraph graph) => AnimationClipsCheck.Samples(graph, new HashSet<IGraph>());

        [Test]
        public void Graph_SamplingAClip_IsSeen_AlsoInsideASubgraph()
        {
            Assert.IsTrue(Samples(GraphWith(SampleClip())));

            var subgraph = new SubgraphUnit();
            subgraph.nest.SwitchToEmbed(GraphWith(SampleClip()));
            var outer = new FlowGraph();
            outer.units.Add(subgraph);
            Assert.IsTrue(Samples(outer));
        }

        [Test]
        public void Graph_WithoutSampling_OrNoGraph_Passes()
        {
            Assert.IsFalse(Samples(GraphWith(new InvokeMember(new Member(typeof(Vector3), nameof(Vector3.Normalize), new[] { typeof(Vector3) })))));
            Assert.IsFalse(Samples(null));
        }

        [Test]
        public void ScriptMachine_SamplingAClip_IsFlagged()
        {
            Create("graph").AddComponent<ScriptMachine>().nest.SwitchToEmbed(GraphWith(SampleClip()));
            Assert.That(Run(new AnimationClipsCheck()).Single().Title, Does.Contain("SampleAnimation"));
        }

        // ---- stereo shaders ----

        const string OwnVertexOutput = "#pragma vertex vert\nstruct V { float4 p : SV_POSITION; };";
        const string StereoMacros = "UNITY_VERTEX_OUTPUT_STEREO UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);";

        [TestCase(OwnVertexOutput, (int)StereoShadersCheck.Gap.NoStereoOutput)]
        [TestCase("#pragma  vertex vert\nfloat4 p : SV_Position;", (int)StereoShadersCheck.Gap.NoStereoOutput)]
        [TestCase(OwnVertexOutput + StereoMacros, (int)StereoShadersCheck.Gap.None)]
        [TestCase(OwnVertexOutput + "// " + StereoMacros, (int)StereoShadersCheck.Gap.NoStereoOutput)]
        [TestCase("// #pragma vertex vert\nfloat4 p : SV_POSITION;", (int)StereoShadersCheck.Gap.None)]
        [TestCase("#pragma vertex Vert\n#include \"Vertex.hlsl\"", (int)StereoShadersCheck.Gap.None)]
        [TestCase("sampler2D _CameraDepthTexture;", (int)StereoShadersCheck.Gap.ScreenTextureNotPerEye)]
        [TestCase("sampler2D_float _CameraDepthTexture;", (int)StereoShadersCheck.Gap.ScreenTextureNotPerEye)]
        [TestCase("TEXTURE2D(_CameraOpaqueTexture);", (int)StereoShadersCheck.Gap.ScreenTextureNotPerEye)]
        [TestCase("TEXTURE2D_X(_CameraDepthTexture);", (int)StereoShadersCheck.Gap.None)]
        public void StereoGaps(string source, int expected) =>
            Assert.AreEqual((StereoShadersCheck.Gap)expected, StereoShadersCheck.Gaps(source));

        [Test]
        public void StereoGaps_CanBothApply() =>
            Assert.AreEqual(StereoShadersCheck.Gap.NoStereoOutput | StereoShadersCheck.Gap.ScreenTextureNotPerEye,
                StereoShadersCheck.Gaps(OwnVertexOutput + "sampler2D _CameraOpaqueTexture;"));

        // ---- reflection probes ----

        [Test]
        public void RealtimeProbe_IsFlaggedWhenTheBuildIncludesQuest()
        {
            Create("realtime").AddComponent<ReflectionProbe>().mode = ReflectionProbeMode.Realtime;
            Create("baked").AddComponent<ReflectionProbe>().mode = ReflectionProbeMode.Baked;

            var quest = Run(new ReflectionProbesCheck()).Where(found => found.Title.Contains("don't update on Quest")).ToList();
            Assert.AreEqual(BuildCheckSeverity.Warning, quest.Single().Severity);
            StringAssert.Contains("realtime", quest.Single().Targets.Single().Label);

            Assert.IsFalse(Run(new ReflectionProbesCheck(), BuildTarget.StandaloneWindows).Any(found => found.Title.Contains("don't update on Quest")));
        }

        [Test]
        public void Probes_NoneInTheScene_Passes() => Assert.IsEmpty(Run(new ReflectionProbesCheck()));

        UniversalRenderPipelineAsset UrpAsset(bool blending, bool boxProjection)
        {
            var asset = ScriptableObject.CreateInstance<UniversalRenderPipelineAsset>();
            _assets.Add(asset);
            using (var fields = new SerializedObject(asset))
            {
                fields.FindProperty("m_ReflectionProbeBlending").boolValue = blending;
                fields.FindProperty("m_ReflectionProbeBoxProjection").boolValue = boxProjection;
                fields.ApplyModifiedPropertiesWithoutUndo();
            }
            return asset;
        }

        [Test]
        public void ProbeVariants_AreKeptWhenAnyOfATargetsAssetsNeedsThem()
        {
            var off = UrpAsset(blending: false, boxProjection: false);
            var on = UrpAsset(blending: true, boxProjection: true);

            Assert.IsTrue(ReflectionProbesCheck.VariantsLeftOut(new[] { off }, wantsBlending: true, wantsBox: false));
            Assert.IsTrue(ReflectionProbesCheck.VariantsLeftOut(new[] { off }, wantsBlending: false, wantsBox: true));
            Assert.IsFalse(ReflectionProbesCheck.VariantsLeftOut(new[] { off }, wantsBlending: false, wantsBox: false));
            Assert.IsFalse(ReflectionProbesCheck.VariantsLeftOut(new[] { off, on }, wantsBlending: true, wantsBox: true));
        }

        [Test]
        public void ProbeBlending_IsKeptWithTheGpuResidentDrawer()
        {
            var asset = UrpAsset(blending: false, boxProjection: false);
            using (var fields = new SerializedObject(asset))
            {
                fields.FindProperty("m_GPUResidentDrawerMode").intValue = 1;
                fields.ApplyModifiedPropertiesWithoutUndo();
            }
            Assert.IsFalse(ReflectionProbesCheck.VariantsLeftOut(new[] { asset }, wantsBlending: true, wantsBox: false));
        }

        // ---- shader stripping ----

        [Test]
        public void StripUnusedVariantsOff_IsFlagged_PostProcessingStrippingAloneIsNot()
        {
            Assume.That(GraphicsSettings.TryGetRenderPipelineSettings<URPShaderStrippingSetting>(out var stripping), "needs URP");
            var variants = stripping.stripUnusedVariants;
            var postProcessing = stripping.stripUnusedPostProcessingVariants;
            try
            {
                // URP's default for post processing is off, so this has to stay quiet on a fresh project.
                stripping.stripUnusedVariants = true;
                stripping.stripUnusedPostProcessingVariants = false;
                Assert.IsEmpty(Run(new ShaderStrippingCheck()));

                stripping.stripUnusedVariants = false;
                Assert.AreEqual(BuildCheckSeverity.Warning, Run(new ShaderStrippingCheck()).Single().Severity);
            }
            finally
            {
                stripping.stripUnusedVariants = variants;
                stripping.stripUnusedPostProcessingVariants = postProcessing;
            }
        }

        [TestCase(BuildTarget.Android)]
        [TestCase(BuildTarget.StandaloneWindows)]
        public void Forward_IsTheFirstSuggestion(BuildTarget target) =>
            Assert.AreEqual(RenderingMode.Forward, UrpRendererBuildCheck.AcceptedModes(target)[0]);

        [Test]
        public void ForwardPlus_StaysAcceptedOnWindowsOnly()
        {
            CollectionAssert.Contains(UrpRendererBuildCheck.AcceptedModes(BuildTarget.StandaloneWindows), RenderingMode.ForwardPlus);
            CollectionAssert.DoesNotContain(UrpRendererBuildCheck.AcceptedModes(BuildTarget.Android), RenderingMode.ForwardPlus);
        }

        // ---- odd scenes ----

        [Test]
        public void Checks_CopeWithHalfSetUpObjects()
        {
            Create("no mesh").AddComponent<MeshCollider>();
            Create("no terrain data").AddComponent<Terrain>();
            Create("no graph").AddComponent<ScriptMachine>();
            Create("no graph").AddComponent<StateMachine>();
            Create("no clips").AddComponent<Animation>();
            Create("no materials").AddComponent<MeshRenderer>().sharedMaterials = new Material[] { null };
            Create("particles").AddComponent<ParticleSystem>();
            Create("probe").AddComponent<ReflectionProbe>();
            var bare = Create("bare");
            bare.AddComponent<BSRigidbody>();
            Hull("hull", bare.transform);

            var checks = new BuildCheck[]
            {
                new DynamicConcaveCollidersCheck(), new TerrainCheck(), new ParticleCurveModesCheck(), new AnimationClipsCheck(),
                new StereoShadersCheck(), new ReflectionProbesCheck(), new ShaderStrippingCheck(),
            };
            using (var context = BuildCheckContext.ForScene(_scene, new[] { BuildTarget.Android, BuildTarget.StandaloneWindows }, "Assets/Test.unity"))
            {
                var result = BuildChecklist.Run(context, checks);
                Assert.IsFalse(result.Issues.Any(issue => issue.Title.Contains("couldn't run")));
            }
        }
    }
}
