using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using BS;

namespace BS.SDKEditor
{
    // editorForChildClasses so the deprecated Banter-prefixed subclasses get this inspector too.
    [CustomEditor(typeof(BSLathe), true)]
    public class BSLatheEditor : Editor
    {
        void OnEnable()
        {
            if (target is BSLathe)
            {
                var script = (BSLathe)target;
                var path = AssetDatabase.GetAssetPath(script);
            }
        }
        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI()
        {
            var script = (BSLathe)target;
            Editor editor = Editor.CreateEditor(script);

            return BSComponentInspector.Build(
                editor,
                new string[] { "shapePoints", "segments", "radialSegments", "phiStart", "phiLength" },
                new string[] {  });
        }
    }
}
