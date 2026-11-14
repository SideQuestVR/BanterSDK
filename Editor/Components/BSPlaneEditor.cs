using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using BS;

namespace BS.SDKEditor
{
    // editorForChildClasses so the deprecated Banter-prefixed subclasses get this inspector too.
    [CustomEditor(typeof(BSPlane), true)]
    public class BSPlaneEditor : Editor
    {
        void OnEnable()
        {
            if (target is BSPlane)
            {
                var script = (BSPlane)target;
                var path = AssetDatabase.GetAssetPath(script);
            }
        }
        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI()
        {
            var script = (BSPlane)target;
            Editor editor = Editor.CreateEditor(script);

            return BSComponentInspector.Build(
                editor,
                new string[] { "width", "height", "widthSegments", "heightSegments" },
                new string[] {  });
        }
    }
}
