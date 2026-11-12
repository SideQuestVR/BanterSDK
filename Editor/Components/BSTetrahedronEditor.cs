using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using BS;

namespace BS.SDKEditor
{
    // editorForChildClasses so the deprecated Banter-prefixed subclasses get this inspector too.
    [CustomEditor(typeof(BSTetrahedron), true)]
    public class BSTetrahedronEditor : Editor
    {
        void OnEnable()
        {
            if (target is BSTetrahedron)
            {
                var script = (BSTetrahedron)target;
                var path = AssetDatabase.GetAssetPath(script);
            }
        }
        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI()
        {
            var script = (BSTetrahedron)target;
            Editor editor = Editor.CreateEditor(script);

            return BSComponentInspector.Build(
                editor,
                new string[] { "radius", "detail" },
                new string[] {  });
        }
    }
}
