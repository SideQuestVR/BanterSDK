using UnityEngine;

namespace BS
{
    /// <summary>
    /// Which scene objects belong to the current world, as opposed to the client around it (player rig, menus, platform
    /// services). Page-driven tools that resolve objects by name, path or id must stay inside this scope; before it, the
    /// page's script-graph bridge could bind graphs to any ScriptMachine or GameObject Unity could find
    /// (S1 in notes/creator-sdk-issues-report-2026-11-03.md).
    /// </summary>
    /// <remarks>
    /// The world is everything under the scene settings' world root (itself DontDestroyOnLoad), plus any regular loaded
    /// scene other than the SDK host's own: scene bundles load with LoadSceneMode.Single, so the client keeps everything it
    /// owns in DontDestroyOnLoad or in the host's scene.
    /// </remarks>
    public static class BSWorldScope
    {
        static BSStarterUpper host;

        public static bool Contains(GameObject go)
        {
            if (go == null) return false;
            // A runtime boundary: in edit mode there is no page and no world, and editor tools (the script-graph sync
            // window) legitimately see every open scene.
            if (!Application.isPlaying) return true;
            var root = BSScene.Instance()?.settings?.parentTransform;
            if (root != null && go.transform.IsChildOf(root)) return true;

            var scene = go.scene;
            if (!scene.IsValid() || scene.name == "DontDestroyOnLoad") return false;
            // A world the content guard refused stays deactivated until a newer load or Void replaces it: never world content.
            if (BSUnityEventGuard.IsRefusedScene(scene)) return false;
            if (host == null) host = Object.FindAnyObjectByType<BSStarterUpper>(FindObjectsInactive.Include);
            if (host != null && scene == host.gameObject.scene) return false;
            return true;
        }

        public static bool Contains(Component component) => component != null && Contains(component.gameObject);
    }
}
