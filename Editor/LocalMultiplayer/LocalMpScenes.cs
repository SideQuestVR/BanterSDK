using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BS.LocalMultiplayer.Editor
{
    /// <summary>
    /// The open scenes as Multiplayer Play Mode sees them. A clone follows the main editor's open scenes but
    /// loads them from disk ("Unsaved scene changes in the main editor will not be loaded on other players"), so
    /// an unsaved change exists in the main editor alone, and an untitled scene in no other player at all.
    /// </summary>
    internal static class LocalMpScenes
    {
        // The Test Runner's own scene while it enters Play Mode for play mode tests.
        const string TestRunScenePrefix = "InitTestScene";

        /// <summary>Loaded scenes that were never saved.</summary>
        internal static List<Scene> Untitled()
        {
            var result = new List<Scene>();
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded && string.IsNullOrEmpty(scene.path))
                {
                    result.Add(scene);
                }
            }
            return result;
        }

        /// <summary>Loaded scenes that have a file and unsaved changes.</summary>
        internal static List<Scene> Dirty()
        {
            var result = new List<Scene>();
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded && scene.isDirty && !string.IsNullOrEmpty(scene.path))
                {
                    result.Add(scene);
                }
            }
            return result;
        }

        /// <summary>Saves scenes that have a file. False when Unity could not save one of them.</summary>
        internal static bool SaveDirty(List<Scene> scenes)
        {
            if (scenes == null || scenes.Count == 0)
            {
                return true;
            }
            if (EditorSceneManager.SaveScenes(scenes.ToArray()))
            {
                return true;
            }
            Debug.LogWarning($"[LocalMP] Couldn't save {Names(scenes)}; the other players load the last saved version.");
            return false;
        }

        /// <summary>
        /// Saves untitled scenes through Unity's own save dialog (SaveScene shows it for a scene without a path).
        /// False when one was cancelled or failed.
        /// </summary>
        internal static bool SaveUntitled(List<Scene> scenes)
        {
            if (scenes == null)
            {
                return true;
            }
            foreach (var scene in scenes)
            {
                if (!scene.IsValid() || !string.IsNullOrEmpty(scene.path))
                {
                    continue;
                }
                if (!EditorSceneManager.SaveScene(scene))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>The Test Runner is entering Play Mode with its own scene: not a creator's Play.</summary>
        internal static bool IsTestRunScene()
        {
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var name = SceneManager.GetSceneAt(i).name;
                if (!string.IsNullOrEmpty(name) && name.StartsWith(TestRunScenePrefix, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>"Main, Lobby", with "Untitled" for a scene that has no name yet.</summary>
        internal static string Names(List<Scene> scenes)
        {
            var text = new StringBuilder();
            foreach (var scene in scenes)
            {
                if (text.Length > 0)
                {
                    text.Append(", ");
                }
                text.Append(string.IsNullOrEmpty(scene.name) ? "Untitled" : scene.name);
            }
            return text.ToString();
        }
    }
}
