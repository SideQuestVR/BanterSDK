using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using BS;

namespace BS.SDKEditor
{
    // editorForChildClasses so the deprecated Banter-prefixed subclasses get this inspector too.
    [CustomEditor(typeof(BSBrowser), true)]
    public class BSBrowserEditor : Editor
    {
        void OnEnable()
        {
            if (target is BSBrowser)
            {
                var script = (BSBrowser)target;
                var path = AssetDatabase.GetAssetPath(script);
            }
        }
        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI()
        {
            var script = (BSBrowser)target;
            Editor editor = Editor.CreateEditor(script);

            return BSComponentInspector.Build(
                editor,
                new string[] { "url", "mipMaps", "pixelsPerUnit", "pageWidth", "pageHeight", "actions", "rolloffMode", "minDistance", "maxDistance" },
                new string[] {  });
        }
    }
}
