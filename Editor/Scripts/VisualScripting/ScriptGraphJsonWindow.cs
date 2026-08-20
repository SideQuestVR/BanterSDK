#if GREENFIELD_PROJECT
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace BS.SDKEditor
{
    /// <summary>
    /// Paste-JSON round-trip tool: paste a runtime-saved envelope (or raw UVS FullSerializer
    /// JSON), load it into a ScriptGraphAsset, inspect the round-trip report, then assign it to
    /// a ScriptMachine or save it as a project asset. This is the backwards-compat test bench
    /// between UVS serialization and the Shane's Editor pipeline.
    /// </summary>
    public class ScriptGraphJsonWindow : EditorWindow
    {
        [MenuItem("Banter/Visual Scripting/Script Graph JSON Tool")]
        static void Open()
        {
            GetWindow<ScriptGraphJsonWindow>("Script Graph JSON");
        }

        string json = "";
        Vector2 jsonScroll;
        ScriptGraphAsset loaded;
        string report = "";
        List<string> warnings = new List<string>();

        void OnGUI()
        {
            EditorGUILayout.LabelField("Paste an envelope ({\"uvsJson\":...}) or raw UVS JSON ({\"graph\":...}):",
                EditorStyles.wordWrappedLabel);
            jsonScroll = EditorGUILayout.BeginScrollView(jsonScroll, GUILayout.Height(160));
            json = EditorGUILayout.TextArea(json, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Paste From Clipboard"))
                {
                    json = EditorGUIUtility.systemCopyBuffer ?? "";
                    GUI.FocusControl(null);
                }
                if (GUILayout.Button("Load / Validate"))
                {
                    Load();
                }
                if (GUILayout.Button("Clear"))
                {
                    json = "";
                    report = "";
                    warnings.Clear();
                    DisposeLoaded();
                    GUI.FocusControl(null);
                }
            }

            if (!string.IsNullOrEmpty(report))
            {
                EditorGUILayout.HelpBox(report, warnings.Count > 0 ? MessageType.Warning : MessageType.Info);
            }
            foreach (var warning in warnings)
            {
                EditorGUILayout.HelpBox(warning, MessageType.Warning);
            }

            using (new EditorGUI.DisabledScope(loaded == null))
            {
                var machine = (Selection.activeGameObject != null)
                    ? Selection.activeGameObject.GetComponent<ScriptMachine>()
                    : null;
                using (new EditorGUI.DisabledScope(machine == null))
                {
                    var label = machine != null
                        ? $"Assign To Selected ScriptMachine ({machine.gameObject.name})"
                        : "Assign To Selected ScriptMachine (none selected)";
                    if (GUILayout.Button(label))
                    {
                        AssignToMachine(machine);
                    }
                }
                if (GUILayout.Button("Save As .asset..."))
                {
                    SaveAsAsset();
                }
            }
        }

        void Load()
        {
            DisposeLoaded();
            warnings.Clear();
            report = "";

            try
            {
                string uvsJson;
                UnityObject[] refs;
                var trimmed = json.Trim();
                var parsed = JObject.Parse(trimmed);

                if (parsed.ContainsKey("uvsJson"))
                {
                    var envelope = JsonConvert.DeserializeObject<GraphEnvelope>(trimmed);
                    var refWarnings = new List<ObjectRefWarning>();
                    refs = ObjectRefResolver.Resolve(envelope.objectRefDescriptors, refWarnings);
                    foreach (var refWarning in refWarnings)
                    {
                        warnings.Add($"Unresolved reference in slot {refWarning.slot}: "
                            + JsonConvert.SerializeObject(refWarning.descriptor));
                    }
                    uvsJson = envelope.uvsJson;
                }
                else
                {
                    // Raw FullSerializer JSON. Accept both the asset shape ({"graph":...}) and a
                    // bare FlowGraph by wrapping the latter.
                    uvsJson = parsed.ContainsKey("graph") ? trimmed : "{\"graph\":" + trimmed + "}";
                    refs = Array.Empty<UnityObject>();
                    if (!parsed.ContainsKey("graph"))
                    {
                        warnings.Add("Input looked like a bare FlowGraph; wrapped it in {\"graph\":...}. "
                            + "Unity object references (integer slots) cannot resolve without an envelope.");
                    }
                }

                var asset = ScriptableObject.CreateInstance<ScriptGraphAsset>();
                asset.name = "PastedScriptGraph";
                object boxed = asset;
                new SerializationData(uvsJson, refs).DeserializeInto(ref boxed, true);
                loaded = asset;

                var graph = asset.graph;
                var invalid = graph.elements.OfType<InvalidConnection>().Count();
                var undefined = graph.units.Count(u => u.failedToDefine);
                report = $"Loaded: {graph.units.Count} units, {graph.controlConnections.Count} control + "
                    + $"{graph.valueConnections.Count} value connections"
                    + (string.IsNullOrEmpty(graph.title) ? "" : $", title '{graph.title}'");
                if (invalid > 0)
                {
                    warnings.Add($"{invalid} connection(s) degraded to InvalidConnection - ports referenced "
                        + "by the JSON no longer exist on their units (version drift).");
                }
                if (undefined > 0)
                {
                    warnings.Add($"{undefined} unit(s) failed to define.");
                }
                if (BanterStubsAllowed.IsBlocked(graph))
                {
                    warnings.Add("Graph contains elements outside the AOT allow-list; it will be "
                        + "refused at runtime.");
                }
            }
            catch (Exception e)
            {
                report = "Failed to load: " + e.Message;
                DisposeLoaded();
            }
        }

        void AssignToMachine(ScriptMachine machine)
        {
            // Clone so the window keeps its own copy whatever happens to the machine later.
            var data = ((object)loaded).Serialize(true);
            var assigned = ScriptableObject.CreateInstance<ScriptGraphAsset>();
            assigned.name = loaded.name;
            object boxed = assigned;
            new SerializationData(data.json, data.objectReferences).DeserializeInto(ref boxed, true);

            Undo.RecordObject(machine, "Assign Pasted Script Graph");
            machine.nest.SwitchToMacro(assigned);
            EditorUtility.SetDirty(machine);
            report = $"Assigned to {machine.gameObject.name}. The asset is in-memory only - use "
                + "'Save As .asset...' to persist it.";
        }

        void SaveAsAsset()
        {
            var path = EditorUtility.SaveFilePanelInProject("Save Script Graph Asset",
                loaded.name, "asset", "Where to save the loaded graph");
            if (string.IsNullOrEmpty(path)) return;

            var data = ((object)loaded).Serialize(true);
            var saved = ScriptableObject.CreateInstance<ScriptGraphAsset>();
            object boxed = saved;
            new SerializationData(data.json, data.objectReferences).DeserializeInto(ref boxed, true);
            AssetDatabase.CreateAsset(saved, path);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(saved);
            // Double-click the asset (or use AssetDatabase.OpenAsset) to open it in the UVS
            // graph window for a visual diff.
            report = $"Saved to {path}.";
        }

        void DisposeLoaded()
        {
            if (loaded != null)
            {
                DestroyImmediate(loaded);
                loaded = null;
            }
        }

        void OnDisable()
        {
            DisposeLoaded();
        }
    }
}
#endif
