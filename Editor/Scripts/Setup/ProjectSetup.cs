using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BS.SDKEditor.Setup
{
    /// <summary>
    /// The project setup checklist behind the Setup panel: every <see cref="SetupCheck"/>, fixing them one
    /// at a time or all at once, and opening the window the first time the SDK loads in a project (and after
    /// an SDK update that leaves something required unfinished). Nothing here changes the project unless the
    /// creator presses a fix button.
    /// </summary>
    public static class ProjectSetup
    {
        const string LogTag = "[Creator SDK]";

        // Per user and project (UserSettings/EditorUserSettings.asset, which isn't shared through version control).
        const string SeenVersionKey = "BS.SDK.Setup.SeenVersion";
        const string ShowAtStartupKey = "BS.SDK.Setup.ShowAtStartup";
        // Per editor session: cleared when Unity restarts, which is exactly when a restart-pending fix lands.
        // The SDK version the startup check ran for, so an update made with Unity open runs it again.
        const string StartupHandledKey = "BS.SDK.Setup.StartupHandledVersion";
        const string StartupRanKey = "BS.SDK.Setup.StartupRan";
        const string RestartPendingKey = "BS.SDK.Setup.RestartPending";

        /// <summary>Raised after any fix runs or a background one finishes, so open windows can refresh.</summary>
        public static event Action Changed;

        /// <summary>For checks whose fix finishes in the background (an import, a package install).</summary>
        public static void NotifyChanged() => Changed?.Invoke();

        static List<SetupCheck> s_Checks;

        public static IReadOnlyList<SetupCheck> Checks => s_Checks ?? (s_Checks = CreateChecks());

        public static List<SetupCheck> CreateChecks() =>
            TypeCache.GetTypesDerivedFrom<SetupCheck>()
                .Where(type => !type.IsAbstract && type.GetConstructor(Type.EmptyTypes) != null)
                .Select(type => (SetupCheck)Activator.CreateInstance(type))
                .OrderBy(check => check.Order)
                .ThenBy(check => check.Id, StringComparer.Ordinal)
                .ToList();

        /// <summary>The check's status, or a manual-fix status saying why it couldn't be checked.</summary>
        public static SetupStatus Evaluate(SetupCheck check)
        {
            if (IsRestartPending(check.Id))
                return SetupStatus.RestartPending("Changed. Restart Unity for it to take effect.");
            try
            {
                return check.Evaluate() ?? SetupStatus.Done("");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return SetupStatus.NeedsManualFix("Couldn't check this: " + e.Message);
            }
        }

        /// <summary>Runs one item's fix. Returns whether anything changed.</summary>
        public static bool Fix(SetupCheck check)
        {
            bool changed;
            try
            {
                changed = check.Fix();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Debug.LogError($"{LogTag} {check.Title}: the fix failed ({e.Message}).");
                Changed?.Invoke();
                return false;
            }
            if (changed)
            {
                SaveProjectSettings();
                if (check.FixNeedsRestart)
                    MarkRestartPending(check.Id);
            }
            var (outcome, failed) = Outcome(check, changed);
            if (failed)
                Debug.LogWarning($"{LogTag} {check.Title}: {outcome}.");
            else
                Debug.Log($"{LogTag} {check.Title}: {outcome}.");
            Changed?.Invoke();
            return changed;
        }

        // From the item's state afterwards, not just what Fix returned: a fix returns false both when there was nothing
        // to do and when it couldn't do it.
        static (string text, bool failed) Outcome(SetupCheck check, bool changed)
        {
            var state = Evaluate(check).State;
            if (!changed)
                return state == SetupState.NeedsFix ? ("couldn't fix it; see the messages above", true) : ("nothing to change", false);
            if (check.FixNeedsRestart)
                return ("changed; restart Unity for it to take effect", false);
            switch (state)
            {
                // An import or a package install that finishes, or fails, after the fix returns.
                case SetupState.Working: return ("started; it finishes in the background", false);
                case SetupState.NeedsFix: return ("changed, but it still needs fixing; see the messages above", true);
                default: return ("fixed", false);
            }
        }

        // Player Settings changed from code are otherwise written only when the project is saved or Unity quits,
        // so a crash in between would undo the fix. (TagManager and Visual Scripting's settings save themselves.)
        static void SaveProjectSettings()
        {
            foreach (var settings in AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset"))
            {
                if (settings != null)
                    AssetDatabase.SaveAssetIfDirty(settings);
            }
        }

        public sealed class FixAllResult
        {
            public readonly List<SetupCheck> Fixed = new List<SetupCheck>();
            public readonly List<SetupCheck> Failed = new List<SetupCheck>();
            public bool RestartNeeded => Fixed.Any(check => check.FixNeedsRestart);
        }

        /// <summary>The items Fix All would run now: Required and Recommended ones that need a fix, in order.</summary>
        public static List<SetupCheck> FixAllCandidates(IReadOnlyList<SetupCheck> checks = null) =>
            (checks ?? Checks).Where(check => check.InFixAll && Evaluate(check).State == SetupState.NeedsFix).ToList();

        /// <summary>
        /// Fixes every Required and Recommended item that needs it, in list order (the slow node generation
        /// and anything that needs a restart come last). Manual and Optional items are left alone.
        /// </summary>
        public static FixAllResult FixAll(IReadOnlyList<SetupCheck> checks = null)
        {
            var result = new FixAllResult();
            var todo = FixAllCandidates(checks);
            try
            {
                for (var i = 0; i < todo.Count; i++)
                {
                    EditorUtility.DisplayProgressBar("Setting up the project", todo[i].Title, i / (float)todo.Count);
                    // A fix can also return false because it had nothing left to change; only a status that
                    // still needs the same fix afterwards counts as a failure.
                    Fix(todo[i]);
                    if (Evaluate(todo[i]).State == SetupState.NeedsFix)
                        result.Failed.Add(todo[i]);
                    else
                        result.Fixed.Add(todo[i]);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            return result;
        }

        // -- Restart --------------------------------------------------------------

        public static bool IsRestartPending(string id) => PendingRestartIds().Contains(id);

        public static bool AnyRestartPending => PendingRestartIds().Count > 0;

        static HashSet<string> PendingRestartIds() =>
            new HashSet<string>(SessionState.GetString(RestartPendingKey, "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));

        static void MarkRestartPending(string id)
        {
            var ids = PendingRestartIds();
            ids.Add(id);
            SessionState.SetString(RestartPendingKey, string.Join(",", ids));
        }

        /// <summary>Saves scenes if the creator wants, then restarts Unity so pending changes take effect.</summary>
        public static void RestartEditor() => ActiveInputHandlingCheck.RestartEditor();

        // -- Setup panel at startup -------------------------------------------------

        /// <summary>Whether the Setup panel opens every time Unity starts, not just the first time.</summary>
        public static bool ShowAtStartup
        {
            get => EditorUserSettings.GetConfigValue(ShowAtStartupKey) == "1";
            set => EditorUserSettings.SetConfigValue(ShowAtStartupKey, value ? "1" : "0");
        }

#if !GREENFIELD_PROJECT
        static double s_SettledSince = -1;

        [InitializeOnLoadMethod]
        static void OnLoad()
        {
            // Not in Unity's helper processes (asset import workers, the standalone Profiler) or in Multiplayer Play
            // Mode's players: each would open its own panel and log its own warnings.
            if (Application.isBatchMode || AssetDatabase.IsAssetImportWorkerProcess() ||
                UnityEditor.MPE.ProcessService.level != UnityEditor.MPE.ProcessLevel.Main || IsVirtualPlayer())
                return;
            if (StartupHandled)
                return;
            // EditorApplication.update, not delayCall: delayCall waits for a repaint, which an editor in the
            // background never does.
            EditorApplication.update -= WhenSettled;
            EditorApplication.update += WhenSettled;
        }

        // The first install goes through several imports and compiles; wait them out, and play mode, then a
        // second more for the editor layout, so the window opens once, onto a project that has finished loading.
        static void WhenSettled()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating ||
                BuildPipeline.isBuildingPlayer)
            {
                s_SettledSince = -1;
                return;
            }
            if (s_SettledSince < 0)
                s_SettledSince = EditorApplication.timeSinceStartup;
            if (EditorApplication.timeSinceStartup - s_SettledSince < 1.0)
                return;
            EditorApplication.update -= WhenSettled;
            if (StartupHandled)
                return;
            var version = CurrentVersion;
            SessionState.SetString(StartupHandledKey, version);

            var seen = EditorUserSettings.GetConfigValue(SeenVersionKey);
            var unfinished = Checks.Where(check => check.Importance == SetupImportance.Required)
                .Select(check => (check, status: Evaluate(check)))
                .Where(item => item.status.NeedsAttention)
                .ToList();

            var firstLoad = string.IsNullOrEmpty(seen);
            var updatedAndUnfinished = !firstLoad && seen != version && unfinished.Count > 0;
            // "When Unity starts": not again when an SDK update made with Unity open runs this a second time.
            var startingUp = !SessionState.GetBool(StartupRanKey, false);
            SessionState.SetBool(StartupRanKey, true);
            if (firstLoad || updatedAndUnfinished || (ShowAtStartup && startingUp))
                SdkSetupWindow.Open();
            EditorUserSettings.SetConfigValue(SeenVersionKey, version);

            if (unfinished.Count > 0)
                Debug.LogWarning($"{LogTag} Project setup isn't finished. " + UnfinishedAdvice(unfinished));
        }

        // Fix All only runs the items that have a fix; the others need the creator.
        static string UnfinishedAdvice(List<(SetupCheck check, SetupStatus status)> unfinished)
        {
            var fixable = unfinished.Where(item => item.status.State == SetupState.NeedsFix).Select(item => item.check.Title).ToList();
            var manual = unfinished.Where(item => item.status.State != SetupState.NeedsFix).Select(item => item.check.Title).ToList();
            var advice = new List<string>();
            if (fixable.Count > 0)
                advice.Add($"{string.Join(", ", fixable)}: open Creator SDK > Setup and press Fix All.");
            if (manual.Count > 0)
                advice.Add($"{string.Join(", ", manual)} {(manual.Count == 1 ? "needs a step by hand; see its row" : "need steps by hand; see their rows")} " +
                           "in Creator SDK > Setup.");
            return string.Join(" ", advice);
        }

        static string CurrentVersion => PackageManagerUtility.currentVersion ?? "unknown";

        static bool StartupHandled => SessionState.GetString(StartupHandledKey, "") == CurrentVersion;
#endif

        /// <summary>A Multiplayer Play Mode virtual player: a clone of this project, driven by the main editor.</summary>
        static bool IsVirtualPlayer() =>
            Application.dataPath.Replace('\\', '/').Contains("/Library/VP/") ||
            Environment.GetCommandLineArgs().Any(arg => arg == "--virtual-project-clone");
    }
}
