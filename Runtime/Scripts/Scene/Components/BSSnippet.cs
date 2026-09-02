using UnityEngine;

namespace BS
{
    /*
     * Authoring-time component: pairs this GameObject with exactly one <bs-snippet> element in
     * Assets/WebRoot/index.html (inside the "<!-- snippet section -->" markers). Deliberately NOT
     * a BSComponentBase / [WatchComponent] — that would trigger the component codegen (which
     * overwrites Editor/Components/ and appends to the frozen JS wire-ordinal registry) and this
     * component has no JS-visible runtime behaviour: at runtime the injection bundle's
     * <bs-snippet> custom element does all the work from the HTML alone.
     *
     * All of the editor behaviour (fetching the snippet, writing the HTML, drawing gizmos,
     * cleaning up removed components) lives in BS.SDKEditor — see SnippetHtmlSync,
     * SnippetReconciler and BSSnippetEditor.
     */
    public class BSSnippet : MonoBehaviour
    {
        [Tooltip("The snippet slug on altvr.app, e.g. 'video-player'. The snippet is fetched once and cached in index.html.")]
        [SerializeField] internal string slug;

        [Tooltip("Unique id pairing this component instance with one <bs-snippet instance=\"...\"> element in index.html. Managed automatically; duplicating the object regenerates it.")]
        [SerializeField] internal string instanceId;

        [Tooltip("Cached from the last successful fetch; shown when the HTML element can't be read.")]
        [SerializeField] internal string cachedTitle;

        [Tooltip("Cached from the last successful fetch; shown when the HTML element can't be read.")]
        [SerializeField] internal string cachedDescription;

        // No InternalsVisibleTo(BS.SDKEditor) exists in this package, so editor code reads through
        // these and writes through SerializedObject (which ignores accessibility and gives Undo).
        public string Slug => slug;
        public string InstanceId => instanceId;
        public string CachedTitle => cachedTitle;
        public string CachedDescription => cachedDescription;

#if UNITY_EDITOR
        /*
         * OnValidate fires on Ctrl+D duplication, scene load, undo and inspector edits — exactly
         * the moments the component↔element pairing can break. Only an event is raised here:
         * asset-database work inside OnValidate is unsafe, so SnippetReconciler queues the
         * component and does the real work on the next EditorApplication.update tick.
         */
        public static event System.Action<BSSnippet> EditorValidated;
        void OnValidate()
        {
            if (UnityEditor.BuildPipeline.isBuildingPlayer)
                return;
            EditorValidated?.Invoke(this);
        }
#endif
    }
}
