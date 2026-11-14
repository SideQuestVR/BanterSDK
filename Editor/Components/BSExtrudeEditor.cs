using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using BS;

namespace BS.SDKEditor
{
    // editorForChildClasses so the deprecated Banter-prefixed subclasses get this inspector too.
    [CustomEditor(typeof(BSExtrude), true)]
    public class BSExtrudeEditor : Editor
    {
        void OnEnable()
        {
            if (target is BSExtrude)
            {
                var script = (BSExtrude)target;
                var path = AssetDatabase.GetAssetPath(script);
            }
        }
        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI()
        {
            var script = (BSExtrude)target;
            Editor editor = Editor.CreateEditor(script);

            return BSComponentInspector.Build(
                editor,
                new string[] { "shapePoints", "curvePoints", "depth", "depthSegments", "segments" },
                new string[] {  });
        }
    }
}
