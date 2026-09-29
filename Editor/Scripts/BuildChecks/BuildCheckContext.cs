using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BS.SDKEditor.BuildChecks
{
    /// <summary>
    /// What the checks look at: the scene the builder is about to build, and the build targets.
    /// If that scene is open it is read as it is (unsaved edits included); otherwise a preview copy is
    /// opened and closed again on <see cref="Dispose"/>. Either way the active scene, the loaded
    /// scenes and their dirty state are left alone.
    /// </summary>
    public sealed class BuildCheckContext : IDisposable
    {
        public string ScenePath { get; }
        public Scene Scene { get; }
        public bool IsPreview { get; }
        public BuildTarget[] Targets { get; }
        /// <summary>True just before a build: checks may do slower, more thorough work.</summary>
        public bool ForBuild { get; }

        string[] dependencies;

        BuildCheckContext(string scenePath, Scene scene, bool isPreview, BuildTarget[] targets, bool forBuild)
        {
            ScenePath = scenePath;
            Scene = scene;
            IsPreview = isPreview;
            Targets = targets ?? Array.Empty<BuildTarget>();
            ForBuild = forBuild;
        }

        public static BuildCheckContext Open(string scenePath, BuildTarget[] targets, bool forBuild)
        {
            var loaded = SceneManager.GetSceneByPath(scenePath);
            if (loaded.IsValid() && loaded.isLoaded)
                return new BuildCheckContext(scenePath, loaded, false, targets, forBuild);
            var preview = EditorSceneManager.OpenPreviewScene(scenePath);
            return new BuildCheckContext(scenePath, preview, true, targets, forBuild);
        }

        /// <summary>Checks an already open (or preview) scene, which the caller owns. For tests.</summary>
        public static BuildCheckContext ForScene(Scene scene, BuildTarget[] targets, string scenePath = null) =>
            new BuildCheckContext(scenePath ?? scene.path, scene, false, targets, false);

        public void Dispose()
        {
            if (IsPreview && Scene.IsValid())
                EditorSceneManager.ClosePreviewScene(Scene);
        }

        public IEnumerable<GameObject> Roots => Scene.IsValid() ? Scene.GetRootGameObjects() : Array.Empty<GameObject>();

        /// <summary>
        /// Every object that ships: inactive ones included (they are in the bundle), EditorOnly
        /// subtrees and what the build strips (the authoring BSStarterUpper) left out.
        /// </summary>
        public IEnumerable<GameObject> AllObjects()
        {
            foreach (var root in Roots)
            {
                foreach (var go in Walk(root.transform))
                    yield return go;
            }
        }

        static IEnumerable<GameObject> Walk(Transform transform)
        {
            if (IsStripped(transform.gameObject))
                yield break;
            yield return transform.gameObject;
            foreach (Transform child in transform)
            {
                foreach (var go in Walk(child))
                    yield return go;
            }
        }

        static bool IsStripped(GameObject go) =>
            go.CompareTag("EditorOnly")
            || go.name == CustomSceneProcessor.SceneBeeBuildMarkerName
            || go.GetComponent<BSStarterUpper>() != null;

        /// <summary>The components of type <typeparamref name="T"/> on every object that ships.</summary>
        public IEnumerable<T> Components<T>() where T : Component =>
            AllObjects().SelectMany(go => go.GetComponents<T>()).Where(component => component != null);

        /// <summary>Every asset the scene depends on, recursively.</summary>
        public string[] Dependencies
        {
            get
            {
                if (dependencies == null)
                {
                    dependencies = string.IsNullOrEmpty(ScenePath)
                        ? Array.Empty<string>()
                        : AssetDatabase.GetDependencies(ScenePath, true).Where(path => path != ScenePath).ToArray();
                }
                return dependencies;
            }
        }

        public BuildCheckTarget Target(GameObject go) => BuildCheckTarget.ForObject(go, ScenePath);

        public IEnumerable<BuildCheckTarget> TargetsOf<T>(IEnumerable<T> components) where T : Component =>
            components.Select(component => Target(component.gameObject));
    }
}
