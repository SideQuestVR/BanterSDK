using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using BS;

namespace BS.SDKEditor
{
    // editorForChildClasses so the deprecated Banter-prefixed subclasses get this inspector too.
    [CustomEditor(typeof(BSBox), true)]
    public class BSBoxEditor : Editor
    {
        void OnEnable()
        {
            if (target is BSBox)
            {
                var script = (BSBox)target;
                var path = AssetDatabase.GetAssetPath(script);
            }
        }
        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI()
        {
            var script = (BSBox)target;
            Editor editor = Editor.CreateEditor(script);

            return BSComponentInspector.Build(
                editor,
                new string[] { "width", "height", "depth", "widthSegments", "heightSegments", "depthSegments" },
                new string[] {  });
        }
    }
}
