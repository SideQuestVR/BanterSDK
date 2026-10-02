using System;
using System.Collections.Generic;
using System.Linq;
using LongBunnyLabs;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BS.SDKEditor.BuildChecks
{
    public sealed class BuildChecklistResult
    {
        public string ScenePath { get; }
        public IReadOnlyList<BuildCheckIssue> Issues { get; }

        public BuildChecklistResult(string scenePath, IReadOnlyList<BuildCheckIssue> issues)
        {
            ScenePath = scenePath;
            Issues = issues;
        }

        public int Count(BuildCheckSeverity severity) => Issues.Count(issue => issue.Severity == severity);

        /// <summary>Errors nobody can build past.</summary>
        public IEnumerable<BuildCheckIssue> Blockers =>
            Issues.Where(issue => issue.Severity == BuildCheckSeverity.Error && !issue.Overridable);

        public bool BlocksInteractive => Blockers.Any();

        /// <summary>Unattended builds can't choose to build anyway, so any error stops them.</summary>
        public bool BlocksBatch => Issues.Any(issue => issue.Severity == BuildCheckSeverity.Error);

        /// <summary>Anything worse than a note, i.e. worth confirming before building.</summary>
        public bool HasProblems => Issues.Any(issue => issue.Severity != BuildCheckSeverity.Info);

        public string Summary
        {
            get
            {
                var parts = new List<string>();
                Add(Count(BuildCheckSeverity.Error), "error");
                Add(Count(BuildCheckSeverity.Warning), "warning");
                Add(Count(BuildCheckSeverity.Info), "note");
                return parts.Count == 0 ? "All checks passed" : string.Join(", ", parts);

                void Add(int count, string noun)
                {
                    if (count > 0)
                        parts.Add(count + " " + noun + (count == 1 ? "" : "s"));
                }
            }
        }
    }

    /// <summary>
    /// The pre-build checklist: every <see cref="BuildCheck"/>, run against the scene the builder is
    /// about to build. It only reports. Fixes run one at a time when the creator clicks them, never
    /// during a build or in batch mode.
    /// </summary>
    public static class BuildChecklist
    {
        const string LogTag = "[Build checklist]";
        static readonly BuildTarget[] DefaultTargets = { BuildTarget.Android, BuildTarget.StandaloneWindows };

        public static List<BuildCheck> CreateChecks() =>
            TypeCache.GetTypesDerivedFrom<BuildCheck>()
                .Where(type => !type.IsAbstract && type.GetConstructor(Type.EmptyTypes) != null)
                .Select(type => (BuildCheck)Activator.CreateInstance(type))
                .OrderBy(check => check.Order)
                .ThenBy(check => check.Id, StringComparer.Ordinal)
                .ToList();

        public static BuildChecklistResult Run(string scenePath, BuildTarget[] targets, bool forBuild,
            Action<string, float> progress = null)
        {
            if (string.IsNullOrEmpty(scenePath))
                throw new ArgumentException("The checklist needs a saved scene.", nameof(scenePath));
            using (var context = BuildCheckContext.Open(scenePath, targets ?? DefaultTargets, forBuild))
                return Run(context, CreateChecks(), progress);
        }

        public static BuildChecklistResult Run(BuildCheckContext context, IReadOnlyList<BuildCheck> checks,
            Action<string, float> progress = null)
        {
            var issues = new List<BuildCheckIssue>();
            for (var i = 0; i < checks.Count; i++)
            {
                var check = checks[i];
                progress?.Invoke(check.Title, i / (float)checks.Count);
                var found = new List<BuildCheckIssue>();
                try
                {
                    check.Run(context, found);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    found.Clear();
                    found.Add(new BuildCheckIssue(check.Id, BuildCheckSeverity.Error,
                        $"The \"{check.Title}\" check couldn't run", e.Message)
                    {
                        Overridable = !check.BlockOnException,
                    });
                }
                issues.AddRange(found);
            }
            return new BuildChecklistResult(context.ScenePath, issues);
        }

        /// <summary>Writes every issue to the Console, at its severity.</summary>
        public static void Log(BuildChecklistResult result)
        {
            foreach (var issue in result.Issues)
            {
                var line = $"{LogTag} {issue}";
                switch (issue.Severity)
                {
                    case BuildCheckSeverity.Error: Debug.LogError(line); break;
                    case BuildCheckSeverity.Warning: Debug.LogWarning(line); break;
                    default: Debug.Log(line); break;
                }
            }
            Debug.Log($"{LogTag} {result.ScenePath}: {result.Summary}.");
        }

        /// <summary>
        /// Runs an issue's fix, after asking to open the scene if the fix edits it and it isn't open.
        /// Returns whether anything changed.
        /// </summary>
        public static bool ApplyFix(BuildCheckIssue issue, string scenePath)
        {
            if (issue?.Fix == null || Application.isBatchMode)
                return false;
            if (issue.Fix.NeedsLoadedScene && !IsLoaded(scenePath))
            {
                if (!EditorUtility.DisplayDialog("Open the scene?",
                        $"\"{issue.Fix.Label}\" changes {scenePath}, which isn't open. Open it now?", "Open scene", "Cancel"))
                    return false;
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    return false;
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            }

            Undo.IncrementCurrentGroup();
            var changed = issue.Fix.Apply(issue);
            Undo.SetCurrentGroupName(issue.Fix.Label);
            if (changed && issue.Fix.NeedsLoadedScene)
                EditorSceneManager.MarkSceneDirty(SceneManager.GetSceneByPath(scenePath));
            Debug.Log($"{LogTag} {issue.Fix.Label}: {(changed ? "done" : "nothing changed")}.");
            return changed;
        }

        /// <summary>Selects what the issue points at. False if none of it can be found (scene not open).</summary>
        public static bool Select(BuildCheckIssue issue)
        {
            var objects = issue.Targets.Select(target => target.Resolve()).Where(obj => obj != null).ToArray();
            if (objects.Length == 0)
                return false;
            Selection.objects = objects;
            EditorGUIUtility.PingObject(objects[0]);
            return true;
        }

        static bool IsLoaded(string scenePath)
        {
            var scene = SceneManager.GetSceneByPath(scenePath);
            return scene.IsValid() && scene.isLoaded;
        }

        [MenuItem("Creator SDK/Tools/Run Build Checklist")]
        static void RunFromMenu()
        {
            var scenePath = ProjectPrefs.GetString("BanterBuilder_ScenePath", "");
            if (string.IsNullOrEmpty(scenePath))
                scenePath = SceneManager.GetActiveScene().path;
            if (string.IsNullOrEmpty(scenePath))
            {
                Debug.LogWarning($"{LogTag} Save the scene first, or pick it in the Builder.");
                return;
            }
            try
            {
                Log(Run(scenePath, null, false, (title, done) => EditorUtility.DisplayProgressBar("Build checklist", title, done)));
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }
    }
}
