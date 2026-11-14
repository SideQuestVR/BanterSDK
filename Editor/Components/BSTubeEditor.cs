using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using BS;

namespace BS.SDKEditor
{
    // editorForChildClasses so the deprecated Banter-prefixed subclasses get this inspector too.
    [CustomEditor(typeof(BSTube), true)]
    public class BSTubeEditor : Editor
    {
        void OnEnable()
        {
            if (target is BSTube)
            {
                var script = (BSTube)target;
                var path = AssetDatabase.GetAssetPath(script);
            }
        }
        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI()
        {
            var script = (BSTube)target;
            Editor editor = Editor.CreateEditor(script);

            return BSComponentInspector.Build(
                editor,
                new string[] { "curvePoints", "radius", "tubularSegments", "radialSegments" },
                new string[] {  });
        }
    }
}
