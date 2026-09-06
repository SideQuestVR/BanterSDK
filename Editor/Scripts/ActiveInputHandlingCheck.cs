// Active Input Handling check.
//
// The SDK runtime uses the legacy Input Manager (PlayerEmulator) and the Input System
// package (HandGrabber, BSScene, WorldSpaceUIDocument, ...) at the same time, and there
// isn't an ENABLE_INPUT_SYSTEM / ENABLE_LEGACY_INPUT_MANAGER guard anywhere in the
// package, so it only works with Player Settings > Active Input Handling set to "Both".
//
// Neither wrong value is a compile error: under New-only the legacy Input.* calls throw
// InvalidOperationException at runtime, and under Old-only the Input System has no native
// device backend and just sees no devices. The project builds and then fails silently,
// which is why this checks explicitly and warns before changing anything.
//
// Mechanics follow Unity's own Input System package, which does the same thing:
//   InputSystem/Editor/Settings/EditorPlayerSettingHelpers.cs - the read/write path
//   InputSystem/Editor/Internal/EditorHelpers.cs:54           - the editor restart
// There is no public PlayerSettings API for this setting in 6000.3.

using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
#if UNITY_6000_0_OR_NEWER
using UnityEditor.Build.Profile;
#endif

namespace BS.SDKEditor
{
    public static class ActiveInputHandlingCheck
    {
        public enum InputHandling
        {
            Unknown = -1,
            OldInputManager = 0,
            NewInputSystem = 1,
            Both = 2,
        }

        const string PROPERTY_NAME = "activeInputHandler";
        const string PROJECT_SETTINGS_PATH = "ProjectSettings/ProjectSettings.asset";
        const string MENU_PATH = "Altspace/Tools/Fix Active Input Handling";
        const string MENU_DISPLAY = "Altspace > Tools > Fix Active Input Handling";
        const string PROMPTED_KEY = "BS.SDK.ActiveInputHandling.Prompted";
        const string RESTART_LATER = "[Banter] Active Input Handling is now \"Both\". Restart Unity when you're ready for it to take effect.";

        // -- Detection ----------------------------------------------------------

        /// <summary>What Player Settings currently says. A pure read, no side effects.</summary>
        public static InputHandling Current
        {
            get
            {
                SerializedObject so;
                SerializedProperty prop;
                if (!TryGetProperty(out so, out prop))
                    return InputHandling.Unknown;

                switch (prop.intValue)
                {
                    case 0: return InputHandling.OldInputManager;
                    case 1: return InputHandling.NewInputSystem;
                    case 2: return InputHandling.Both;
                    default: return InputHandling.Unknown;
                }
            }
        }

        public static bool IsConfigured
        {
            get { return Current == InputHandling.Both; }
        }

        static bool TryGetProperty(out SerializedObject so, out SerializedProperty prop)
        {
            so = null;
            prop = null;

            string assetPath;
            var target = FindPlayerSettings(out assetPath);
            if (target == null)
                return false;

            try
            {
                so = new SerializedObject(target);
                prop = so.FindProperty(PROPERTY_NAME);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Banter] Could not read '" + PROPERTY_NAME + "' from Player Settings: " + e.Message);
                return false;
            }

            return prop != null;
        }

        /// <summary>
        /// The PlayerSettings object that actually governs the build, plus the asset backing
        /// it. Never uses Resources.FindObjectsOfTypeAll&lt;PlayerSettings&gt;() - that is the
        /// path Unity's own HOTFIX comment blames for an infinite reimport loop on Unity 6.
        /// </summary>
        static UnityEngine.Object FindPlayerSettings(out string assetPath)
        {
            assetPath = PROJECT_SETTINGS_PATH;

#if UNITY_6000_0_OR_NEWER
            try
            {
                var profileType = typeof(BuildProfile);

                // A build profile may carry a Player Settings override, and that override is
                // what the player actually gets. Prefer it over the global settings.
                var active = BuildProfile.GetActiveBuildProfile();
                if (active != null)
                {
                    var overrideField = profileType.GetField("m_PlayerSettings", BindingFlags.Instance | BindingFlags.NonPublic);
                    var profileSettings = overrideField == null ? null : overrideField.GetValue(active) as PlayerSettings;
                    if (profileSettings != null)
                    {
                        var profilePath = AssetDatabase.GetAssetPath(active);
                        if (!string.IsNullOrEmpty(profilePath))
                            assetPath = profilePath;
                        return profileSettings;
                    }
                }

                var globalField = profileType.GetField("s_GlobalPlayerSettings", BindingFlags.Static | BindingFlags.NonPublic);
                var globalSettings = globalField == null ? null : globalField.GetValue(null) as PlayerSettings;
                if (globalSettings != null)
                    return globalSettings;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Banter] Could not reach Player Settings through the build profile API (" + e.Message +
                                 "); falling back to " + PROJECT_SETTINGS_PATH + ".");
            }
#endif

            // Fallback - the ProjectSettings asset itself, the same approach InitialiseOnLoad
            // uses for TagManager.asset. Can't see a build profile's Player Settings override,
            // but survives a rename of Unity's private fields.
            try
            {
                var assets = AssetDatabase.LoadAllAssetsAtPath(PROJECT_SETTINGS_PATH);
                if (assets != null)
                {
                    foreach (var asset in assets)
                    {
                        if (asset != null)
                            return asset;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Banter] Could not load " + PROJECT_SETTINGS_PATH + ": " + e.Message);
            }

            return null;
        }

        // -- Prompt -------------------------------------------------------------

        [MenuItem(MENU_PATH)]
        public static void PromptFromMenu()
        {
            var current = Current;

            if (current == InputHandling.Both)
            {
                EditorUtility.DisplayDialog("Active Input Handling",
                    "Active Input Handling is already set to \"Both\". Nothing to change.", "OK");
                return;
            }

            if (current == InputHandling.Unknown)
            {
                EditorUtility.DisplayDialog("Active Input Handling",
                    "Could not read Active Input Handling from Player Settings. Set it to \"Both\" by hand under Edit > Project Settings > Player.",
                    "OK");
                return;
            }

            SessionState.SetBool(PROMPTED_KEY, true);
            Prompt(current);
        }

        static void Prompt(InputHandling current)
        {
            var message =
                "This project's Active Input Handling is \"" + Label(current) + "\". The SideQuest Creator SDK requires \"Both\".\n\n" +
                "The SDK uses the legacy Input Manager and the Input System package at the same time, and neither is optional:\n" +
                "• Desktop keyboard and mouse control (PlayerEmulator) uses the legacy Input Manager.\n" +
                "• Hand grabbing, world-space UI and asset loading use the Input System package.\n\n" +
                "Neither wrong setting is a compile error, so the project will build and then fail silently at runtime.\n\n" +
                "\"Set to Both and restart\" rewrites Player Settings and restarts Unity to recompile. Unsaved scenes are saved first.";

            var accepted = EditorUtility.DisplayDialog(
                "Set Active Input Handling to Both?",
                message,
                "Set to Both and restart",
                "Leave it unchanged");

            if (!accepted)
            {
                Debug.LogWarning(DeclineMessage(current));
                return;
            }

            if (ApplyBoth())
                RestartEditor();
        }

        static string DeclineMessage(InputHandling current)
        {
            return "[Banter] Active Input Handling is \"" + Label(current) + "\", not \"Both\". SDK input will fail silently at runtime. Fix it with " + MENU_DISPLAY + ".";
        }

        static string Label(InputHandling handling)
        {
            switch (handling)
            {
                case InputHandling.OldInputManager: return "Input Manager (Old)";
                case InputHandling.NewInputSystem: return "Input System Package (New)";
                case InputHandling.Both: return "Both";
                default: return "unknown";
            }
        }

        // -- Write --------------------------------------------------------------

        /// <summary>
        /// Writes Active Input Handling = Both. No dialog, no restart. Returns false and
        /// leaves the project untouched if the write could not be made to stick.
        /// </summary>
        public static bool ApplyBoth()
        {
            string assetPath;
            var target = FindPlayerSettings(out assetPath);
            if (target == null)
            {
                Debug.LogError("[Banter] Could not find Player Settings - Active Input Handling was not changed.");
                return false;
            }

            if (!AssetDatabase.MakeEditable(assetPath))
            {
                Debug.LogError("[Banter] Could not check out " + assetPath + " - Active Input Handling was not changed.");
                if (!Application.isBatchMode)
                {
                    EditorUtility.DisplayDialog("Could not check out Player Settings",
                        assetPath + " is not editable. Check it out in your version control system, then run " + MENU_DISPLAY + " again.",
                        "OK");
                }
                return false;
            }

            SerializedObject so;
            SerializedProperty prop;
            if (!TryGetProperty(out so, out prop))
            {
                Debug.LogError("[Banter] Could not read '" + PROPERTY_NAME + "' - Active Input Handling was not changed.");
                return false;
            }

            // Set 2 outright rather than going through the two-bool backend API, which has a
            // documented (false, false) trap - see EditorPlayerSettingHelpers.cs:105-114.
            prop.intValue = (int)InputHandling.Both;
            so.ApplyModifiedProperties();

            // Force this object out to disk before the on-disk check below - SaveAssets()
            // alone doesn't guarantee Player Settings has been flushed yet, and a stale read
            // would look like a failed write.
            AssetDatabase.SaveAssetIfDirty(target);
            AssetDatabase.SaveAssets();

            if (Current != InputHandling.Both || !VerifyOnDisk(assetPath))
            {
                Debug.LogError("[Banter] Writing Active Input Handling to \"Both\" did not stick - is " + assetPath +
                               " read-only? Set it by hand under Edit > Project Settings > Player.");
                return false;
            }

            Debug.Log("[Banter] Active Input Handling set to \"Both\".");
            return true;
        }

        /// <summary>
        /// Catches a read-only file that MakeEditable optimistically approved. Returns true
        /// when the file cannot be checked (binary serialization, missing key), leaving the
        /// in-memory read as the verdict.
        /// </summary>
        static bool VerifyOnDisk(string assetPath)
        {
            try
            {
                if (!File.Exists(assetPath))
                    return true;

                var text = File.ReadAllText(assetPath);
                var index = text.IndexOf(PROPERTY_NAME + ":", StringComparison.Ordinal);
                if (index < 0)
                    return true;

                return text.Substring(index).StartsWith(PROPERTY_NAME + ": 2", StringComparison.Ordinal);
            }
            catch
            {
                return true;
            }
        }

        // -- Restart ------------------------------------------------------------

        /// <summary>
        /// Restarts the editor so the new value takes effect. Nothing in this repo has
        /// restarted the editor before, so every step degrades to the next.
        /// </summary>
        public static void RestartEditor()
        {
            if (Application.isBatchMode)
                return;

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                // The setting is already applied, so the next launch passes the check.
                Debug.LogWarning(RESTART_LATER);
                return;
            }

            try
            {
                var restart = typeof(EditorApplication).GetMethod(
                    "RestartEditorAndRecompileScripts", BindingFlags.NonPublic | BindingFlags.Static);
                if (restart != null)
                {
                    restart.Invoke(null, null);
                    return;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Banter] Editor restart failed (" + e.Message + "); trying to reopen the project instead.");
            }

            try
            {
                var projectRoot = Directory.GetParent(Application.dataPath);
                if (projectRoot != null)
                {
                    EditorApplication.OpenProject(projectRoot.FullName);
                    return;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Banter] Could not reopen the project (" + e.Message + ").");
            }

            Debug.LogWarning(RESTART_LATER);
            EditorUtility.DisplayDialog("Restart Unity to finish",
                "Active Input Handling is now \"Both\". Restart Unity for it to take effect.", "OK");
        }

        // -- Check, run from the bootstrap below --------------------------------

        internal static void Check()
        {
            if (SessionState.GetBool(PROMPTED_KEY, false))
                return;

            // A dialog raised mid-import cancels the import, so wait it out. This is what
            // covers the very first import of the SDK.
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += Check;
                return;
            }

            // Don't interrupt a build. Re-checked on the next domain reload.
            if (BuildPipeline.isBuildingPlayer)
                return;

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.playModeStateChanged -= ResumeAfterPlayMode;
                EditorApplication.playModeStateChanged += ResumeAfterPlayMode;
                return;
            }

            SuppressUnityInputSystemPrompt();

            var current = Current;
            if (current == InputHandling.Both)
                return;

            if (current == InputHandling.Unknown)
            {
                SessionState.SetBool(PROMPTED_KEY, true);
                Debug.LogWarning("[Banter] Could not read Active Input Handling from Player Settings. The SDK needs it set to \"Both\" - check it under Edit > Project Settings > Player.");
                return;
            }

            SessionState.SetBool(PROMPTED_KEY, true);

            // DisplayDialog auto-answers OK in batch mode, which would rewrite Player
            // Settings and kill a CI editor mid-run. Log and leave the project alone.
            if (Application.isBatchMode)
            {
                Debug.LogWarning(DeclineMessage(current));
                return;
            }

            Prompt(current);
        }

        static void ResumeAfterPlayMode(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredEditMode)
                return;

            EditorApplication.playModeStateChanged -= ResumeAfterPlayMode;
            EditorApplication.delayCall += Check;
        }

        /// <summary>
        /// The Input System package puts up its own "Enable and Restart" dialog whenever the
        /// new backends are off - i.e. only when this project is Old-only - so a creator who
        /// declines ours would then field Unity's. Ours is a strict superset (we require
        /// Both, they require New), so mark theirs as already asked. This flips no project
        /// setting; it only records that the question was put to the user this session.
        ///
        /// Deliberately NOT done by pre-setting their "RestartInstalledInputHandlingWarning"
        /// opt-out key: DisplayDialog returns true when an opt-out is set, which would drive
        /// their handler into enabling the backends and restarting with no consent at all.
        /// </summary>
        internal static void SuppressUnityInputSystemPrompt()
        {
            try
            {
                if (Current == InputHandling.Both)
                    return;

                var type = Type.GetType("UnityEngine.InputSystem.InputSystemStateManager, Unity.InputSystem");
                if (type == null)
                    return;

                var field = type.GetField("newInputBackendsCheckedAsEnabled", BindingFlags.Instance | BindingFlags.Public);
                if (field == null)
                    return;

                foreach (var stateManager in Resources.FindObjectsOfTypeAll(type))
                    field.SetValue(stateManager, true);
            }
            catch
            {
                // Best effort. Worst case the creator sees Unity's dialog as well, which is
                // harmless - accepting it from Old-only also lands on Both.
            }
        }
    }

#if !GREENFIELD_PROJECT
    /// <summary>
    /// Gated off in the Greenfield host project, which owns its own project settings and is
    /// deliberately already set to "Both". The menu item stays available everywhere.
    /// </summary>
    [InitializeOnLoad]
    internal static class ActiveInputHandlingBootstrap
    {
        static ActiveInputHandlingBootstrap()
        {
            // Every [InitializeOnLoad] static constructor runs before any delayCall is
            // dispatched, so two attempts cover both load orderings: if the Input System's
            // constructor ran first its state manager already exists and this one takes; if
            // ours ran first, theirs registers its delayCall after ours, so our handler runs
            // first and the attempt inside Check() takes.
            ActiveInputHandlingCheck.SuppressUnityInputSystemPrompt();
            EditorApplication.delayCall += ActiveInputHandlingCheck.Check;
        }
    }
#endif
}
