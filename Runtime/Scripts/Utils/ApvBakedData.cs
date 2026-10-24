using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;

namespace BS
{
    /// <summary>
    /// Whether a baked Adaptive Probe Volumes (APV) baking set can be loaded without reading past its data.
    ///
    /// A player built with "Probe Volume Disable Streaming Assets" (the client) reads a world's APV data straight out
    /// of the bundle's TextAssets. <c>ProbeVolumeBakingSet.ResolveSharedCellData</c> /
    /// <c>ResolvePerScenarioCellData</c> slice them cell after cell at offsets taken from the baking set's own
    /// descriptors, and <c>ProbeReferenceVolume.UpdatePool</c> copies them chunk by chunk with the runtime's chunk
    /// size. None of that is bounds-checked in a release player, so a file shorter than its descriptors (left over
    /// from an older bake, say) crashes the player natively instead of throwing. Duke's Cinema did that to Windows
    /// clients in October 2026.
    ///
    /// This repeats URP's arithmetic: every data file must exist and hold at least the bytes its cells need, and
    /// the bake's chunk sizes must be the ones this URP version reads. The client checks before it switches a world
    /// to APV, and the Builder before a world is published. Everything is read by reflection (the baking set's
    /// fields are internal); while <see cref="CanCheck"/> is false, nothing passes.
    ///
    /// It checks sizes, not contents: data of the right size can still be wrong (URP indexes its own buffers with
    /// brick positions it only Debug.Asserts), so this keeps creators' stale bakes from crashing players, not a
    /// crafted bundle. It is also slightly stricter than URP: it checks every lighting scenario, where URP only copies
    /// the active one, so a world whose inactive scenario is short loses APV lighting instead of loading it.
    /// </summary>
    public static class ApvBakedData
    {
        /// <summary>
        /// Finds what would make <paramref name="bakingSet"/> unsafe to load on a pipeline using
        /// <paramref name="shBands"/>. Null when it is safe; otherwise one sentence naming the problem.
        /// </summary>
        public static string FindProblem(ProbeVolumeBakingSet bakingSet, ProbeVolumeSHBands shBands)
        {
            if (bakingSet == null)
                return "there is no baking set.";
            if (!CanCheck)
                return "this URP version's probe volume internals changed, so its data can't be checked.";
            Layout layout;
            try
            {
                layout = Read(bakingSet);
            }
            catch (Exception e)
            {
                return $"its data couldn't be read ({e.GetType().Name}: {e.Message}).";
            }
            return FindProblem(layout, shBands, s_RuntimeChunkSizeInBricks, s_RuntimeChunkSizeInProbes, s_BrickSize);
        }

        /// <summary>Whether this URP version's internals are where <see cref="FindProblem(ProbeVolumeBakingSet, ProbeVolumeSHBands)"/> reads them.</summary>
        public static bool CanCheck => s_Reflection.Value;

        /// <summary>
        /// Whether the baking set of <paramref name="data"/> lists cells for the scene GUID <paramref name="data"/> carries,
        /// which is what a player looks the scene up by. False makes <c>ProbeReferenceVolume.LoadCells</c> throw every frame
        /// in a player without streaming assets (the client refuses such a world). Null when there is no baking set, or the
        /// internals moved in this URP version.
        /// </summary>
        public static bool? HasCellsForScene(ProbeVolumePerSceneData data)
        {
            if (data == null || data.bakingSet == null || s_SceneGuid?.FieldType != typeof(string) ||
                s_CellList?.ReturnType != typeof(List<int>))
                return null;
            try
            {
                var sceneGuid = s_SceneGuid.GetValue(data) as string;
                return s_CellList.Invoke(data.bakingSet, new object[] { sceneGuid }) != null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        static readonly FieldInfo s_SceneGuid =
            typeof(ProbeVolumePerSceneData).GetField("sceneGUID", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly MethodInfo s_CellList =
            typeof(ProbeVolumeBakingSet).GetMethod("GetSceneCellIndexList", BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(string) }, null);

        // ---- the check, on plain numbers --------------------------------------------------------------------------

        internal const long Missing = -1;

        /// <summary>What a baking set says about its data, and how big its data files are (<see cref="Missing"/>: no file).</summary>
        internal sealed class Layout
        {
            public int ChunkSizeInBricks;
            public int L0ChunkSize;
            public int L1ChunkSize;
            public int L2TextureChunkSize;
            public int ProbeOcclusionChunkSize;
            public int SharedValidityMaskChunkSize;
            public int SharedSkyOcclusionL0L1ChunkSize;
            public int SharedSkyShadingDirectionIndicesChunkSize;
            public int BakedMaskCount = 1;
            public bool BakedProbeOcclusion;
            public bool BakedSkyOcclusion;
            public bool BakedSkyShadingDirection;
            /// <summary>Cell descriptors by cell index.</summary>
            public readonly Dictionary<int, Cell> Cells = new Dictionary<int, Cell>();
            public long SharedBytes = Missing;
            public long BricksBytes = Missing;
            public readonly List<Scenario> Scenarios = new List<Scenario>();
        }

        internal struct Cell
        {
            public int BricksCount;
            public int ShChunkCount;
        }

        internal sealed class Scenario
        {
            public string Name;
            public long CellBytes = Missing;
            public long OptionalBytes = Missing;
            public long OcclusionBytes = Missing;
            /// <summary>The cells this scenario has data for.</summary>
            public readonly HashSet<int> CellIndices = new HashSet<int>();
        }

        /// <param name="chunkSizeInBricks">The runtime's bricks per chunk (<c>ProbeBrickPool.GetChunkSizeInBrickCount</c>).</param>
        /// <param name="chunkSizeInProbes">The runtime's probes per chunk (<c>ProbeBrickPool.GetChunkSizeInProbeCount</c>).</param>
        /// <param name="brickSize">Bytes per <c>ProbeBrickIndex.Brick</c>.</param>
        internal static string FindProblem(Layout layout, ProbeVolumeSHBands shBands, int chunkSizeInBricks, int chunkSizeInProbes, int brickSize)
        {
            if (layout.Cells.Count == 0)
                return null; // Nothing gets loaded.

            // UpdatePool copies every chunk with the runtime's chunk size; a bake from another layout would be read off its end.
            int n = chunkSizeInProbes;
            if (layout.ChunkSizeInBricks != chunkSizeInBricks)
                return $"it was baked with {layout.ChunkSizeInBricks} bricks per chunk, but this version reads {chunkSizeInBricks}.";
            if (layout.L0ChunkSize != sizeof(ushort) * 4 * n || layout.L1ChunkSize != 4 * n)
                return $"its L0/L1 chunks are {layout.L0ChunkSize}/{layout.L1ChunkSize} bytes, but this version reads {sizeof(ushort) * 4 * n}/{4 * n}.";
            int validitySize = (layout.BakedMaskCount == 1 ? 1 : 4) * n;
            if (layout.SharedValidityMaskChunkSize != validitySize)
                return $"its validity chunks are {layout.SharedValidityMaskChunkSize} bytes, but this version reads {validitySize}.";
            if (layout.BakedSkyOcclusion && layout.SharedSkyOcclusionL0L1ChunkSize != sizeof(ushort) * 4 * n)
                return $"its sky occlusion chunks are {layout.SharedSkyOcclusionL0L1ChunkSize} bytes, but this version reads {sizeof(ushort) * 4 * n}.";
            if (layout.BakedSkyOcclusion && layout.BakedSkyShadingDirection && layout.SharedSkyShadingDirectionIndicesChunkSize != n)
                return $"its sky direction chunks are {layout.SharedSkyShadingDirectionIndicesChunkSize} bytes, but this version reads {n}.";
            bool l2 = shBands == ProbeVolumeSHBands.SphericalHarmonicsL2;
            if (l2 && layout.L2TextureChunkSize != 4 * n)
                return $"its L2 chunks are {layout.L2TextureChunkSize} bytes, but this version reads {4 * n}.";
            if (layout.BakedProbeOcclusion && layout.ProbeOcclusionChunkSize != 4 * n)
                return $"its probe occlusion chunks are {layout.ProbeOcclusionChunkSize} bytes, but this version reads {4 * n}.";

            long chunks = 0;
            long bricks = 0;
            foreach (var pair in layout.Cells)
            {
                var cell = pair.Value;
                if (cell.ShChunkCount < 0 || cell.BricksCount < 0 || (long)cell.BricksCount > (long)cell.ShChunkCount * chunkSizeInBricks)
                    return $"cell {pair.Key} lists {cell.BricksCount} bricks in {cell.ShChunkCount} chunks.";
                chunks += cell.ShChunkCount;
                bricks += cell.BricksCount;
            }

            // Shared data: validity, then sky occlusion and its directions when they were baked (ResolveSharedCellData).
            long sharedPerChunk = layout.SharedValidityMaskChunkSize;
            if (layout.BakedSkyOcclusion)
            {
                sharedPerChunk += layout.SharedSkyOcclusionL0L1ChunkSize;
                if (layout.BakedSkyShadingDirection)
                    sharedPerChunk += layout.SharedSkyShadingDirectionIndicesChunkSize;
            }
            string problem = Short("shared data", layout.SharedBytes, chunks * sharedPerChunk)
                             ?? Short("brick data", layout.BricksBytes, bricks * brickSize);
            if (problem != null)
                return problem;

            // Per scenario: L0/L1, then L2 (only on an L2 pipeline) and probe occlusion (only when baked) (ResolvePerScenarioCellData).
            foreach (var scenario in layout.Scenarios)
            {
                long scenarioChunks = 0;
                foreach (int index in scenario.CellIndices)
                {
                    if (layout.Cells.TryGetValue(index, out var cell))
                        scenarioChunks += cell.ShChunkCount;
                }
                problem = Short($"scenario '{scenario.Name}' L0/L1 data", scenario.CellBytes,
                              scenarioChunks * (layout.L0ChunkSize + 2L * layout.L1ChunkSize))
                          ?? (l2 ? Short($"scenario '{scenario.Name}' L2 data", scenario.OptionalBytes, scenarioChunks * 4L * layout.L2TextureChunkSize) : null)
                          ?? (layout.BakedProbeOcclusion
                              ? Short($"scenario '{scenario.Name}' probe occlusion data", scenario.OcclusionBytes, scenarioChunks * layout.ProbeOcclusionChunkSize)
                              : null);
                if (problem != null)
                    return problem;
            }
            return null;
        }

        static string Short(string what, long have, long need)
        {
            if (have == Missing)
                return $"its {what} file is missing.";
            if (have < need)
                return $"its {what} file is {have:N0} bytes, but its cells need {need:N0}.";
            return null;
        }

        // ---- reading a baking set ---------------------------------------------------------------------------------

        const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        static FieldInfo s_CellDescs, s_SharedAsset, s_BricksAsset, s_Scenarios;
        static FieldInfo s_ChunkSizeInBricks, s_L0, s_L1, s_L2, s_ProbeOcclusion, s_SharedValidity, s_SharedSky, s_SharedSkyDirection;
        static FieldInfo s_BakedMaskCount, s_BakedProbeOcclusion, s_BakedSkyOcclusionValue, s_BakedSkyShadingDirectionValue;
        static FieldInfo s_CellShChunkCount, s_CellBricksCount;
        static FieldInfo s_ScenarioCellData, s_ScenarioOptionalData, s_ScenarioOcclusionData;
        static PropertyInfo s_StreamableAsset, s_StreamableCellDescs;
        static int s_RuntimeChunkSizeInBricks, s_RuntimeChunkSizeInProbes, s_BrickSize;

        static readonly Lazy<bool> s_Reflection = new Lazy<bool>(ResolveReflection);

        static bool ResolveReflection()
        {
            try
            {
                var set = typeof(ProbeVolumeBakingSet);
                var assembly = set.Assembly;
                var streamable = assembly.GetType("UnityEngine.Rendering.ProbeVolumeStreamableAsset");
                var cellDesc = typeof(ProbeReferenceVolume).GetNestedType("CellDesc", BindingFlags.NonPublic | BindingFlags.Public);
                var scenario = set.GetNestedType("PerScenarioDataInfo", BindingFlags.NonPublic | BindingFlags.Public);
                var pool = assembly.GetType("UnityEngine.Rendering.ProbeBrickPool");
                var brick = assembly.GetType("UnityEngine.Rendering.ProbeBrickIndex")?.GetNestedType("Brick", BindingFlags.NonPublic | BindingFlags.Public);
                if (streamable == null || cellDesc == null || scenario == null || pool == null || brick == null)
                    return false;

                s_CellDescs = set.GetField("cellDescs", Instance);
                s_SharedAsset = set.GetField("cellSharedDataAsset", Instance);
                s_BricksAsset = set.GetField("cellBricksDataAsset", Instance);
                s_Scenarios = set.GetField("scenarios", Instance);
                s_ChunkSizeInBricks = set.GetField("chunkSizeInBricks", Instance);
                s_L0 = set.GetField("L0ChunkSize", Instance);
                s_L1 = set.GetField("L1ChunkSize", Instance);
                s_L2 = set.GetField("L2TextureChunkSize", Instance);
                s_ProbeOcclusion = set.GetField("ProbeOcclusionChunkSize", Instance);
                s_SharedValidity = set.GetField("sharedValidityMaskChunkSize", Instance);
                s_SharedSky = set.GetField("sharedSkyOcclusionL0L1ChunkSize", Instance);
                s_SharedSkyDirection = set.GetField("sharedSkyShadingDirectionIndicesChunkSize", Instance);
                s_BakedMaskCount = set.GetField("bakedMaskCount", Instance);
                s_BakedProbeOcclusion = set.GetField("bakedProbeOcclusion", Instance);
                s_BakedSkyOcclusionValue = set.GetField("bakedSkyOcclusionValue", Instance);
                s_BakedSkyShadingDirectionValue = set.GetField("bakedSkyShadingDirectionValue", Instance);
                s_CellShChunkCount = cellDesc.GetField("shChunkCount", Instance);
                s_CellBricksCount = cellDesc.GetField("bricksCount", Instance);
                s_ScenarioCellData = scenario.GetField("cellDataAsset", Instance);
                s_ScenarioOptionalData = scenario.GetField("cellOptionalDataAsset", Instance);
                s_ScenarioOcclusionData = scenario.GetField("cellProbeOcclusionDataAsset", Instance);
                s_StreamableAsset = streamable.GetProperty("asset", Instance);
                s_StreamableCellDescs = streamable.GetProperty("streamableCellDescs", Instance);
                var bricksPerChunk = pool.GetMethod("GetChunkSizeInBrickCount", Static, null, Type.EmptyTypes, null);
                var probesPerChunk = pool.GetMethod("GetChunkSizeInProbeCount", Static, null, Type.EmptyTypes, null);

                var ints = new[] { s_ChunkSizeInBricks, s_L0, s_L1, s_L2, s_ProbeOcclusion, s_SharedValidity, s_SharedSky, s_SharedSkyDirection,
                                   s_BakedMaskCount, s_BakedSkyOcclusionValue, s_BakedSkyShadingDirectionValue, s_CellShChunkCount, s_CellBricksCount };
                foreach (var field in ints)
                {
                    if (field == null || field.FieldType != typeof(int))
                        return false;
                }
                if (s_BakedProbeOcclusion?.FieldType != typeof(bool)
                    || !typeof(IDictionary).IsAssignableFrom(s_CellDescs?.FieldType)
                    || !typeof(IDictionary).IsAssignableFrom(s_Scenarios?.FieldType)
                    || s_SharedAsset?.FieldType != streamable || s_BricksAsset?.FieldType != streamable
                    || s_ScenarioCellData?.FieldType != streamable || s_ScenarioOptionalData?.FieldType != streamable
                    || s_ScenarioOcclusionData?.FieldType != streamable
                    || s_StreamableAsset?.PropertyType != typeof(TextAsset)
                    || !typeof(IDictionary).IsAssignableFrom(s_StreamableCellDescs?.PropertyType)
                    || bricksPerChunk?.ReturnType != typeof(int) || probesPerChunk?.ReturnType != typeof(int))
                    return false;

                s_RuntimeChunkSizeInBricks = (int)bricksPerChunk.Invoke(null, null);
                s_RuntimeChunkSizeInProbes = (int)probesPerChunk.Invoke(null, null);
                s_BrickSize = UnsafeUtility.SizeOf(brick);
                return s_RuntimeChunkSizeInBricks > 0 && s_RuntimeChunkSizeInProbes > 0 && s_BrickSize > 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        static Layout Read(ProbeVolumeBakingSet set)
        {
            var layout = new Layout
            {
                ChunkSizeInBricks = (int)s_ChunkSizeInBricks.GetValue(set),
                L0ChunkSize = (int)s_L0.GetValue(set),
                L1ChunkSize = (int)s_L1.GetValue(set),
                L2TextureChunkSize = (int)s_L2.GetValue(set),
                ProbeOcclusionChunkSize = (int)s_ProbeOcclusion.GetValue(set),
                SharedValidityMaskChunkSize = (int)s_SharedValidity.GetValue(set),
                SharedSkyOcclusionL0L1ChunkSize = (int)s_SharedSky.GetValue(set),
                SharedSkyShadingDirectionIndicesChunkSize = (int)s_SharedSkyDirection.GetValue(set),
                BakedMaskCount = (int)s_BakedMaskCount.GetValue(set),
                BakedProbeOcclusion = (bool)s_BakedProbeOcclusion.GetValue(set),
                // ProbeVolumeBakingSet.bakedSkyOcclusion / bakedSkyShadingDirection: -1 (never set) and 0 are false.
                BakedSkyOcclusion = (int)s_BakedSkyOcclusionValue.GetValue(set) > 0,
                BakedSkyShadingDirection = (int)s_BakedSkyShadingDirectionValue.GetValue(set) > 0,
                SharedBytes = Bytes(s_SharedAsset.GetValue(set)),
                BricksBytes = Bytes(s_BricksAsset.GetValue(set)),
            };

            if (s_CellDescs.GetValue(set) is IDictionary cells)
            {
                foreach (DictionaryEntry entry in cells)
                {
                    if (entry.Value == null)
                        continue;
                    layout.Cells[(int)entry.Key] = new Cell
                    {
                        ShChunkCount = (int)s_CellShChunkCount.GetValue(entry.Value),
                        BricksCount = (int)s_CellBricksCount.GetValue(entry.Value),
                    };
                }
            }

            if (s_Scenarios.GetValue(set) is IDictionary scenarios)
            {
                foreach (DictionaryEntry entry in scenarios)
                {
                    if (entry.Value == null)
                        continue;
                    var cellData = s_ScenarioCellData.GetValue(entry.Value);
                    var scenario = new Scenario
                    {
                        Name = entry.Key as string,
                        CellBytes = Bytes(cellData),
                        OptionalBytes = Bytes(s_ScenarioOptionalData.GetValue(entry.Value)),
                        OcclusionBytes = Bytes(s_ScenarioOcclusionData.GetValue(entry.Value)),
                    };
                    if (cellData != null && s_StreamableCellDescs.GetValue(cellData) is IDictionary scenarioCells)
                    {
                        foreach (var key in scenarioCells.Keys)
                            scenario.CellIndices.Add((int)key);
                    }
                    layout.Scenarios.Add(scenario);
                }
            }
            return layout;
        }

        // The size of a streamable asset's TextAsset, which is all a player without streaming assets reads.
        static long Bytes(object streamable)
        {
            if (streamable == null)
                return Missing;
            var asset = s_StreamableAsset.GetValue(streamable) as TextAsset;
            return asset != null ? asset.dataSize : Missing;
        }
    }
}
