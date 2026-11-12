using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using BS;

namespace BS.SDKEditor
{
    // editorForChildClasses so the deprecated Banter-prefixed subclasses get this inspector too.
    [CustomEditor(typeof(BSCone), true)]
    public class BSConeEditor : Editor
    {
        void OnEnable()
        {
            if (target is BSCone)
            {
                var script = (BSCone)target;
                var path = AssetDatabase.GetAssetPath(script);
            }
        }
        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI()
        {
            var script = (BSCone)target;
            Editor editor = Editor.CreateEditor(script);

            return BSComponentInspector.Build(
                editor,
                new string[] { "radius", "height", "radialSegments", "heightSegments", "openEnded", "thetaStart", "thetaLength" },
                new string[] {  });
        }
    }
}
