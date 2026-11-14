using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using BS;

namespace BS.SDKEditor
{
    // editorForChildClasses so the deprecated Banter-prefixed subclasses get this inspector too.
    [CustomEditor(typeof(BSSphere), true)]
    public class BSSphereEditor : Editor
    {
        void OnEnable()
        {
            if (target is BSSphere)
            {
                var script = (BSSphere)target;
                var path = AssetDatabase.GetAssetPath(script);
            }
        }
        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI()
        {
            var script = (BSSphere)target;
            Editor editor = Editor.CreateEditor(script);

            return BSComponentInspector.Build(
                editor,
                new string[] { "radius", "widthSegments", "heightSegments", "phiStart", "phiLength", "thetaStart", "thetaLength" },
                new string[] {  });
        }
    }
}
