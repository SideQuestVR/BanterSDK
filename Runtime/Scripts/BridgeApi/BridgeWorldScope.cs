using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BS.BridgeApi
{
    /// <summary>
    /// Which objects the typed API works on. An object must belong to the world.
    /// </summary>
    internal sealed class BridgeWorldScope
    {
        readonly Func<Transform> worldRoot;
        readonly Func<GameObject, bool> isRegistered;
        readonly Func<GameObject, bool> inRegularScene;
        readonly Func<GameObject, bool> inWorldScene;
        readonly HashSet<GameObject> sceneRoots = new HashSet<GameObject>();

        public BridgeWorldScope(Func<Transform> worldRoot, Func<GameObject, bool> isRegistered, Func<GameObject, bool> inRegularScene, Func<GameObject, bool> inWorldScene)
        {
            this.worldRoot = worldRoot;
            this.isRegistered = isRegistered;
            this.inRegularScene = inRegularScene;
            this.inWorldScene = inWorldScene;
        }

        /// <summary>The scope for the running world.</summary>
        public static BridgeWorldScope ForRunningWorld() => new BridgeWorldScope(
            () => BSScene.Current?.settings?.parentTransform,
            go => go.TryGetComponent<BSObjectId>(out _) && BSScene.Current?.GetGameObject(BSScene.UnityId(go)) == go,
            BSWorldScope.Contains,
            InCurrentWorldScene);

        static bool InCurrentWorldScene(GameObject go)
        {
            int scene = BSUnityEventGuard.CurrentWorldScene;
#if !GREENFIELD_PROJECT
            // Play Mode in a creator's project runs the open scene, which no bundle load announces.
            if (scene == 0) return true;
#endif
            return go.scene.handle == scene;
        }

        /// <summary>The world root, or null when there is none.</summary>
        public Transform Root => worldRoot();

        public bool Contains(GameObject go)
        {
            if (go == null) return false;
            var t = go.transform;
            var root = worldRoot();
            if (root != null && t == root) return false;
            if (root != null && t.IsChildOf(root)) return true;
            if (!inRegularScene(go)) return false;
            if (!inWorldScene(go)) return false;
            return isRegistered(go) || sceneRoots.Contains(t.root.gameObject);
        }

        /// <summary>A scene has replaced the last one. If it turns out to be the world's, what is in it now is the world's.</summary>
        public void SceneLoaded(Scene scene)
        {
            sceneRoots.Clear();
            foreach (var go in scene.GetRootGameObjects()) sceneRoots.Add(go);
        }

        public void AddSceneRoot(GameObject go) => sceneRoots.Add(go);

        public void Clear() => sceneRoots.Clear();
    }
}
