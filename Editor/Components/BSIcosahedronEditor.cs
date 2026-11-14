using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using BS;

namespace BS.SDKEditor
{
    // editorForChildClasses so the deprecated Banter-prefixed subclasses get this inspector too.
    [CustomEditor(typeof(BSIcosahedron), true)]
    public class BSIcosahedronEditor : Editor
    {
        void OnEnable()
        {
            if (target is BSIcosahedron)
            {
                var script = (BSIcosahedron)target;
                var path = AssetDatabase.GetAssetPath(script);
            }
        }
        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI()
        {
            var script = (BSIcosahedron)target;
            Editor editor = Editor.CreateEditor(script);

            return BSComponentInspector.Build(
                editor,
                new string[] { "radius", "detail" },
                new string[] {  });
        }
    }
}
