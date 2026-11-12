using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BS.SDKEditor
{
    [InitializeOnLoad]
    static class BSTeleporterPreview
    {
        static readonly HashSet<BSTeleporter> Previews = new HashSet<BSTeleporter>();

        static BSTeleporterPreview()
        {
            EditorApplication.update += Update;
            AssemblyReloadEvents.beforeAssemblyReload += Release;
            EditorApplication.playModeStateChanged += _ => Release();
            EditorApplication.quitting += Release;
        }

        static void Update()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            var selected = new HashSet<BSTeleporter>();
            foreach (var root in Selection.gameObjects)
            {
                if (EditorUtility.IsPersistent(root)) continue;
                foreach (var portal in root.GetComponentsInChildren<BSTeleporter>()) selected.Add(portal);
                foreach (var portal in root.GetComponentsInParent<BSTeleporter>()) selected.Add(portal);
            }
            Previews.RemoveWhere(portal =>
            {
                if (portal != null && portal.isActiveAndEnabled) return false;
                if (!ReferenceEquals(portal, null)) portal.ReleaseEditorPortalPreview();
                return true;
            });
            foreach (var portal in selected)
            {
                Previews.Add(portal);
                portal.UpdateEditorPortalPreview();
            }
        }

        static void Release()
        {
            foreach (var portal in Previews)
                if (!ReferenceEquals(portal, null)) portal.ReleaseEditorPortalPreview();
            Previews.Clear();
        }
    }
}
