#if GREENFIELD_PROJECT
using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;
using UnityEngine;

namespace BS
{
    /// <summary>
    /// Wire identity of one ScriptMachine: the BSObjectId of its GameObject plus the machine's
    /// index among that object's ScriptMachines. <c>path</c> rides along as a fuzzy fallback
    /// for objects whose bid has changed or was never assigned.
    /// </summary>
    public class TargetRef
    {
        public string bid;
        public int machineIndex;
        public string path;
    }

    public class MachineDescriptor
    {
        public string bid;
        public int machineIndex;
        public string objectName;
        public string path;
        public bool active;
        public string source;
        public string graphTitle;
        public int unitCount;
        public bool blocked;
        public bool paused;

        /// <summary>
        /// Content hash of the machine's current graph, in the same form an envelope's
        /// baseGraphRef takes — so a saved override can be told apart from one whose base has
        /// since changed. Null unless the caller asked for it: producing one serializes the whole
        /// graph, which is far too expensive for the list the picker polls.
        /// </summary>
        public string graphRef;
    }

    public static class MachineDirectory
    {
        public static string HierarchyPath(Transform transform)
        {
            var parts = new List<string>();
            while (transform != null)
            {
                parts.Add(transform.name);
                transform = transform.parent;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }

        public static List<ScriptMachine> AllMachines()
        {
            return Object.FindObjectsByType<ScriptMachine>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .OrderBy(m => HierarchyPath(m.transform), System.StringComparer.Ordinal)
                .ThenBy(MachineIndex)
                .ToList();
        }

        public static int MachineIndex(ScriptMachine machine)
        {
            return System.Array.IndexOf(machine.GetComponents<ScriptMachine>(), machine);
        }

        public static MachineDescriptor Describe(ScriptMachine machine) => Describe(machine, false);

        public static MachineDescriptor Describe(ScriptMachine machine, bool withGraphRef)
        {
            var graph = machine.graph;
            return new MachineDescriptor
            {
                graphRef = withGraphRef ? GraphRef(machine) : null,
                bid = machine.GetComponent<BSObjectId>()?.Id ?? "",
                machineIndex = MachineIndex(machine),
                objectName = machine.gameObject.name,
                path = HierarchyPath(machine.transform),
                active = machine.isActiveAndEnabled,
                source = machine.nest?.source == GraphSource.Macro ? "macro" : "embed",
                graphTitle = FriendlyTitle(machine),
                unitCount = graph?.units.Count ?? 0,
                blocked = graph != null && IsBlockedQuiet(graph),
                paused = machine.GraphPaused,
            };
        }

        /// <summary>
        /// The machine's current graph as a content hash, comparable with an envelope's
        /// baseGraphRef. Serializes a CLONE, never the live graph - the machine is running.
        /// </summary>
        static string GraphRef(ScriptMachine machine)
        {
            var graph = machine.graph;
            if (graph == null) return null;
            ScriptGraphAsset asset = null;
            try
            {
                asset = ScriptableObject.CreateInstance<ScriptGraphAsset>();
                asset.hideFlags = HideFlags.DontSave;
                asset.graph = ScriptGraphSession.CloneGraph(graph);
                return ScriptGraphSession.HashRef(((object)asset).Serialize(true).json);
            }
            catch (System.Exception e)
            {
                // Not fatal: a machine whose hash cannot be taken simply cannot be staleness-checked,
                // and the caller treats a missing hash as "cannot tell" rather than as "stale".
                Debug.LogWarning($"[Banter] Could not hash the graph on '{machine.gameObject.name}': {e.Message}");
                return null;
            }
            finally
            {
                if (asset != null) Object.DestroyImmediate(asset);
            }
        }

        /// <summary>
        /// BanterStubsAllowed.IsBlocked semantics without its per-element Debug.LogError -
        /// the picker polls list, and a space legitimately containing one blocked machine
        /// must not spam the console on every poll.
        /// </summary>
        static bool IsBlockedQuiet(FlowGraph graph)
        {
            foreach (var element in graph.elements)
            {
                if (element is StickyNote || element is GraphGroup) continue;
                var id = element.GetAnalyticsIdentifier()?.Identifier?.Split('(')[0].Trim();
                if (id == null) continue;
                if (id.StartsWith("Unity.VisualScripting.")
                    || id.StartsWith("BS.VisualScripting.")
                    || id.StartsWith("Banter.VisualScripting.")
                    || id.StartsWith("PicaVoxel.VisualScripting.")) continue;
                if (!BanterStubsAllowed.IsAllowedIdentifier(id)) return true;
            }
            return false;
        }

        public static string FriendlyTitle(ScriptMachine machine)
        {
            var title = machine.graph?.title;
            if (!string.IsNullOrEmpty(title)) return title;
            if (machine.nest?.source == GraphSource.Macro && machine.nest.macro != null)
            {
                return machine.nest.macro.name;
            }
            return machine.gameObject.name;
        }

        public static ScriptMachine Resolve(TargetRef target)
        {
            if (target == null) return null;

            if (!string.IsNullOrEmpty(target.bid))
            {
                var scene = BSScene.Instance();
                var obj = scene != null ? scene.GetObjectByBid(target.bid) : default;
                if (obj.gameObject != null)
                {
                    var machines = obj.gameObject.GetComponents<ScriptMachine>();
                    if (target.machineIndex >= 0 && target.machineIndex < machines.Length)
                    {
                        return machines[target.machineIndex];
                    }
                }
            }

            // Fuzzy fallback: hierarchy path. Covers inactive objects and bids that never
            // resolved; first match wins.
            if (!string.IsNullOrEmpty(target.path))
            {
                foreach (var machine in AllMachines())
                {
                    if (HierarchyPath(machine.transform) == target.path
                        && MachineIndex(machine) == target.machineIndex)
                    {
                        return machine;
                    }
                }
            }

            return null;
        }
    }
}
#endif
