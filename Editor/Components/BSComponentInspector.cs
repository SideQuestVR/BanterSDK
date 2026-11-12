using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace BS.SDKEditor
{
    /// <summary>
    /// The shared layout every generated BS component inspector uses: the component's own properties
    /// first, then anything it syncs to JS, then a closed drawer listing what a page script can reach.
    ///
    /// <para>It lives here rather than in each generated file so the layout can change without
    /// regenerating 87 editors; the generated half supplies only the two name lists and the sync
    /// toggles, which are the parts that differ per component.</para>
    /// </summary>
    public static class BSComponentInspector
    {
        const string SeenTooltip =
            "Seen: a page script can read these and set them on this component.";

        const string WatchedTooltip =
            "Watched: Unity also sends these to the page whenever they change, so a page can react " +
            "without asking for them. Switch one on above, or call watchProperties from the page.";

        /// <summary>
        /// Builds the inspector.
        /// </summary>
        /// <param name="defaultEditor">The component's own default inspector, drawn at the top.</param>
        /// <param name="seen">Property names a page can read and set.</param>
        /// <param name="watched">Property names Unity can also push to the page as they change.</param>
        /// <param name="syncSection">The generated per-property sync toggles, or null when there are none.</param>
        public static VisualElement Build(Editor defaultEditor, IReadOnlyList<string> seen, IReadOnlyList<string> watched, VisualElement syncSection = null)
        {
            var root = new VisualElement();
            var style = Resources.Load<StyleSheet>("BanterCustomInspector");
            if (style != null) root.styleSheets.Add(style);

            // The component's own properties, open: this is what people came to edit.
            if (defaultEditor != null)
                root.Add(new IMGUIContainer(() => { defaultEditor.OnInspectorGUI(); }));

            if (syncSection != null) root.Add(syncSection);

            var hasSeen = seen != null && seen.Count > 0;
            var hasWatched = watched != null && watched.Count > 0;
            if (!hasSeen && !hasWatched) return root;

            // Reference, not controls, so it starts closed and sits under the properties.
            var drawer = new Foldout { text = "Properties seen by JS", value = false };
            drawer.style.marginTop = 10;
            if (hasSeen) drawer.Add(Section("Seen", seen, SeenTooltip));
            if (hasWatched) drawer.Add(Section("Watched", watched, WatchedTooltip));
            root.Add(drawer);
            return root;
        }

        static VisualElement Section(string heading, IReadOnlyList<string> names, string tooltip)
        {
            var group = new VisualElement();
            group.style.marginBottom = 8;
            group.tooltip = tooltip;

            var title = new Label(heading);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.tooltip = tooltip;
            group.Add(title);

            var list = new Label(string.Join(", ", names));
            list.style.flexWrap = Wrap.Wrap;
            list.style.whiteSpace = WhiteSpace.Normal;
            list.style.color = Color.gray;
            list.tooltip = tooltip;
            group.Add(list);
            return group;
        }
    }
}
