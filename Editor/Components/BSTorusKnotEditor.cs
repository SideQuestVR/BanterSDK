using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using BS;

namespace BS.SDKEditor
{
    // editorForChildClasses so the deprecated Banter-prefixed subclasses get this inspector too.
    [CustomEditor(typeof(BSTorusKnot), true)]
    public class BSTorusKnotEditor : Editor
    {
        void OnEnable()
        {
            if (target is BSTorusKnot)
            {
                var script = (BSTorusKnot)target;
                var path = AssetDatabase.GetAssetPath(script);
            }
        }
        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI()
        {
            var script = (BSTorusKnot)target;
            Editor editor = Editor.CreateEditor(script);

            return BSComponentInspector.Build(
                editor,
                new string[] { "radius", "tube", "radialSegments", "tubularSegments", "p", "q" },
                new string[] {  });
        }
    }
}
