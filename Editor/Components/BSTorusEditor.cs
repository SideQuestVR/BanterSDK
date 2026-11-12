using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using BS;

namespace BS.SDKEditor
{
    // editorForChildClasses so the deprecated Banter-prefixed subclasses get this inspector too.
    [CustomEditor(typeof(BSTorus), true)]
    public class BSTorusEditor : Editor
    {
        void OnEnable()
        {
            if (target is BSTorus)
            {
                var script = (BSTorus)target;
                var path = AssetDatabase.GetAssetPath(script);
            }
        }
        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI()
        {
            var script = (BSTorus)target;
            Editor editor = Editor.CreateEditor(script);

            return BSComponentInspector.Build(
                editor,
                new string[] { "radius", "tube", "radialSegments", "tubularSegments", "arc" },
                new string[] {  });
        }
    }
}
