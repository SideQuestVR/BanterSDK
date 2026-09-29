using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace BS.SDKEditor.BuildChecks
{
    public enum BuildCheckSeverity
    {
        /// <summary>Worth knowing; never gets in the way.</summary>
        Info,
        /// <summary>Likely a problem. The build goes ahead, interactively after "Build anyway".</summary>
        Warning,
        /// <summary>A problem. Blocks unattended builds; blocks interactive ones unless overridable.</summary>
        Error,
    }

    /// <summary>
    /// One check in the pre-build checklist. Subclasses are found by type, so adding one is enough to
    /// have it run. Checks only look: they never change the project. A change is offered as a
    /// <see cref="BuildCheckFix"/> that runs when the creator clicks it.
    /// </summary>
    public abstract class BuildCheck
    {
        /// <summary>Stable id, e.g. "scene.missing-scripts". Used in logs.</summary>
        public abstract string Id { get; }

        /// <summary>Short name shown while the checklist runs.</summary>
        public abstract string Title { get; }

        /// <summary>Checks run in ascending order.</summary>
        public virtual int Order => 100;

        /// <summary>
        /// When the check itself fails: true blocks the build (the answer matters too much to guess),
        /// false reports it as an error the creator can build past.
        /// </summary>
        public virtual bool BlockOnException => false;

        public abstract void Run(BuildCheckContext context, List<BuildCheckIssue> issues);
    }

    /// <summary>Something a check found.</summary>
    public sealed class BuildCheckIssue
    {
        public string CheckId;
        public BuildCheckSeverity Severity;
        /// <summary>For errors: whether the creator can choose to build anyway.</summary>
        public bool Overridable = true;
        public string Title;
        public string Details;
        public readonly List<BuildCheckTarget> Targets = new List<BuildCheckTarget>();
        public BuildCheckFix Fix;

        public BuildCheckIssue(string checkId, BuildCheckSeverity severity, string title, string details = null)
        {
            CheckId = checkId;
            Severity = severity;
            Title = title;
            Details = details;
        }

        public BuildCheckIssue WithTargets(IEnumerable<BuildCheckTarget> targets)
        {
            Targets.AddRange(targets);
            return this;
        }

        public BuildCheckIssue WithFix(string label, Func<BuildCheckIssue, bool> apply, bool needsLoadedScene = true)
        {
            Fix = new BuildCheckFix { Label = label, Apply = apply, NeedsLoadedScene = needsLoadedScene };
            return this;
        }

        /// <summary>The scene objects this issue is about, from the loaded scene. Empty if it isn't loaded.</summary>
        public IEnumerable<T> Resolve<T>() where T : Object =>
            Targets.Select(target => target.Resolve()).OfType<T>();

        public override string ToString()
        {
            var text = new StringBuilder($"[{Severity}] {Title}");
            if (!string.IsNullOrEmpty(Details))
                text.Append('\n').Append(Details);
            foreach (var target in Targets.Take(20))
                text.Append("\n  - ").Append(target.Label);
            if (Targets.Count > 20)
                text.Append($"\n  ...and {Targets.Count - 20} more");
            return text.ToString();
        }
    }

    /// <summary>
    /// A change the creator can ask for. It runs only when clicked, never during a build or in batch
    /// mode, with undo where Unity supports it.
    /// </summary>
    public sealed class BuildCheckFix
    {
        public string Label;
        /// <summary>Returns whether anything changed.</summary>
        public Func<BuildCheckIssue, bool> Apply;
        /// <summary>Whether the fix edits the checked scene, which then has to be open.</summary>
        public bool NeedsLoadedScene = true;
    }

    /// <summary>
    /// What an issue points at: an asset, or an object in a scene. Scene objects are remembered by
    /// scene and hierarchy position, not by reference, because the checklist may have inspected a
    /// preview copy of the scene that is gone by the time anyone clicks.
    /// </summary>
    public sealed class BuildCheckTarget
    {
        public string Label;
        public Object Asset;
        public string ScenePath;
        public int[] SiblingPath;
        public string[] NamePath;

        public static BuildCheckTarget ForAsset(Object asset) =>
            new BuildCheckTarget { Asset = asset, Label = asset != null ? AssetDatabase.GetAssetPath(asset) : "(missing)" };

        public static BuildCheckTarget ForObject(GameObject go, string scenePath)
        {
            var siblings = new List<int>();
            var names = new List<string>();
            for (var t = go.transform; t != null; t = t.parent)
            {
                siblings.Insert(0, t.GetSiblingIndex());
                names.Insert(0, t.name);
            }
            return new BuildCheckTarget
            {
                ScenePath = scenePath,
                SiblingPath = siblings.ToArray(),
                NamePath = names.ToArray(),
                Label = string.Join("/", names),
            };
        }

        /// <summary>The asset, or the scene object if its scene is loaded and it's still where it was.</summary>
        public Object Resolve()
        {
            if (Asset != null)
                return Asset;
            if (SiblingPath == null || SiblingPath.Length == 0)
                return null;
            var scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.IsValid() || !scene.isLoaded)
                return null;
            var roots = scene.GetRootGameObjects();
            if (SiblingPath[0] >= roots.Length)
                return null;
            var current = roots[SiblingPath[0]].transform;
            for (var i = 1; i < SiblingPath.Length; i++)
            {
                if (SiblingPath[i] >= current.childCount)
                    return null;
                current = current.GetChild(SiblingPath[i]);
            }
            // The hierarchy may have changed since the check ran.
            return NamesMatch(current) ? current.gameObject : null;
        }

        bool NamesMatch(Transform transform)
        {
            for (var i = NamePath.Length - 1; i >= 0; i--, transform = transform.parent)
            {
                if (transform == null || transform.name != NamePath[i])
                    return false;
            }
            return transform == null;
        }
    }
}
