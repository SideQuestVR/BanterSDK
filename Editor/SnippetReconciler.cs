using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

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

        [MenuItem("Altspace/Snippets/Remove Orphaned Snippet Elements")]
        static void RemoveOrphanedElements()
        {
            var claimed = new HashSet<string>(UnityEngine.Object
                .FindObjectsByType<BSSnippet>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(c => c.gameObject.scene.isLoaded && !EditorSceneManager.IsPreviewSceneObject(c))
                .Select(c => c.InstanceId)
                .Where(id => !string.IsNullOrEmpty(id)));
            var orphans = SnippetHtmlSync.All()
                .Where(e => !claimed.Contains((string)e.Attribute(SnippetHtmlSync.InstanceAttribute)))
                .ToList();
            if (orphans.Count == 0)
            {
                EditorUtility.DisplayDialog("Snippets", "No orphaned snippet elements found.", "OK");
                return;
            }
            // Deliberately manual: elements can belong to scenes that are simply not open right
            // now, so an automatic sweep would eat them.
            var names = string.Join("\n", orphans.Select(e => "  • " + ((string)e.Attribute("name") ?? "(unnamed)")));
            if (!EditorUtility.DisplayDialog("Remove orphaned snippets?",
                    $"These <bs-snippet> elements in index.html have no BSSnippet component in any LOADED scene (they may belong to scenes that are not open!):\n\n{names}\n\nRemove them?",
                    "Remove", "Cancel"))
                return;
            foreach (var orphan in orphans)
            {
                SnippetHtmlSync.RemoveElement(orphan);
            }
            SnippetHtmlSync.FlushNow();
        }
    }
}
