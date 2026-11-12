using System.Collections.Generic;
using System.Linq;
using BS;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BS.SDKEditor
{
    /// <summary>
    /// Finds, creates and reads the <see cref="BSWorldLink"/> that ties a scene to the world it
    /// publishes to.
    /// </summary>
    /// <remarks>
    /// The link is what lets the editor find a world's runtime overrides without asking which world
    /// every time, and it is a scene object rather than a project setting because a project can
    /// author several worlds and the mapping belongs to the scene, not to whoever last touched the
    /// Builder dropdown.
    ///
    /// The holder is tagged <c>EditorOnly</c>, which is what actually keeps it out of builds —
    /// Unity strips those roots from the build copy. CustomSceneProcessor strips it again by name so
    /// a link whose tag someone cleared cannot ship either.
    /// </remarks>
    public static class WorldLinkStamp
    {
        const string EditorOnlyTag = "EditorOnly";

        /// <summary>The link in a scene, or null when it has never been stamped.</summary>
        public static BSWorldLink Find(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return null;
            foreach (var root in scene.GetRootGameObjects())
            {
                var link = root.GetComponentInChildren<BSWorldLink>(true);
                if (link != null) return link;
            }
            return null;
        }

        /// <summary>The link in the active scene, or null.</summary>
        public static BSWorldLink FindActive() => Find(SceneManager.GetActiveScene());

        /// <summary>
        /// Record the world on a scene, creating the holder if needed.
        /// </summary>
        /// <remarks>
        /// A no-op when nothing actually changes. That matters more than it looks: this is called
        /// from the world dropdown's value-changed callback and from every world-list refresh, and
        /// marking the scene dirty on a refresh would leave anyone who merely opened the Builder
        /// with an unsaved scene and no idea why.
        /// </remarks>
        public static void Stamp(Scene scene, string worldId, string slug)
        {
            if (!scene.IsValid() || !scene.isLoaded) return;
            if (string.IsNullOrEmpty(worldId)) return;

            var link = Find(scene);
            if (link != null && link.worldId == worldId && link.slug == slug) return;

            if (link == null)
            {
                var holder = new GameObject(BSWorldLink.HolderName);
                // Tag first: an untagged frame is enough for a build kicked off in the same tick to
                // ship it.
                holder.tag = EditorOnlyTag;
                SceneManager.MoveGameObjectToScene(holder, scene);
                Undo.RegisterCreatedObjectUndo(holder, "Link Scene To World");
                link = Undo.AddComponent<BSWorldLink>(holder);
            }
            else
            {
                Undo.RecordObject(link, "Link Scene To World");
                // An older link may predate the tag, or have been reparented under a live object.
                if (link.gameObject.name == BSWorldLink.HolderName && link.gameObject.tag != EditorOnlyTag)
                    link.gameObject.tag = EditorOnlyTag;
            }

            // Changing world invalidates the sync bookkeeping: those records name entries in the
            // OTHER world's override file, and pruning against this one would delete a stranger's
            // work.
            if (link.worldId != worldId) link.syncedGraphs = new List<SyncedGraphRecord>();

            link.worldId = worldId;
            link.slug = slug;
            EditorUtility.SetDirty(link);
            EditorSceneManager.MarkSceneDirty(scene);
        }

        /// <summary>
        /// Record the world on the scene at <paramref name="scenePath"/> when it happens to be the
        /// open one.
        /// </summary>
        /// <remarks>
        /// Deliberately does NOT open the scene to stamp it. The Builder can be pointed at a scene
        /// file the user is not editing, and silently loading it would throw away their unsaved
        /// work in the scene they ARE editing.
        /// </remarks>
        public static void StampIfOpen(string scenePath, string worldId, string slug)
        {
            if (string.IsNullOrEmpty(scenePath) || string.IsNullOrEmpty(worldId)) return;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.path == scenePath) { Stamp(scene, worldId, slug); return; }
            }
        }

        /// <summary>Records still waiting to be pruned from the world's override file.</summary>
        public static List<SyncedGraphRecord> PendingSyncedGraphs(Scene scene)
        {
            var link = Find(scene);
            if (link?.syncedGraphs == null) return new List<SyncedGraphRecord>();
            return link.syncedGraphs.Where(r => r != null && !string.IsNullOrEmpty(r.entryId)).ToList();
        }
    }
}
