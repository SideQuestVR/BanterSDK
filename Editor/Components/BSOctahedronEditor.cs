using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using BS;

namespace BS.SDKEditor
{
    // editorForChildClasses so the deprecated Banter-prefixed subclasses get this inspector too.
    [CustomEditor(typeof(BSOctahedron), true)]
    public class BSOctahedronEditor : Editor
    {
        void OnEnable()
        {
            if (target is BSOctahedron)
            {
                var script = (BSOctahedron)target;
                var path = AssetDatabase.GetAssetPath(script);
            }
        }
        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI()
        {
            var script = (BSOctahedron)target;
            Editor editor = Editor.CreateEditor(script);

            return BSComponentInspector.Build(
                editor,
                new string[] { "radius", "detail" },
                new string[] {  });
        }
    }
}
