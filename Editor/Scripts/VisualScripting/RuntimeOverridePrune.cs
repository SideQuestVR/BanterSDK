#if GREENFIELD_PROJECT
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BS;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BS.SDKEditor
{
    /// <summary>
    /// Removes runtime script-graph overrides from a world once the scene that now contains them
    /// has been uploaded.
    /// </summary>
    /// <remarks>
    /// An override in <c>__persisted_script_graphs.json</c> is applied over the authored graph every
    /// time the space loads. Once the graph has been synced into the project and that project has
    /// been uploaded, the override is not merely redundant — it is actively harmful, because it
    /// pins the graph at the version it held when the runtime edit was made and every later Unity
    /// edit loses to it silently.
    ///
    /// This only ever runs after a successful world upload, and it is deliberately not
    /// transactional: if the rewrite fails, the records stay on the world link and the next upload
    /// tries again. The failure mode of retrying is a duplicate no-op; the failure mode of clearing
    /// the records early is an override that never gets removed.
    /// </remarks>
    public static class RuntimeOverridePrune
    {
        public static IEnumerator Run(SqEditorAppApi api, string worldId, string slug, Action<string> report)
        {
            report ??= _ => { };

            var scene = SceneManager.GetActiveScene();
            var link = WorldLinkStamp.Find(scene);
            var pending = WorldLinkStamp.PendingSyncedGraphs(scene);
            if (link == null || pending.Count == 0) yield break;

            if (string.IsNullOrEmpty(worldId) || string.IsNullOrEmpty(slug))
            {
                report("Runtime overrides could not be cleaned up: no world selected.");
                yield break;
            }

            // Records are matched by entry id, but only removed when the entry has not been
            // re-saved since: someone editing the same graph at runtime after this project synced
            // it has written a NEWER override, and dropping that would delete their work.
            var byId = pending.ToDictionary(r => r.entryId, r => r, StringComparer.Ordinal);

            var pruned = new List<string>();
            var kept = new List<SyncedGraphRecord>();

            // ---- graphs file -------------------------------------------------------------
            string graphsJson = null;
            var failed = false;
            yield return PersistedWorldFiles.Fetch(slug, PersistedWorldFiles.ScriptGraphsFile,
                json => graphsJson = json,
                error => { failed = true; report(error); });
            if (failed) yield break;

            if (graphsJson == null)
            {
                // Nothing to prune from — someone else already cleared it. The records have done
                // their job, so let them go rather than warning about this on every upload.
                ClearRecords(link, scene, pending.Select(r => r.entryId), report, 0);
                yield break;
            }

            var graphs = PersistedWorldFiles.Parse<PersistedScriptGraphs>(graphsJson);
            if (graphs?.entries == null)
            {
                report("Runtime overrides could not be cleaned up: " + PersistedWorldFiles.ScriptGraphsFile + " is unreadable.");
                yield break;
            }

            var remaining = new List<PersistedScriptGraphEntry>();
            foreach (var entry in graphs.entries)
            {
                if (entry == null) continue;
                if (entry.id != null && byId.TryGetValue(entry.id, out var record)
                    && SameVersion(record, entry))
                {
                    pruned.Add(entry.id);
                    continue;
                }
                remaining.Add(entry);
            }

            // A record whose entry was re-saved (or has already gone) stays pending: the next
            // upload re-evaluates it against whatever the file says then.
            foreach (var record in pending)
            {
                if (!pruned.Contains(record.entryId) && graphs.entries.Any(e => e?.id == record.entryId))
                    kept.Add(record);
            }

            if (pruned.Count == 0)
            {
                report("Runtime overrides were re-saved since they were synced; leaving them in place.");
                ClearRecords(link, scene, pending.Select(r => r.entryId).Except(kept.Select(r => r.entryId)), report, 0);
                yield break;
            }

            graphs.entries = remaining;
            graphs.savedAt = DateTime.UtcNow.ToString("o");

            yield return PersistedWorldFiles.Upload(api, worldId, slug, PersistedWorldFiles.ScriptGraphsFile,
                PersistedWorldFiles.Write(graphs),
                () => report($"Removed {pruned.Count} synced graph override(s) from the world."),
                error => { failed = true; report(error); });
            if (failed) yield break;

            // ---- shanes editor file ------------------------------------------------------
            // Only the graph references are touched. The scene overlay beside them has never been
            // imported into Unity, so clearing it would destroy work that exists nowhere else —
            // hence the extension-data round trip in PersistedShanesEditor.
            string editorJson = null;
            yield return PersistedWorldFiles.Fetch(slug, PersistedWorldFiles.ShanesEditorFile,
                json => editorJson = json,
                error => report(error));

            if (editorJson != null)
            {
                var editorFile = PersistedWorldFiles.Parse<PersistedShanesEditor>(editorJson);
                if (editorFile?.Graphs != null)
                {
                    var before = editorFile.Graphs.Count;
                    editorFile.Graphs = editorFile.Graphs.Where(g => g?.Id == null || !pruned.Contains(g.Id)).ToList();
                    if (editorFile.Graphs.Count != before)
                    {
                        yield return PersistedWorldFiles.Upload(api, worldId, slug, PersistedWorldFiles.ShanesEditorFile,
                            PersistedWorldFiles.Write(editorFile),
                            () => report($"Updated {PersistedWorldFiles.ShanesEditorFile}."),
                            error => report(error));
                    }
                }
            }

            ClearRecords(link, scene, pruned, report, pruned.Count);
        }

        /// <summary>
        /// Whether the override in the world is still the one this project synced. A blank
        /// savedAt on either side means we cannot tell, and not knowing is treated as "yes" —
        /// the alternative leaves an override in place forever.
        /// </summary>
        static bool SameVersion(SyncedGraphRecord record, PersistedScriptGraphEntry entry)
        {
            if (string.IsNullOrEmpty(record.sourceSavedAt) || string.IsNullOrEmpty(entry.savedAt)) return true;
            return string.Equals(record.sourceSavedAt, entry.savedAt, StringComparison.Ordinal);
        }

        static void ClearRecords(BSWorldLink link, Scene scene, IEnumerable<string> entryIds,
                                 Action<string> report, int prunedCount)
        {
            var ids = new HashSet<string>(entryIds ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            if (ids.Count == 0) return;

            Undo.RecordObject(link, "Clear Synced Graph Records");
            link.syncedGraphs?.RemoveAll(r => r != null && ids.Contains(r.entryId));
            EditorUtility.SetDirty(link);
            EditorSceneManager.MarkSceneDirty(scene);
            if (prunedCount == 0)
                report("Cleared " + ids.Count + " stale sync record(s).");
        }

        /// <summary>
        /// One line for the upload confirmation, or null when there is nothing to say.
        /// </summary>
        public static string ConfirmationLine()
        {
            var pending = WorldLinkStamp.PendingSyncedGraphs(SceneManager.GetActiveScene());
            if (pending.Count == 0) return null;

            var names = pending.Select(r => string.IsNullOrEmpty(r.title) ? r.bid : r.title)
                               .Where(n => !string.IsNullOrEmpty(n))
                               .Distinct()
                               .ToList();
            var listed = names.Count <= 3
                ? string.Join(", ", names)
                : string.Join(", ", names.Take(3)) + " and " + (names.Count - 3) + " more";

            return pending.Count == 1
                ? $"1 runtime graph edit ({listed}) is now in this scene and will be removed from the world after upload."
                : $"{pending.Count} runtime graph edits ({listed}) are now in this scene and will be removed from the world after upload.";
        }
    }
}
#endif
