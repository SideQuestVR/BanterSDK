#if GREENFIELD_PROJECT
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BS;
using Unity.EditorCoroutines.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BS.SDKEditor
{
    /// <summary>
    /// Pulls script-graph edits made at runtime (in Shane's Editor) back into the project.
    /// </summary>
    /// <remarks>
    /// The world keeps those edits in <c>__persisted_script_graphs.json</c> and applies them over
    /// the authored graphs every time the space loads. That is fine as a live override and wrong as
    /// a permanent home: the graph the project builds still says something else, so the next person
    /// to open the scene sees the old graph and any Unity-side edit silently loses to the override.
    /// This window is how an edit stops being an override and becomes the graph.
    /// </remarks>
    public class ScriptGraphSyncWindow : EditorWindow
    {
        [MenuItem("Greenfield/Visual Scripting/Sync Runtime Script Graphs")]
        public static ScriptGraphSyncWindow Open()
        {
            var window = GetWindow<ScriptGraphSyncWindow>(true, "Sync Runtime Script Graphs");
            window.minSize = new Vector2(560, 320);
            window.Refresh();
            return window;
        }

        List<SyncCandidate> candidates;
        string status;
        bool busy;
        Vector2 scroll;

        void OnEnable() => titleContent = new GUIContent("Sync Runtime Script Graphs");

        void Refresh()
        {
            candidates = null;
            status = null;
            var link = WorldLinkStamp.FindActive();
            if (link == null || !link.IsLinked)
            {
                status = "This scene is not linked to a world yet.\n\n"
                       + "Open the Banter Builder, pick the world it publishes to, and this will find its runtime edits.";
                Repaint();
                return;
            }

            busy = true;
            status = "Reading " + PersistedWorldFiles.ScriptGraphsFile + "…";
            EditorCoroutineUtility.StartCoroutine(FetchRoutine(link.slug), this);
        }

        IEnumerator FetchRoutine(string slug)
        {
            yield return PersistedWorldFiles.Fetch(slug, PersistedWorldFiles.ScriptGraphsFile,
                json =>
                {
                    busy = false;
                    if (json == null)
                    {
                        status = "This world has no runtime script-graph edits.";
                        candidates = new List<SyncCandidate>();
                        Repaint();
                        return;
                    }
                    var file = PersistedWorldFiles.Parse<PersistedScriptGraphs>(json);
                    if (file == null)
                    {
                        status = "The world's " + PersistedWorldFiles.ScriptGraphsFile + " could not be read.";
                        Repaint();
                        return;
                    }
                    candidates = ScriptGraphSync.Match(file);
                    status = candidates.Count == 0 ? "This world has no runtime script-graph edits." : null;
                    Repaint();
                },
                error =>
                {
                    busy = false;
                    status = error;
                    Repaint();
                });
        }

        void OnGUI()
        {
            var link = WorldLinkStamp.FindActive();

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label(link != null && link.IsLinked
                    ? "World " + link.worldId + " · " + link.slug
                    : "No world linked", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(busy))
                {
                    if (GUILayout.Button("Refresh", EditorStyles.toolbarButton)) Refresh();
                }
            }

            if (!string.IsNullOrEmpty(status))
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(status, MessageType.Info);
                return;
            }
            if (candidates == null)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Loading…");
                return;
            }

            var stale = candidates.Count(c => c.state == SyncState.StaleBase);
            if (stale > 0)
            {
                EditorGUILayout.HelpBox(
                    $"{stale} edit(s) were made against a version of the graph this scene no longer has — "
                    + "someone changed the graph in Unity afterwards. Applying one replaces the Unity version.",
                    MessageType.Warning);
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var candidate in candidates) DrawCandidate(candidate);
            EditorGUILayout.EndScrollView();

            var selected = candidates.Count(c => c.selected && c.machine != null);
            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(selected == 0))
                {
                    if (GUILayout.Button(selected == 1 ? "Apply 1 graph" : $"Apply {selected} graphs", GUILayout.Height(24)))
                        ApplySelected();
                }
            }
        }

        void DrawCandidate(SyncCandidate candidate)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(candidate.machine == null || candidate.state == SyncState.Applied))
                    {
                        candidate.selected = EditorGUILayout.Toggle(candidate.selected, GUILayout.Width(18));
                    }
                    EditorGUILayout.LabelField(candidate.Label, EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(Describe(candidate.state), EditorStyles.miniLabel);
                }

                using (new EditorGUI.IndentLevelScope())
                {
                    if (candidate.machine != null)
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            EditorGUILayout.LabelField(
                                MachineDirectory.HierarchyPath(candidate.machine.transform)
                                + " · machine " + candidate.entry.machineIndex
                                + " · " + (candidate.IsMacro ? "macro" : "embedded"),
                                EditorStyles.miniLabel);
                            if (GUILayout.Button("Select", EditorStyles.miniButton, GUILayout.Width(56)))
                                Selection.activeGameObject = candidate.machine.gameObject;
                        }

                        if (candidate.IsMacro && candidate.selected) DrawMacroChoice(candidate);
                    }
                    else
                    {
                        EditorGUILayout.LabelField(
                            "No machine in this scene matches bid " + candidate.entry.bid
                            + " (machine " + candidate.entry.machineIndex + ").", EditorStyles.miniLabel);
                    }
                }
            }
        }

        void DrawMacroChoice(SyncCandidate candidate)
        {
            var shared = candidate.macroSharedWith;
            EditorGUILayout.HelpBox(
                shared > 0
                    ? $"This graph is a shared asset — {shared} other machine(s) use it."
                    : "This graph lives in a shared asset file.",
                shared > 0 ? MessageType.Warning : MessageType.None);

            candidate.resolution = (MacroResolution)EditorGUILayout.EnumPopup("Apply by", candidate.resolution);
            EditorGUILayout.LabelField(" ", ExplainResolution(candidate.resolution, shared), EditorStyles.miniLabel);
        }

        static string ExplainResolution(MacroResolution resolution, int shared) => resolution switch
        {
            MacroResolution.ConvertToEmbed =>
                "This machine gets its own copy of the graph; the shared asset is untouched.",
            MacroResolution.OverwriteAsset => shared > 0
                ? $"Rewrites the shared asset — all {shared + 1} machines using it change."
                : "Rewrites the shared asset.",
            _ => "Writes a new asset file and points only this machine at it.",
        };

        static string Describe(SyncState state) => state switch
        {
            SyncState.Applied => "already in this scene",
            SyncState.Pending => "ready to apply",
            SyncState.StaleBase => "based on an older graph",
            _ => "no matching machine",
        };

        void ApplySelected()
        {
            var scene = SceneManager.GetActiveScene();
            var link = WorldLinkStamp.FindActive();
            if (link == null) return;

            var applied = new List<SyncedGraphRecord>();
            foreach (var candidate in candidates.Where(c => c.selected && c.machine != null))
            {
                var record = ScriptGraphSync.Apply(candidate);
                if (record != null) applied.Add(record);
            }

            if (applied.Count == 0)
            {
                status = "Nothing was applied — see the console.";
                Repaint();
                return;
            }

            Undo.RecordObject(link, "Sync Runtime Script Graphs");
            link.syncedGraphs ??= new List<SyncedGraphRecord>();
            foreach (var record in applied)
            {
                // Re-applying an entry replaces its record rather than adding a second one; the
                // prune matches on entryId and would otherwise try to remove it twice.
                link.syncedGraphs.RemoveAll(r => r != null && r.entryId == record.entryId);
                link.syncedGraphs.Add(record);
            }
            link.lastSyncedAt = NowIso();
            EditorUtility.SetDirty(link);
            EditorSceneManager.MarkSceneDirty(scene);

            Debug.Log($"[Banter] Applied {applied.Count} runtime graph edit(s) to '{scene.name}'. "
                    + "They stay in the world's override file until the next successful world upload.");
            Refresh();
        }

        static string NowIso() => System.DateTime.UtcNow.ToString("o");
    }
}
#endif
