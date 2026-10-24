using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BS.SDKEditor.BuildChecks;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// <see cref="ApvBakedData"/> (whether a baked APV baking set can be loaded without reading past its data) and the
    /// Builder's <see cref="ApvDataCheck"/> (leftover or broken APV data in a scene).
    /// </summary>
    public class ApvBakedDataTests
    {
        // URP 17's chunk: 128 bricks of 64 probes.
        const int Bricks = 128;
        const int N = 128 * 64;
        const int BrickSize = 16;
        const ProbeVolumeSHBands L1 = ProbeVolumeSHBands.SphericalHarmonicsL1;
        const ProbeVolumeSHBands L2 = ProbeVolumeSHBands.SphericalHarmonicsL2;

        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        readonly List<Object> _objects = new List<Object>();
        Scene _scene;

        [TearDown]
        public void TearDown()
        {
            if (_scene.IsValid())
                EditorSceneManager.ClosePreviewScene(_scene);
            foreach (var obj in _objects)
                if (obj != null)
                    Object.DestroyImmediate(obj);
            _objects.Clear();
        }

        // ---- the arithmetic ----

        /// <summary>Two cells (3 and 1 chunks) baked by this URP version, with exactly the bytes they need.</summary>
        static ApvBakedData.Layout GoodLayout()
        {
            var layout = new ApvBakedData.Layout
            {
                ChunkSizeInBricks = Bricks,
                L0ChunkSize = 8 * N,
                L1ChunkSize = 4 * N,
                L2TextureChunkSize = 4 * N,
                SharedValidityMaskChunkSize = N,
                BakedMaskCount = 1,
            };
            layout.Cells[10] = new ApvBakedData.Cell { ShChunkCount = 3, BricksCount = 300 };
            layout.Cells[11] = new ApvBakedData.Cell { ShChunkCount = 1, BricksCount = 5 };
            layout.SharedBytes = 4L * N;
            layout.BricksBytes = 305L * BrickSize;
            var scenario = new ApvBakedData.Scenario { Name = "Default", CellBytes = 4L * 16 * N };
            scenario.CellIndices.Add(10);
            scenario.CellIndices.Add(11);
            layout.Scenarios.Add(scenario);
            return layout;
        }

        static string Check(ApvBakedData.Layout layout, ProbeVolumeSHBands bands = L1) =>
            ApvBakedData.FindProblem(layout, bands, Bricks, N, BrickSize);

        [Test]
        public void ExactlyTheBytesTheCellsNeed_Passes()
        {
            Assert.IsNull(Check(GoodLayout()));
        }

        [Test]
        public void NoCells_Passes_NothingIsLoaded()
        {
            var layout = GoodLayout();
            layout.Cells.Clear();
            layout.SharedBytes = 0;
            Assert.IsNull(Check(layout));
        }

        [Test]
        public void ShortOrEmptyL0L1Data_IsAProblem()
        {
            var layout = GoodLayout();
            layout.Scenarios[0].CellBytes -= 1;
            StringAssert.Contains("scenario 'Default' L0/L1 data file is", Check(layout));

            layout.Scenarios[0].CellBytes = 0;
            StringAssert.Contains("0 bytes", Check(layout));
        }

        [Test]
        public void ShortSharedOrBrickData_IsAProblem()
        {
            var layout = GoodLayout();
            layout.SharedBytes -= 1;
            StringAssert.Contains("shared data", Check(layout));

            layout = GoodLayout();
            layout.BricksBytes = 304L * BrickSize;
            StringAssert.Contains("brick data", Check(layout));
        }

        [Test]
        public void AMissingDataFile_IsAProblem()
        {
            var layout = GoodLayout();
            layout.Scenarios[0].CellBytes = ApvBakedData.Missing;
            StringAssert.Contains("is missing", Check(layout));
        }

        [Test]
        public void SkyOcclusion_AddsToTheSharedDataEachCellNeeds()
        {
            var layout = GoodLayout();
            layout.BakedSkyOcclusion = true;
            layout.SharedSkyOcclusionL0L1ChunkSize = 8 * N;
            StringAssert.Contains("shared data", Check(layout));

            layout.SharedBytes = 4L * (N + 8 * N);
            Assert.IsNull(Check(layout));

            layout.BakedSkyShadingDirection = true;
            layout.SharedSkyShadingDirectionIndicesChunkSize = N;
            StringAssert.Contains("shared data", Check(layout));
            layout.SharedBytes = 4L * (N + 8 * N + N);
            Assert.IsNull(Check(layout));
        }

        [Test]
        public void L2Data_IsOnlyNeededOnAnL2Pipeline()
        {
            var layout = GoodLayout(); // no L2 file at all
            Assert.IsNull(Check(layout, L1));
            StringAssert.Contains("L2 data file is missing", Check(layout, L2));

            layout.Scenarios[0].OptionalBytes = 4L * 4 * 4 * N;
            Assert.IsNull(Check(layout, L2));
        }

        [Test]
        public void ProbeOcclusion_IsOnlyNeededWhenBaked()
        {
            var layout = GoodLayout();
            layout.ProbeOcclusionChunkSize = 4 * N;
            Assert.IsNull(Check(layout));

            layout.BakedProbeOcclusion = true;
            StringAssert.Contains("probe occlusion data file is missing", Check(layout));
            layout.Scenarios[0].OcclusionBytes = 4L * 4 * N;
            Assert.IsNull(Check(layout));
        }

        [Test]
        public void ChunksFromAnotherLayout_AreAProblem()
        {
            var layout = GoodLayout();
            layout.ChunkSizeInBricks = 64;
            StringAssert.Contains("bricks per chunk", Check(layout));

            layout = GoodLayout();
            layout.L0ChunkSize = 4 * N;
            StringAssert.Contains("L0/L1 chunks", Check(layout));

            layout = GoodLayout();
            layout.BakedMaskCount = 4; // rendering layers: four validity bytes per probe
            StringAssert.Contains("validity chunks", Check(layout));
            layout.SharedValidityMaskChunkSize = 4 * N;
            layout.SharedBytes = 4L * 4 * N;
            Assert.IsNull(Check(layout));
        }

        [Test]
        public void MoreBricksThanTheChunksHold_IsAProblem()
        {
            var layout = GoodLayout();
            layout.Cells[11] = new ApvBakedData.Cell { ShChunkCount = 1, BricksCount = Bricks + 1 };
            StringAssert.Contains("cell 11", Check(layout));
        }

        [Test]
        public void AScenarioOnlyNeedsTheCellsItHas()
        {
            var layout = GoodLayout();
            layout.Scenarios[0].CellIndices.Remove(10);
            layout.Scenarios[0].CellBytes = 1L * 16 * N;
            Assert.IsNull(Check(layout));
        }

        // ---- a real baking set, read by reflection ----

        [Test]
        public void TheCheckFindsThisUrpVersionsInternals()
        {
            Assert.IsTrue(ApvBakedData.CanCheck,
                "ProbeVolumeBakingSet / ProbeVolumeStreamableAsset / ProbeBrickPool internals changed in this URP version; update " +
                "BS.ApvBakedData's reflection (and Greenfield's Assets/link.xml). Until then the client lights no world with APV.");
        }

        [Test]
        public void ARealBakingSet_IsReadAndChecked()
        {
            var set = BakingSet(cellBytes: 16L * N, sharedBytes: N, bricksBytes: 100L * BrickSize);
            Assert.IsNull(ApvBakedData.FindProblem(set, L1));

            set = BakingSet(cellBytes: 16L * N - 1, sharedBytes: N, bricksBytes: 100L * BrickSize);
            StringAssert.Contains("L0/L1 data file is", ApvBakedData.FindProblem(set, L1));

            set = BakingSet(cellBytes: 16L * N, sharedBytes: N, bricksBytes: 100L * BrickSize, cellData: false);
            StringAssert.Contains("L0/L1 data file is missing", ApvBakedData.FindProblem(set, L1));
        }

        [Test]
        public void ARealBakingSet_MaskCountSkyAndProbeOcclusionAreRead()
        {
            var set = BakingSet(cellBytes: 16L * N, sharedBytes: N, bricksBytes: 100L * BrickSize);
            SetField(set, "bakedMaskCount", 4);
            StringAssert.Contains("validity chunks", ApvBakedData.FindProblem(set, L1));

            set = BakingSet(cellBytes: 16L * N, sharedBytes: N, bricksBytes: 100L * BrickSize);
            SetField(set, "bakedSkyOcclusionValue", 1);
            SetField(set, "sharedSkyOcclusionL0L1ChunkSize", 8 * N);
            StringAssert.Contains("shared data", ApvBakedData.FindProblem(set, L1));

            set = BakingSet(cellBytes: 16L * N, sharedBytes: N, bricksBytes: 100L * BrickSize);
            SetField(set, "bakedProbeOcclusion", true);
            SetField(set, "ProbeOcclusionChunkSize", 4 * N);
            StringAssert.Contains("probe occlusion data file is missing", ApvBakedData.FindProblem(set, L1));
        }

        static void SetField(ProbeVolumeBakingSet set, string field, object value) =>
            typeof(ProbeVolumeBakingSet).GetField(field, Any).SetValue(set, value);

        const string SceneGuid = "0123456789abcdef0123456789abcdef";

        /// <summary>
        /// A baking set with one cell of one chunk and 100 bricks, its data files the given sizes, listing that cell for
        /// <see cref="SceneGuid"/> (unless <paramref name="listsTheScene"/> is false).
        /// </summary>
        ProbeVolumeBakingSet BakingSet(long cellBytes, long sharedBytes, long bricksBytes, bool cellData = true, bool listsTheScene = true)
        {
            var set = ScriptableObject.CreateInstance<ProbeVolumeBakingSet>();
            set.name = "TestBakingSet";
            _objects.Add(set);
            var setType = typeof(ProbeVolumeBakingSet);
            void Set(string field, object value) => setType.GetField(field, Any).SetValue(set, value);
            Set("chunkSizeInBricks", Bricks);
            Set("L0ChunkSize", 8 * N);
            Set("L1ChunkSize", 4 * N);
            Set("L2TextureChunkSize", 4 * N);
            Set("sharedValidityMaskChunkSize", N);
            Set("bakedMaskCount", 1);

            var cellDescType = typeof(ProbeReferenceVolume).GetNestedType("CellDesc", BindingFlags.NonPublic);
            var desc = Activator.CreateInstance(cellDescType, true);
            cellDescType.GetField("shChunkCount", Any).SetValue(desc, 1);
            cellDescType.GetField("bricksCount", Any).SetValue(desc, 100);
            ((IDictionary)setType.GetField("cellDescs", Any).GetValue(set))[7] = desc;

            Set("cellSharedDataAsset", Streamable(sharedBytes, 7));
            Set("cellBricksDataAsset", Streamable(bricksBytes, 7));

            var scenarioType = setType.GetNestedType("PerScenarioDataInfo", BindingFlags.NonPublic);
            var scenario = Activator.CreateInstance(scenarioType, true);
            scenarioType.GetField("cellDataAsset", Any).SetValue(scenario, cellData ? Streamable(cellBytes, 7) : Streamable(ApvBakedData.Missing, 7));
            scenarioType.GetField("cellOptionalDataAsset", Any).SetValue(scenario, Streamable(ApvBakedData.Missing));
            scenarioType.GetField("cellProbeOcclusionDataAsset", Any).SetValue(scenario, Streamable(ApvBakedData.Missing));
            ((IDictionary)setType.GetField("scenarios", Any).GetValue(set))["Default"] = scenario;

            if (listsTheScene)
                ((Dictionary<string, List<int>>)setType.GetField("perSceneCellLists", Any).GetValue(set))[SceneGuid] = new List<int> { 7 };
            return set;
        }

        /// <summary>A ProbeVolumeStreamableAsset whose TextAsset holds <paramref name="bytes"/> bytes (none when Missing).</summary>
        object Streamable(long bytes, params int[] cells)
        {
            var type = typeof(ProbeVolumeBakingSet).Assembly.GetType("UnityEngine.Rendering.ProbeVolumeStreamableAsset");
            var streamable = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(type);
            var descsField = type.GetField("m_StreamableCellDescs", Any);
            var descs = (IDictionary)Activator.CreateInstance(descsField.FieldType);
            var cellDescType = type.GetNestedType("StreamableCellDesc", BindingFlags.Public | BindingFlags.NonPublic);
            foreach (int cell in cells)
                descs[cell] = Activator.CreateInstance(cellDescType);
            descsField.SetValue(streamable, descs);
            if (bytes != ApvBakedData.Missing)
            {
                var text = new TextAsset(new string('x', (int)bytes));
                _objects.Add(text);
                type.GetField("m_Asset", Any).SetValue(streamable, text);
            }
            return streamable;
        }

        // ---- the Builder check ----

        ProbeVolumePerSceneData PerSceneData(ProbeVolumeBakingSet set, bool urpsOwnObject = true)
        {
            if (!_scene.IsValid())
                _scene = EditorSceneManager.NewPreviewScene();
            var go = new GameObject(urpsOwnObject ? ApvDataCheck.PerSceneDataObjectName : "Lighting");
            go.hideFlags |= HideFlags.HideInHierarchy;
            SceneManager.MoveGameObjectToScene(go, _scene);
            if (!urpsOwnObject)
                go.AddComponent<BoxCollider>();
            var data = go.AddComponent<ProbeVolumePerSceneData>();
            typeof(ProbeVolumePerSceneData).GetField("serializedBakingSet", Any).SetValue(data, set);
            typeof(ProbeVolumePerSceneData).GetField("sceneGUID", Any).SetValue(data, SceneGuid);
            return data;
        }

        static List<BuildCheckIssue> Find(ProbeVolumePerSceneData data, bool projectUsesApv)
        {
            var issues = new List<BuildCheckIssue>();
            ApvDataCheck.Find(new[] { data }, projectUsesApv,
                go => BuildCheckTarget.ForObject(go, "Assets/Test.unity"), issues);
            return issues;
        }

        [Test]
        public void ApvData_WithNoCellsForItsScene_IsAnError_LikeTheAppRefusesIt()
        {
            var data = PerSceneData(BakingSet(cellBytes: 16L * N, sharedBytes: N, bricksBytes: 100L * BrickSize, listsTheScene: false));
            Assert.AreEqual(false, ApvBakedData.HasCellsForScene(data));

            var issues = Find(data, projectUsesApv: true);
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(BuildCheckSeverity.Error, issues[0].Severity);
            StringAssert.Contains("no Adaptive Probe Volumes cells for this scene", issues[0].Title);
        }

        [Test]
        public void Run_IgnoresInactivePerSceneData_LikeTheApp()
        {
            var data = PerSceneData(BakingSet(cellBytes: 0, sharedBytes: N, bricksBytes: 100L * BrickSize));
            data.gameObject.SetActive(false);
            var issues = new List<BuildCheckIssue>();
            using (var context = BuildCheckContext.ForScene(_scene, new[] { UnityEditor.BuildTarget.StandaloneWindows }, "Assets/Test.unity"))
                new ApvDataCheck().Run(context, issues);
            Assert.IsEmpty(issues);
        }

        [Test]
        public void LeftoverApvData_InALightProbeGroupsProject_IsAnErrorWithAFix()
        {
            var set = BakingSet(cellBytes: 16L * N, sharedBytes: N, bricksBytes: 100L * BrickSize);
            var issues = Find(PerSceneData(set), projectUsesApv: false);

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(BuildCheckSeverity.Error, issues[0].Severity);
            StringAssert.Contains("Light Probe Groups", issues[0].Title);
            StringAssert.Contains("'TestBakingSet'", issues[0].Details);
            Assert.IsNotNull(issues[0].Fix);
        }

        [Test]
        public void ApvData_InAnApvProject_PassesWhenLoadable_AndIsAnErrorWhenNot()
        {
            var good = BakingSet(cellBytes: 16L * N, sharedBytes: N, bricksBytes: 100L * BrickSize);
            Assert.IsEmpty(Find(PerSceneData(good), projectUsesApv: true));

            var bad = BakingSet(cellBytes: 0, sharedBytes: N, bricksBytes: 100L * BrickSize);
            var issues = Find(PerSceneData(bad), projectUsesApv: true);
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(BuildCheckSeverity.Error, issues[0].Severity);
            StringAssert.Contains("can't be loaded", issues[0].Title);
        }

        [Test]
        public void TheHiddenPerSceneObject_IsAmongTheObjectsThatShip()
        {
            var data = PerSceneData(BakingSet(cellBytes: 16L * N, sharedBytes: N, bricksBytes: 100L * BrickSize));
            using (var context = BuildCheckContext.ForScene(_scene, new[] { UnityEditor.BuildTarget.StandaloneWindows }, "Assets/Test.unity"))
                CollectionAssert.Contains(context.Components<ProbeVolumePerSceneData>().ToList(), data);
        }

        [Test]
        public void TheFix_RemovesUrpsObject_ButOnlyTheComponentFromAnObjectThatHasMore()
        {
            var set = BakingSet(cellBytes: 16L * N, sharedBytes: N, bricksBytes: 100L * BrickSize);
            var own = PerSceneData(set).gameObject;
            var shared = PerSceneData(set, urpsOwnObject: false).gameObject;

            Assert.IsTrue(ApvDataCheck.RemovePerSceneData(new[] { own, shared }));

            Assert.IsTrue(own == null, "URP's own object goes.");
            Assert.IsTrue(shared != null && shared.GetComponent<BoxCollider>() != null, "Another object keeps its other components.");
            Assert.IsNull(shared.GetComponent<ProbeVolumePerSceneData>());
            Assert.IsFalse(ApvDataCheck.RemovePerSceneData(new[] { shared }), "Nothing left to remove.");
        }
    }
}
