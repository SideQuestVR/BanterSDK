using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BS.SDKEditor
{
    /*
     * Owns the BSSnippet-component ↔ <bs-snippet> element pairing lifecycle:
     *   - assigns instance ids, resolves Ctrl+D / prefab-instance id collisions,
     *   - heals missing elements (undo of a removal, hand-deleted section) from an in-memory
     *     stash, a same-slug sibling, or as a last resort a re-fetch,
     *   - replaces the element when the slug changes,
     *   - removes the element when the component is removed while its scene stays loaded.
     *
     * Undo honesty: component fields are undoable, the HTML file is not. Undoing a component
     * removal or a slug change lands here (via OnValidate/hierarchyChanged) and the element is
     * healed — preferring the stashed copy so local attribute edits survive the round trip.
     *
     * All work is deferred to EditorApplication.update: OnValidate and scene callbacks are not
     * safe places for asset-database writes.
     */
    [InitializeOnLoad]
    public static class SnippetReconciler
    {
        // instanceId -> component + owning scene path, snapshot of the last reconcile pass.
        // Removal detection compares this against the live scene, so entries must be dropped
        // WITHOUT touching HTML when their scene closes (closed-scene elements must survive).
        static readonly Dictionary<string, (BSSnippet component, string scenePath)> _registry = new Dictionary<string, (BSSnippet, string)>();
        static readonly List<BSSnippet> _pendingValidated = new List<BSSnippet>();
        static readonly HashSet<int> _fetchInFlight = new HashSet<int>();
        // Standing failure per component, WITH the slug that failed: reconcile passes run on
        // every hierarchy change, so without this latch a bad slug (or a non-compliant server)
        // would be re-fetched in a storm. Cleared by editing the slug or the explicit Refresh.
        static readonly Dictionary<int, (string slug, string message)> _lastErrors = new Dictionary<int, (string, string)>();
        // Stash of recently removed/replaced elements so undo heals instantly, offline, and with
        // local attribute edits intact instead of a pristine server copy.
        static readonly Dictionary<string, XElement> _removedStash = new Dictionary<string, XElement>();
        static bool _reconcileQueued;

        static SnippetReconciler()
        {
            BSSnippet.EditorValidated += OnComponentValidated;
            EditorApplication.hierarchyChanged += QueueReconcile;
            EditorSceneManager.sceneClosing += OnSceneClosing;
            EditorApplication.update += Drain;
            QueueReconcile();
        }

        public static void QueueReconcile() => _reconcileQueued = true;

        // Deterministic entry point: the update-tick Drain can lag when the editor is unfocused
        // (background editors pump EditorApplication.update rarely), so anything that needs the
        // pass to have happened NOW — tests, tools driving the editor headlessly — calls this.
        public static void ReconcileNow()
        {
            _reconcileQueued = true;
            Drain();
        }

        public static string GetLastError(BSSnippet component) =>
            component != null && _lastErrors.TryGetValue(component.GetInstanceID(), out var e) ? e.message : null;

        static bool HasStandingError(BSSnippet component) =>
            _lastErrors.TryGetValue(component.GetInstanceID(), out var e) && e.slug == component.Slug;

        public static bool IsFetching(BSSnippet component) =>
            component != null && _fetchInFlight.Contains(component.GetInstanceID());

        public static void EnsureInstanceId(BSSnippet component)
        {
            if (component == null || !string.IsNullOrEmpty(component.InstanceId)) return;
            AssignNewId(component);
        }

        /*
         * Explicit re-fetch (the inspector's Refresh button): discards local attribute edits by
         * design, keeps the instance id.
         */
        public static void Refetch(BSSnippet component)
        {
            if (component == null || string.IsNullOrEmpty(component.Slug)) return;
            StartFetch(component);
        }

        static void OnComponentValidated(BSSnippet component)
        {
            if (component != null && !_pendingValidated.Contains(component))
                _pendingValidated.Add(component);
            _reconcileQueued = true;
        }

        static void OnSceneClosing(UnityEngine.SceneManagement.Scene scene, bool removingScene)
        {
            foreach (var id in _registry.Where(kv => kv.Value.scenePath == scene.path).Select(kv => kv.Key).ToList())
                _registry.Remove(id);
        }

        static void Drain()
        {
            if (!_reconcileQueued && _pendingValidated.Count == 0) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return; // runs after play mode ends
            _reconcileQueued = false;
            var validated = _pendingValidated.Where(c => c != null).ToList();
            _pendingValidated.Clear();
            try
            {
                Reconcile(validated);
            }
            catch (Exception e)
            {
                Debug.LogError("[BSSnippet] Reconcile failed: " + e);
            }
        }

        static void Reconcile(List<BSSnippet> justValidated)
        {
            var components = UnityEngine.Object
                .FindObjectsByType<BSSnippet>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(c => c.gameObject.scene.IsValid()
                            && c.gameObject.scene.isLoaded
                            && !EditorSceneManager.IsPreviewSceneObject(c))
                .ToList();

            ResolveDuplicateIds(components, justValidated);

            foreach (var component in components)
            {
                if (string.IsNullOrEmpty(component.Slug))
                {
                    // Slug cleared = detach. Stash so undoing the clear heals with edits intact.
                    if (!string.IsNullOrEmpty(component.InstanceId))
                    {
                        var abandoned = SnippetHtmlSync.Get(component.InstanceId);
                        if (abandoned != null)
                        {
                            _removedStash[component.InstanceId] = new XElement(abandoned);
                            SnippetHtmlSync.Remove(component.InstanceId);
                        }
                    }
                    continue;
                }

                EnsureInstanceId(component);
                var element = SnippetHtmlSync.Get(component.InstanceId);

                if (element == null)
                {
                    // Heal order: stash (instant, keeps edits) -> same-slug sibling clone (no
                    // network, honors fetch-once) -> re-fetch (last resort).
                    if (_removedStash.TryGetValue(component.InstanceId, out var stashed)
                        && (string)stashed.Attribute("name") == component.Slug)
                    {
                        _removedStash.Remove(component.InstanceId);
                        SnippetHtmlSync.Upsert(component.InstanceId, new XElement(stashed));
                        continue;
                    }
                    var sibling = SnippetHtmlSync.FindAnyBySlug(component.Slug);
                    if (sibling != null)
                    {
                        SnippetHtmlSync.Upsert(component.InstanceId, new XElement(sibling));
                        continue;
                    }
                    if (!HasStandingError(component)) StartFetch(component);
                }
                else if ((string)element.Attribute("name") != component.Slug)
                {
                    // Slug edited (or edit undone): this element belongs to a different snippet
                    // now. Stash the old one, then fetch the new slug under the same id.
                    _removedStash[component.InstanceId] = new XElement(element);
                    if (!HasStandingError(component)) StartFetch(component);
                }
            }

            // Components that vanished since last pass: removed by the user if their scene is
            // still loaded (undo re-adds them and heals above), otherwise a scene unload.
            var live = new HashSet<string>(components.Where(c => !string.IsNullOrEmpty(c.InstanceId)).Select(c => c.InstanceId));
            foreach (var kv in _registry.ToList())
            {
                if (kv.Value.component != null || live.Contains(kv.Key)) continue;
                var scene = EditorSceneManager.GetSceneByPath(kv.Value.scenePath);
                if (scene.IsValid() && scene.isLoaded)
                {
                    var element = SnippetHtmlSync.Get(kv.Key);
                    if (element != null)
                    {
                        _removedStash[kv.Key] = new XElement(element);
                        SnippetHtmlSync.Remove(kv.Key);
                    }
                }
                _registry.Remove(kv.Key);
            }

            _registry.Clear();
            foreach (var component in components)
                if (!string.IsNullOrEmpty(component.InstanceId))
                    _registry[component.InstanceId] = (component, component.gameObject.scene.path);
        }

        static void ResolveDuplicateIds(List<BSSnippet> components, List<BSSnippet> justValidated)
        {
            foreach (var group in components.Where(c => !string.IsNullOrEmpty(c.InstanceId)).GroupBy(c => c.InstanceId))
            {
                if (group.Count() < 2) continue;
                // Keep the id on the component least likely to be the fresh duplicate: prefer one
                // that was NOT just validated (Ctrl+D fires OnValidate on the copy), otherwise
                // first wins (e.g. several prefab instances loading at once).
                var winner = group.FirstOrDefault(c => !justValidated.Contains(c)) ?? group.First();
                foreach (var loser in group)
                {
                    if (loser == winner) continue;
                    AssignNewId(loser);
                    // The per-component pass clones the sibling element for the new id.
                }
            }
        }

        static void AssignNewId(BSSnippet component)
        {
            var serialized = new SerializedObject(component);
            serialized.FindProperty("instanceId").stringValue = Guid.NewGuid().ToString("N");
            serialized.ApplyModifiedProperties();
            PrefabUtility.RecordPrefabInstancePropertyModifications(component);
        }

        static void StartFetch(BSSnippet component)
        {
            var key = component.GetInstanceID();
            if (!_fetchInFlight.Add(key)) return;
            _lastErrors.Remove(key);
            var slug = component.Slug;
            SnippetApi.Fetch(slug,
                element =>
                {
                    _fetchInFlight.Remove(key);
                    if (component == null) return; // component died mid-fetch
                    SnippetApi.ApplyFetched(component, element);
                },
                error =>
                {
                    _fetchInFlight.Remove(key);
                    _lastErrors[key] = (slug, error.Message);
                    Debug.LogWarning($"[BSSnippet] Fetching '{slug}' failed: {error.Message}");
                });
        }

        // ---- orphaned elements -------------------------------------------------------------

        /*
         * An element whose component is gone (deleted across a domain reload, say, or its scene deleted) still has
         * everything the space uses: the slug (`name`), the instance id, the title and every setting. The runtime
         * reads only the element, so a new object with a BSSnippet carrying that slug and id picks it up exactly as
         * it was, local edits included. What's lost is the old object's name, transform and scene, none of which
         * reach the space.
         *
         * Deliberately manual: an element whose component sits in a scene that isn't open looks orphaned from here.
         * Saved scenes and prefabs are searched for the id first, and the ones they still use are left alone.
         * Run from the Setup panel's Tools section.
         */
#if GREENFIELD_PROJECT
        [MenuItem("Greenfield/Creator SDK/Recover Orphaned Snippets...")]
#endif
        internal static void RecoverOrphanedSnippets()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Snippets", "Stop Play mode first: objects added in Play mode are lost when it ends.", "OK");
                return;
            }
            if (SnippetHtmlSync.LoadFailed)
            {
                EditorUtility.DisplayDialog("Snippets", $"The snippet section in {SnippetHtmlSync.AssetPath} can't be read. Fix it first; the Console says what's wrong.", "OK");
                return;
            }

            List<Orphan> orphans;
            try
            {
                orphans = FindOrphans(SnippetHtmlSync.All(), ClaimedInstanceIds(), ids => FindUsers(ids, SavedScenesAndPrefabs()));
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            var stranded = orphans.Where(orphan => orphan.UsedBy == null).ToList();
            var usedElsewhere = orphans.Where(orphan => orphan.UsedBy != null).ToList();
            var leftAlone = usedElsewhere.Count == 0 ? "" :
                "\n\nLeft as they are, because a scene that isn't open or a prefab still uses them:\n" +
                Bullets(usedElsewhere, orphan => $"{orphan.Label} ({orphan.UsedBy})");
            if (stranded.Count == 0)
            {
                EditorUtility.DisplayDialog("Snippets", "Every snippet in index.html belongs to an object." + leftAlone, "OK");
                return;
            }

            var scene = SceneManager.GetActiveScene();
            var sceneName = string.IsNullOrEmpty(scene.name) ? "the open scene" : $"the scene \"{scene.name}\"";
            var recoverable = stranded.Where(orphan => orphan.CanRecover).ToList();
            var unnamed = stranded.Count - recoverable.Count;
            var message = "These snippets in index.html have no object in any scene or prefab:\n" + Bullets(stranded, orphan => orphan.Label) + "\n\n" +
                          (recoverable.Count > 0
                              ? $"Add to Scene creates an object for each in {sceneName}, at the origin, linked to its snippet: its settings are kept. "
                              : "") +
                          "Remove deletes them from index.html." +
                          (unnamed > 0 ? $"\n\n{unnamed} of them {(unnamed == 1 ? "has" : "have")} no name, so only Remove applies." : "") +
                          leftAlone;

            if (recoverable.Count == 0)
            {
                if (EditorUtility.DisplayDialog("Orphaned snippets", message, "Remove", "Cancel"))
                    RemoveOrphans(stranded);
                return;
            }
            switch (EditorUtility.DisplayDialogComplex("Orphaned snippets", message, "Add to Scene", "Cancel", "Remove"))
            {
                case 0:
                    var created = Recover(recoverable, scene);
                    Selection.objects = created.Cast<UnityEngine.Object>().ToArray();
                    if (created.Count > 0)
                        EditorGUIUtility.PingObject(created[0]);
                    Debug.Log($"[BSSnippet] Added {created.Count} object(s) to {sceneName} for orphaned snippets: " +
                              string.Join(", ", recoverable.Select(orphan => orphan.Label)) + ".");
                    break;
                case 2:
                    RemoveOrphans(stranded);
                    break;
            }
        }

        /// <summary>An element in index.html that no BSSnippet in a loaded scene claims.</summary>
        internal sealed class Orphan
        {
            public XElement Element;
            public string InstanceId;
            public string Slug;
            public string Title;
            /// <summary>The saved scene that isn't open, or the prefab, that still uses it; null when nothing does.</summary>
            public string UsedBy;

            /// <summary>Without a slug a component couldn't pair with it: an empty slug detaches.</summary>
            public bool CanRecover => UsedBy == null && !string.IsNullOrEmpty(Slug);

            public string Label =>
                !string.IsNullOrEmpty(Title) ? (string.IsNullOrEmpty(Slug) || Slug == Title ? Title : $"{Title} ({Slug})")
                : !string.IsNullOrEmpty(Slug) ? Slug : "(unnamed)";
        }

        /*
         * Elements no component in a loaded scene claims. A hand-written element (no instance id) isn't one: Unity
         * never pairs those, and the runtime loads them as they are. `whereUsed` maps the unclaimed ids to the saved
         * scene or prefab that still uses them, and is only asked when there are any.
         */
        internal static List<Orphan> FindOrphans(IEnumerable<XElement> elements, ICollection<string> claimed,
            Func<ICollection<string>, IDictionary<string, string>> whereUsed)
        {
            var unclaimed = elements
                .Select(element => (element, id: (string)element.Attribute(SnippetHtmlSync.InstanceAttribute)))
                .Where(item => !string.IsNullOrEmpty(item.id) && !claimed.Contains(item.id))
                .ToList();
            if (unclaimed.Count == 0)
                return new List<Orphan>();
            var usedBy = whereUsed(unclaimed.Select(item => item.id).Distinct().ToList());
            return unclaimed.Select(item => new Orphan
            {
                Element = item.element,
                InstanceId = item.id,
                Slug = (string)item.element.Attribute("name"),
                Title = (string)item.element.Attribute("title"),
                UsedBy = usedBy.TryGetValue(item.id, out var path) ? path : null,
            }).ToList();
        }

        /// <summary>For each id, the first file whose text contains it. Stops reading files once every id is found.</summary>
        internal static Dictionary<string, string> FindUsers(ICollection<string> ids, IEnumerable<(string path, string text)> files)
        {
            var users = new Dictionary<string, string>();
            foreach (var (path, text) in files)
            {
                foreach (var id in ids)
                {
                    if (!users.ContainsKey(id) && text.Contains(id))
                        users[id] = path;
                }
                if (users.Count == ids.Count)
                    break;
            }
            return users;
        }

        /*
         * Creates an object at the origin for each orphan, with a BSSnippet that pairs with its element. At the origin
         * with no rotation the gizmos show what the space does: the runtime places a snippet from its own position
         * attribute, measured from the world origin. One undo step; undoing it removes the objects, and with them
         * their elements, as deleting any snippet object does (redo brings both back).
         */
        internal static List<GameObject> Recover(IEnumerable<Orphan> orphans, UnityEngine.SceneManagement.Scene scene)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Recover orphaned snippets");
            var group = Undo.GetCurrentGroup();
            var created = new List<GameObject>();
            foreach (var orphan in orphans.Where(orphan => orphan.CanRecover))
            {
                var id = orphan.InstanceId;
                // A pasted copy can share its id with another element. Unity pairs only the first, so a copy gets its own.
                if (SnippetHtmlSync.Get(id) != orphan.Element)
                {
                    id = Guid.NewGuid().ToString("N");
                    SnippetHtmlSync.SetInstanceId(orphan.Element, id);
                }

                var go = new GameObject(!string.IsNullOrEmpty(orphan.Title) ? orphan.Title : orphan.Slug);
                if (scene.IsValid() && scene.isLoaded && go.scene != scene)
                    SceneManager.MoveGameObjectToScene(go, scene);
                Undo.RegisterCreatedObjectUndo(go, "Recover orphaned snippets");
                var component = go.AddComponent<BSSnippet>();
                var serialized = new SerializedObject(component);
                serialized.FindProperty("slug").stringValue = orphan.Slug;
                serialized.FindProperty("instanceId").stringValue = id;
                serialized.FindProperty("cachedTitle").stringValue = orphan.Title ?? "";
                serialized.FindProperty("cachedDescription").stringValue = (string)orphan.Element.Attribute("description") ?? "";
                serialized.ApplyModifiedPropertiesWithoutUndo();
                created.Add(go);
            }
            Undo.CollapseUndoOperations(group);
            // Register them now, so the next pass treats them like any other snippet object.
            ReconcileNow();
            SnippetHtmlSync.FlushNow();
            return created;
        }

        static void RemoveOrphans(IEnumerable<Orphan> orphans)
        {
            foreach (var orphan in orphans)
                SnippetHtmlSync.RemoveElement(orphan.Element);
            SnippetHtmlSync.FlushNow();
        }

        static HashSet<string> ClaimedInstanceIds() =>
            new HashSet<string>(UnityEngine.Object
                .FindObjectsByType<BSSnippet>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(c => c.gameObject.scene.isLoaded && !EditorSceneManager.IsPreviewSceneObject(c))
                .Select(c => c.InstanceId)
                .Where(id => !string.IsNullOrEmpty(id)));

        /*
         * The project's saved scenes that aren't open, and its prefabs. A BSSnippet keeps its instance id in the file
         * as plain text ("instanceId: <id>"); a binary-serialized file holds the same characters. Open scenes are
         * left out: what's in memory is what counts for them.
         */
        static IEnumerable<(string path, string text)> SavedScenesAndPrefabs()
        {
            var open = new HashSet<string>(Enumerable.Range(0, SceneManager.sceneCount)
                .Select(SceneManager.GetSceneAt)
                .Where(scene => scene.isLoaded)
                .Select(scene => scene.path));
            var paths = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" })
                .Concat(AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
                .Select(AssetDatabase.GUIDToAssetPath)
                .Distinct()
                .Where(path => !open.Contains(path))
                .ToList();
            for (var i = 0; i < paths.Count; i++)
            {
                EditorUtility.DisplayProgressBar("Snippets", "Looking for snippet objects in " + paths[i], i / (float)paths.Count);
                string text;
                try
                {
                    text = File.ReadAllText(paths[i]);
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    continue;
                }
                yield return (paths[i], text);
            }
        }

        static string Bullets(List<Orphan> orphans, Func<Orphan, string> describe)
        {
            const int Shown = 12;
            var lines = orphans.Take(Shown).Select(orphan => "  • " + describe(orphan)).ToList();
            if (orphans.Count > Shown)
                lines.Add($"  ...and {orphans.Count - Shown} more");
            return string.Join("\n", lines);
        }
    }
}
