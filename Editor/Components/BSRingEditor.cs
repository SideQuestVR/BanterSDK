using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using BS;

namespace BS.SDKEditor
{
    // editorForChildClasses so the deprecated Banter-prefixed subclasses get this inspector too.
    [CustomEditor(typeof(BSRing), true)]
    public class BSRingEditor : Editor
    {
        void OnEnable()
        {
            if (target is BSRing)
            {
                var script = (BSRing)target;
                var path = AssetDatabase.GetAssetPath(script);
            }
        }
        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI()
        {
            var script = (BSRing)target;
            Editor editor = Editor.CreateEditor(script);

            return BSComponentInspector.Build(
                editor,
                new string[] { "innerRadius", "outerRadius", "thetaSegments", "phiSegments", "thetaStart", "thetaLength" },
                new string[] {  });
        }
    }
}
