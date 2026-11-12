using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using BS;

namespace BS.SDKEditor
{
    // editorForChildClasses so the deprecated Banter-prefixed subclasses get this inspector too.
    [CustomEditor(typeof(BSText), true)]
    public class BSTextEditor : Editor
    {
        void OnEnable()
        {
            if (target is BSText)
            {
                var script = (BSText)target;
                var path = AssetDatabase.GetAssetPath(script);
            }
        }
        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI()
        {
            var script = (BSText)target;
            Editor editor = Editor.CreateEditor(script);

            return BSComponentInspector.Build(
                editor,
                new string[] { "text", "color", "horizontalAlignment", "verticalAlignment", "fontSize", "richText", "enableWordWrapping", "rectTransformSizeDelta" },
                new string[] {  });
        }
    }
}
