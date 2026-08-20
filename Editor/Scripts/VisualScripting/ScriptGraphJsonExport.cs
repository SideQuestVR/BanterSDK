#if BANTER_VISUAL_SCRIPTING
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;

namespace BS.SDKEditor
{
    /// <summary>
    /// Exports script graphs as the same envelope JSON the runtime editor saves - the fixture
    /// producer for backwards-compat testing between UVS and the Shane's Editor pipeline.
    /// </summary>
    public static class ScriptGraphJsonExport
    {
        [MenuItem("CONTEXT/ScriptMachine/Export Graph JSON...")]
        static void ExportFromMachine(MenuCommand command)
        {
            var machine = (ScriptMachine)command.context;
            if (machine.graph == null)
            {
                EditorUtility.DisplayDialog("Export Graph JSON", "This machine has no graph.", "OK");
                return;
            }
            var envelope = BuildEnvelope(machine.graph, new TargetRef
            {
                bid = machine.GetComponent<BSObjectId>()?.Id,
                machineIndex = MachineDirectory.MachineIndex(machine),
                path = MachineDirectory.HierarchyPath(machine.transform),
            }, MachineDirectory.FriendlyTitle(machine));
            WriteEnvelope(envelope, machine.gameObject.name);
        }

        [MenuItem("Assets/Banter/Export Script Graph JSON...", true)]
        static bool ValidateExportFromAsset()
        {
            return Selection.activeObject is ScriptGraphAsset;
        }

        [MenuItem("Assets/Banter/Export Script Graph JSON...")]
        static void ExportFromAsset()
        {
            var asset = (ScriptGraphAsset)Selection.activeObject;
            var data = ((object)asset).Serialize(true);
            var envelope = new GraphEnvelope
            {
                uvsJson = data.json,
                objectRefDescriptors = ObjectRefResolver.Describe(data.objectReferences),
                meta = new GraphEnvelopeMeta
                {
                    baseGraphTitle = string.IsNullOrEmpty(asset.graph?.title) ? asset.name : asset.graph.title,
                    baseGraphRef = ScriptGraphSession.HashRef(data.json),
                    savedAt = System.DateTime.UtcNow.ToString("o"),
                    sdkVersion = Application.version,
                    nodeCount = asset.graph?.units.Count ?? 0,
                },
            };
            WriteEnvelope(envelope, asset.name);
        }

        public static GraphEnvelope BuildEnvelope(FlowGraph graph, TargetRef target, string title)
        {
            // Clone through an asset wrapper so the envelope's uvsJson has the same shape a
            // runtime save produces ({"graph":{...}}).
            var clone = ScriptGraphSession.CloneGraph(graph);
            var asset = ScriptableObject.CreateInstance<ScriptGraphAsset>();
            asset.graph = clone;
            var data = ((object)asset).Serialize(true);
            var envelope = new GraphEnvelope
            {
                uvsJson = data.json,
                objectRefDescriptors = ObjectRefResolver.Describe(data.objectReferences),
                meta = new GraphEnvelopeMeta
                {
                    target = target,
                    baseGraphTitle = string.IsNullOrEmpty(title) ? (graph.title ?? "") : title,
                    baseGraphRef = ScriptGraphSession.HashRef(data.json),
                    savedAt = System.DateTime.UtcNow.ToString("o"),
                    sdkVersion = Application.version,
                    nodeCount = graph.units.Count,
                },
            };
            Object.DestroyImmediate(asset);
            return envelope;
        }

        static void WriteEnvelope(GraphEnvelope envelope, string suggestedName)
        {
            var path = EditorUtility.SaveFilePanel("Export Script Graph JSON",
                Directory.GetCurrentDirectory(), suggestedName + ".graph", "json");
            if (string.IsNullOrEmpty(path)) return;
            File.WriteAllText(path, JsonConvert.SerializeObject(envelope, Formatting.Indented),
                new UTF8Encoding(false));
            Debug.Log($"[Banter] Exported script graph envelope ({envelope.meta.nodeCount} units) to {path}");
        }
    }
}
#endif
