#if GREENFIELD_PROJECT
using System;
using System.Collections.Generic;
using System.Linq;
using BS;
using Unity.VisualScripting;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace BS.SDKEditor
{
    /// <summary>What a runtime override means for the machine currently in the scene.</summary>
    public enum SyncState
    {
        /// <summary>The scene already holds this exact graph. Nothing to do.</summary>
        Applied,

        /// <summary>The scene's graph differs and the override was based on what the scene still has.</summary>
        Pending,

        /// <summary>
        /// The override was based on a graph the scene no longer holds — someone edited the
        /// authored graph in Unity after the runtime edit was made. Applying discards that work.
        /// </summary>
        StaleBase,

        /// <summary>No machine in the scene matches. Usually a deleted object.</summary>
        Unmatched,
    }

    /// <summary>How to write an override into a machine whose graph is a shared macro asset.</summary>
    public enum MacroResolution
    {
        /// <summary>Move this machine to its own embedded copy; the shared asset is untouched.</summary>
        ConvertToEmbed,

        /// <summary>Write into the shared asset. Every machine using it changes.</summary>
        OverwriteAsset,

        /// <summary>Write a new asset beside the original and point only this machine at it.</summary>
        SaveAsNewAsset,
    }

    /// <summary>One override matched against the scene.</summary>
    public class SyncCandidate
    {
        public PersistedScriptGraphEntry entry;
        public ScriptMachine machine;
        public SyncState state;

        /// <summary>Machines other than this one that share the same macro asset.</summary>
        public int macroSharedWith;

        public bool IsMacro => machine != null && machine.nest?.source == GraphSource.Macro;
        public MacroResolution resolution = MacroResolution.ConvertToEmbed;
        public bool selected;

        public string Label => entry?.title
            ?? (machine != null ? MachineDirectory.FriendlyTitle(machine) : entry?.bid ?? "(unknown)");
    }

    /// <summary>
    /// Matching runtime script-graph overrides against the machines in the open scene, and writing
    /// the chosen ones in.
    /// </summary>
    /// <remarks>
    /// Separated from the window so the same logic serves the window, the quiet
    /// check-on-scene-open, and the Builder's pre-upload confirmation without three readings of
    /// what "already applied" means.
    /// </remarks>
    public static class ScriptGraphSync
    {
        /// <summary>
        /// Match every override against the scene.
        /// </summary>
        /// <remarks>
        /// Matching is on <c>BSObjectId.Id</c>, not on the runtime unityId the override also
        /// carries: the runtime id is a per-session <c>GetInstanceID()</c> and means nothing here,
        /// whereas BSObjectId.Id is serialized into the scene and is what the runtime resolved the
        /// machine by in the first place. Hierarchy path is the fallback, for objects whose bid was
        /// regenerated after a duplicate collision.
        /// </remarks>
        public static List<SyncCandidate> Match(PersistedScriptGraphs file)
        {
            var candidates = new List<SyncCandidate>();
            if (file?.entries == null) return candidates;

            var machines = MachineDirectory.AllMachines();
            var byBid = new Dictionary<string, List<ScriptMachine>>(StringComparer.Ordinal);
            foreach (var machine in machines)
            {
                var bid = machine.GetComponent<BSObjectId>()?.Id;
                if (string.IsNullOrEmpty(bid)) continue;
                if (!byBid.TryGetValue(bid, out var list)) byBid[bid] = list = new List<ScriptMachine>();
                list.Add(machine);
            }

            // How many machines share each macro, so the UI can say what an overwrite would touch.
            var macroUsers = new Dictionary<UnityObject, int>();
            foreach (var machine in machines)
            {
                if (machine.nest?.source != GraphSource.Macro || machine.nest.macro == null) continue;
                macroUsers.TryGetValue(machine.nest.macro, out var count);
                macroUsers[machine.nest.macro] = count + 1;
            }

            foreach (var entry in file.entries)
            {
                if (entry == null) continue;
                var candidate = new SyncCandidate { entry = entry };
                candidate.machine = Resolve(entry, byBid, machines);
                Classify(candidate);
                if (candidate.IsMacro && macroUsers.TryGetValue(candidate.machine.nest.macro, out var users))
                    candidate.macroSharedWith = Math.Max(0, users - 1);
                candidate.selected = candidate.state == SyncState.Pending;
                candidates.Add(candidate);
            }
            return candidates;
        }

        static ScriptMachine Resolve(PersistedScriptGraphEntry entry,
                                     Dictionary<string, List<ScriptMachine>> byBid,
                                     List<ScriptMachine> all)
        {
            if (!string.IsNullOrEmpty(entry.bid) && byBid.TryGetValue(entry.bid, out var onObject))
            {
                foreach (var machine in onObject)
                    if (MachineDirectory.MachineIndex(machine) == entry.machineIndex) return machine;
            }
            if (!string.IsNullOrEmpty(entry.path))
            {
                foreach (var machine in all)
                {
                    if (MachineDirectory.HierarchyPath(machine.transform) == entry.path
                        && MachineDirectory.MachineIndex(machine) == entry.machineIndex)
                        return machine;
                }
            }
            return null;
        }

        static void Classify(SyncCandidate candidate)
        {
            if (candidate.machine == null) { candidate.state = SyncState.Unmatched; return; }

            var overrideJson = (string)candidate.entry.envelope?["uvsJson"];
            if (string.IsNullOrEmpty(overrideJson)) { candidate.state = SyncState.Unmatched; return; }

            var sceneJson = SerializeMachineGraph(candidate.machine);
            if (sceneJson == null) { candidate.state = SyncState.Unmatched; return; }

            var sceneRef = ScriptGraphSession.HashRef(sceneJson);
            if (sceneRef == ScriptGraphSession.HashRef(overrideJson)) { candidate.state = SyncState.Applied; return; }

            // The override records the hash of the graph it was edited FROM. If that no longer
            // matches what the scene holds, the authored graph has moved on independently and
            // applying would silently drop whatever changed in Unity.
            var basedOn = candidate.entry.baseGraphRef;
            candidate.state = string.IsNullOrEmpty(basedOn) || basedOn == sceneRef
                ? SyncState.Pending
                : SyncState.StaleBase;
        }

        /// <summary>
        /// The machine's current graph in the same shape an override stores — an asset-level
        /// <c>{"graph":...}</c> document, so the two hashes are comparable.
        /// </summary>
        public static string SerializeMachineGraph(ScriptMachine machine)
        {
            var graph = machine.graph;
            if (graph == null) return null;
            var asset = ScriptableObject.CreateInstance<ScriptGraphAsset>();
            asset.hideFlags = HideFlags.DontSave;
            try
            {
                // A clone, never the live graph: Serialize walks and can touch the object it is
                // given, and the machine in the open scene is the user's own work.
                asset.graph = ScriptGraphSession.CloneGraph(graph);
                return ((object)asset).Serialize(true).json;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Banter] Could not serialize the graph on '{machine.gameObject.name}': {e.Message}");
                return null;
            }
            finally
            {
                UnityObject.DestroyImmediate(asset);
            }
        }

        /// <summary>
        /// Write one override into its machine. Returns the record to keep on the world link, or
        /// null when nothing was applied.
        /// </summary>
        public static SyncedGraphRecord Apply(SyncCandidate candidate)
        {
            if (candidate?.machine == null) return null;
            var json = (string)candidate.entry.envelope?["uvsJson"];
            if (string.IsNullOrEmpty(json)) return null;

            var refs = Array.Empty<UnityObject>();
            var loaded = ScriptableObject.CreateInstance<ScriptGraphAsset>();
            loaded.hideFlags = HideFlags.DontSave;
            object boxed = loaded;
            try
            {
                new SerializationData(json, refs).DeserializeInto(ref boxed, true);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Banter] '{candidate.Label}' could not be read: {e.Message}");
                UnityObject.DestroyImmediate(loaded);
                return null;
            }

            var graph = ((ScriptGraphAsset)boxed).graph;
            if (graph == null)
            {
                Debug.LogError($"[Banter] '{candidate.Label}' contained no graph.");
                UnityObject.DestroyImmediate(loaded);
                return null;
            }

            // A blocked graph must never reach a machine — the runtime refuses to run one, so
            // letting it into the scene would only fail later and further from the cause.
            if (BanterStubsAllowed.IsBlocked(graph))
            {
                Debug.LogError($"[Banter] '{candidate.Label}' uses members that are not allowed in Banter; not applied.");
                UnityObject.DestroyImmediate(loaded);
                return null;
            }

            var machine = candidate.machine;
            var isMacro = candidate.IsMacro;
            var resolution = isMacro ? candidate.resolution : MacroResolution.ConvertToEmbed;

            try
            {
                switch (resolution)
                {
                    case MacroResolution.OverwriteAsset:
                    {
                        var macro = machine.nest.macro;
                        Undo.RecordObject(macro, "Sync Runtime Script Graph");
                        macro.graph = graph;
                        EditorUtility.SetDirty(macro);
                        AssetDatabase.SaveAssetIfDirty(macro);
                        break;
                    }
                    case MacroResolution.SaveAsNewAsset:
                    {
                        var path = NewAssetPathFor(machine);
                        if (path == null) return null;
                        var created = ScriptableObject.CreateInstance<ScriptGraphAsset>();
                        created.graph = graph;
                        AssetDatabase.CreateAsset(created, path);
                        AssetDatabase.SaveAssets();
                        Undo.RecordObject(machine, "Sync Runtime Script Graph");
                        machine.nest.SwitchToMacro(created);
                        break;
                    }
                    default:
                    {
                        Undo.RecordObject(machine, "Sync Runtime Script Graph");
                        machine.nest.SwitchToEmbed(graph);
                        break;
                    }
                }

                EditorUtility.SetDirty(machine);
                EditorSceneManager.MarkSceneDirty(machine.gameObject.scene);
            }
            finally
            {
                UnityObject.DestroyImmediate(loaded);
            }

            return new SyncedGraphRecord
            {
                entryId = candidate.entry.id,
                bid = candidate.entry.bid,
                machineIndex = candidate.entry.machineIndex,
                title = candidate.Label,
                syncedAt = DateTime.UtcNow.ToString("o"),
                sourceSavedAt = candidate.entry.savedAt,
            };
        }

        static string NewAssetPathFor(ScriptMachine machine)
        {
            var original = machine.nest?.macro;
            var directory = original != null
                ? System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(original))
                : "Assets";
            if (string.IsNullOrEmpty(directory)) directory = "Assets";
            var baseName = (original != null ? original.name : machine.gameObject.name) + "_edited";
            var path = EditorUtility.SaveFilePanelInProject(
                "Save the synced graph", baseName, "asset",
                "Where should the edited copy of this graph live?", directory);
            return string.IsNullOrEmpty(path) ? null : path;
        }
    }
}
#endif
