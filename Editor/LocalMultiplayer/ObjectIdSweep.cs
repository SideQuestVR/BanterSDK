using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BS.LocalMultiplayer.Editor
{
    /// <summary>Why a networked object's <see cref="BSObjectId.Id"/> may not be the same in every player.</summary>
    internal enum IdProblem
    {
        None,

        /// <summary>No Id: Awake fills in the instance id, which each editor process picks for itself.</summary>
        Empty,

        /// <summary>
        /// Another object in the loaded scenes has the same Id. Each player renames the copies to its own instance
        /// ids, and a lookup by Id finds whichever copy comes first (BSScene.GetObjectByBid).
        /// </summary>
        Duplicate,

        /// <summary>
        /// An instance id (<c>^-?\d+$</c>, ASCII digits). OnValidate and Awake write these to de-duplicate without
        /// marking the scene dirty, so the saved scene may hold another value, or the same value twice.
        /// </summary>
        InstanceIdStyle,
    }

    /// <summary>
    /// Finds the networked objects (<see cref="BSSyncedObject"/>, <see cref="BSAttachedObject"/>,
    /// <see cref="BSSeat"/>) whose <see cref="BSObjectId"/> other players would not share, and gives them stable
    /// Ids. Players match objects by that Id (object records, attachment and seat keys), and a Multiplayer Play
    /// Mode clone loads the saved scene, so an Id only the main editor's memory holds splits the object in two.
    /// </summary>
    /// <remarks>
    /// Where unstable Ids come from: BSObjectId.Awake and OnValidate call <c>GenerateId(IsDuplicateId(Id))</c>
    /// (BSObjectId.cs:38-50, 63-70), which writes <c>gameObject.GetInstanceID().ToString()</c> (:80-86) whenever the
    /// Id is empty or a duplicate. That value differs per process and never dirties the scene. A duplicate is
    /// typically a copy of an object that already had an Id, such as a second instance of the shipped Seat
    /// prefab. The fix is <see cref="BSObjectId.ForceGenerateId"/> (:87-90), a random URL-safe Id, recorded for
    /// Undo and marked dirty so it gets saved.
    /// </remarks>
    internal static class ObjectIdSweep
    {
        internal struct Finding
        {
            public BSObjectId Component;
            public IdProblem Problem;
        }

        /// <summary>
        /// The problem with an Id that <paramref name="occurrences"/> BSObjectIds in the loaded scenes carry.
        /// Empty first, then Duplicate, then InstanceIdStyle.
        /// </summary>
        internal static IdProblem Classify(string id, int occurrences)
        {
            if (string.IsNullOrEmpty(id))
            {
                return IdProblem.Empty;
            }
            if (occurrences > 1)
            {
                return IdProblem.Duplicate;
            }
            return IsInstanceIdStyle(id) ? IdProblem.InstanceIdStyle : IdProblem.None;
        }

        /// <summary><c>^-?\d+$</c> over ASCII digits: what <c>GetInstanceID().ToString()</c> produces.</summary>
        internal static bool IsInstanceIdStyle(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return false;
            }
            var start = id[0] == '-' ? 1 : 0;
            if (start == id.Length)
            {
                return false;
            }
            for (var i = start; i < id.Length; i++)
            {
                if (id[i] < '0' || id[i] > '9')
                {
                    return false;
                }
            }
            return true;
        }

        internal static string Describe(IdProblem problem)
        {
            switch (problem)
            {
                case IdProblem.Empty: return "no Id";
                case IdProblem.Duplicate: return "duplicate Id";
                case IdProblem.InstanceIdStyle: return "instance-id style Id";
                default: return "stable";
            }
        }

        /// <summary>The components that make an object networked; each of them requires a BSObjectId.</summary>
        internal static bool IsNetworked(GameObject go)
        {
            return go != null
                   && (go.TryGetComponent<BSSyncedObject>(out _)
                       || go.TryGetComponent<BSAttachedObject>(out _)
                       || go.TryGetComponent<BSSeat>(out _));
        }

        /// <summary>
        /// Every networked object in the loaded scenes whose Id is unstable, inactive ones included. Duplicates are
        /// counted over every BSObjectId, networked or not, because a lookup by Id sees them all.
        /// </summary>
        internal static List<Finding> Find(out int networkedCount)
        {
            var all = new List<BSObjectId>();
            var buffer = new List<BSObjectId>();
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                {
                    continue;
                }
                foreach (var root in scene.GetRootGameObjects())
                {
                    root.GetComponentsInChildren(true, buffer);
                    foreach (var id in buffer)
                    {
                        // Editor-only helpers are never saved, so no other player will look for them.
                        if (id != null && (id.gameObject.hideFlags & HideFlags.DontSaveInEditor) == 0)
                        {
                            all.Add(id);
                        }
                    }
                }
            }

            var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var id in all)
            {
                if (!string.IsNullOrEmpty(id.Id))
                {
                    occurrences.TryGetValue(id.Id, out var count);
                    occurrences[id.Id] = count + 1;
                }
            }

            networkedCount = 0;
            var findings = new List<Finding>();
            foreach (var id in all)
            {
                if (!IsNetworked(id.gameObject))
                {
                    continue;
                }
                networkedCount++;
                var count = 0;
                if (!string.IsNullOrEmpty(id.Id))
                {
                    occurrences.TryGetValue(id.Id, out count);
                }
                var problem = Classify(id.Id, count);
                if (problem != IdProblem.None)
                {
                    findings.Add(new Finding { Component = id, Problem = problem });
                }
            }
            return findings;
        }

        /// <summary>Asks first, then gives every finding a new Id and offers to save the scenes.</summary>
        internal static bool AssignWithConfirmation(IReadOnlyList<Finding> findings)
        {
            if (findings == null || findings.Count == 0)
            {
                return false;
            }
            var message =
                $"Give {findings.Count} networked object{(findings.Count == 1 ? "" : "s")} a new, stable BSObjectId?\n\n"
                + "Every player then finds the object under the same Id once the scene is saved. Scripts that refer "
                + "to these objects by their current Id need updating. You can undo this.";
            if (!EditorUtility.DisplayDialog("Assign stable Ids", message, "Assign", "Cancel"))
            {
                return false;
            }
            var touched = new List<Scene>();
            var changed = Assign(findings, touched);
            if (changed > 0
                && EditorUtility.DisplayDialog("Assign stable Ids",
                    $"{changed} Id{(changed == 1 ? "" : "s")} changed. Multiplayer Play Mode players only load saved "
                    + "scenes. Save the changed scenes now?", "Save", "Later"))
            {
                LocalMpScenes.SaveDirty(ScenesToSave(touched));
            }
            return changed > 0;
        }

        /// <summary>
        /// The open scenes with unsaved changes (<see cref="LocalMpScenes.Dirty"/>) that are among
        /// <paramref name="touched"/>, and no others: the dialog offers to save the changed scenes, so unrelated
        /// edits in another open scene stay unsaved. An untitled scene has no file to save to; the window's Save
        /// All asks where to save it.
        /// </summary>
        internal static List<Scene> ScenesToSave(List<Scene> touched)
        {
            var scenes = LocalMpScenes.Dirty();
            scenes.RemoveAll(scene => touched == null || !touched.Contains(scene));
            return scenes;
        }

        /// <summary>
        /// Gives each finding a new Id as one Undo step: the change is recorded, kept as a prefab-instance
        /// override where it applies, and the scene is marked dirty so it gets saved. Each scene that changed is
        /// added once to <paramref name="touchedScenes"/> when one is given.
        /// </summary>
        internal static int Assign(IReadOnlyList<Finding> findings, List<Scene> touchedScenes = null)
        {
            if (findings == null || findings.Count == 0)
            {
                return 0;
            }
            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Assign stable Ids");
            var changed = 0;
            foreach (var finding in findings)
            {
                var id = finding.Component;
                if (id == null)
                {
                    continue;
                }
                Undo.RecordObject(id, "Assign stable Ids");
                id.ForceGenerateId();
                if (PrefabUtility.IsPartOfPrefabInstance(id))
                {
                    PrefabUtility.RecordPrefabInstancePropertyModifications(id);
                }
                var scene = id.gameObject.scene;
                EditorSceneManager.MarkSceneDirty(scene);
                if (touchedScenes != null && !touchedScenes.Contains(scene))
                {
                    touchedScenes.Add(scene);
                }
                changed++;
            }
            Undo.CollapseUndoOperations(group);
            if (changed > 0)
            {
                Debug.Log($"[LocalMP] Gave {changed} networked object{(changed == 1 ? "" : "s")} a stable BSObjectId.");
            }
            return changed;
        }
    }
}
