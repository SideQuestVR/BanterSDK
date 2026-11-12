using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BS.LocalMultiplayer.Editor
{
    /// <summary>
    /// Before Play starts in the main editor, makes sure every player will load what this editor shows. A
    /// Multiplayer Play Mode clone loads its scenes from disk, so with local multiplayer on, scenes with unsaved
    /// changes are saved first (autoSaveScenesOnPlay) or the creator is asked. An untitled scene only gets a
    /// Console warning: no clone can open it, but Play still starts, because playing alone needs no saved scene
    /// and the Test Runner's edit-mode runs enter Play from an untitled scene of their own. Clones follow the main
    /// editor's Play and never save, so they skip all of it.
    /// </summary>
    [InitializeOnLoad]
    internal static class LocalMpPlayModeGuard
    {
        const string Title = "Local multiplayer";

        static LocalMpPlayModeGuard()
        {
            if (MppmEnvironment.Current.IsClone)
            {
                return;
            }
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.ExitingEditMode)
            {
                return;
            }
            // Batch mode has nobody to answer a dialog and no other players; the Test Runner plays its own scene.
            if (Application.isBatchMode || LocalMpScenes.IsTestRunScene())
            {
                return;
            }

            LocalMultiplayerSettings settings;
            try
            {
                settings = LocalMultiplayerSettings.Load(MppmEnvironment.Current.MainProjectRoot);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return;
            }
            if (!settings.IsEnabled)
            {
                return;
            }

            if (!LetPlayStart(settings, LocalMpScenes.Untitled(), LocalMpScenes.Dirty()))
            {
                // Turning Play back off while leaving Edit Mode cancels it.
                EditorApplication.isPlaying = false;
            }
        }

        /// <summary>
        /// Whether Play may start with these <paramref name="untitled"/> and <paramref name="dirty"/> scenes. Only
        /// the creator's Cancel in the unsaved-changes dialog says no; an untitled scene is a warning in the Console,
        /// never a dialog (an interactive test run would hang on it) or a cancelled Play.
        /// </summary>
        internal static bool LetPlayStart(LocalMultiplayerSettings settings, List<Scene> untitled, List<Scene> dirty)
        {
            if (untitled != null && untitled.Count > 0)
            {
                Debug.LogWarning(UntitledWarning(untitled));
            }

            if (dirty == null || dirty.Count == 0)
            {
                return true;
            }
            var names = LocalMpScenes.Names(dirty);
            if (settings == null || settings.autoSaveScenesOnPlay)
            {
                if (LocalMpScenes.SaveDirty(dirty))
                {
                    Debug.Log($"[LocalMP] Saved {names} before Play: Multiplayer Play Mode players only load saved scenes.");
                }
                return true;
            }

            var choice = EditorUtility.DisplayDialogComplex(Title,
                $"{names} {(dirty.Count == 1 ? "has" : "have")} unsaved changes. Multiplayer Play Mode players only "
                + "load saved scenes, so the other players won't see them.",
                "Save and Play", "Cancel", "Play Without Saving");
            switch (choice)
            {
                case 0:
                    LocalMpScenes.SaveDirty(dirty);
                    return true;
                case 2:
                    return true;
                default:
                    // 1: Cancel, the window's close button or Escape.
                    return false;
            }
        }

        /// <summary>The Console warning for scenes that were never saved.</summary>
        internal static string UntitledWarning(List<Scene> untitled)
        {
            return $"[LocalMP] Never saved: {LocalMpScenes.Names(untitled)}. Multiplayer Play Mode players load scenes "
                   + "from disk, so they can't open a scene that was never saved. Save it and press Play again so they "
                   + "see it too.";
        }
    }
}
