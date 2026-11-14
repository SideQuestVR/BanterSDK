using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using BS;

namespace BS.SDKEditor
{
    // editorForChildClasses so the deprecated Banter-prefixed subclasses get this inspector too.
    [CustomEditor(typeof(BSCapsule), true)]
    public class BSCapsuleEditor : Editor
    {
        void OnEnable()
        {
            if (target is BSCapsule)
            {
                var script = (BSCapsule)target;
                var path = AssetDatabase.GetAssetPath(script);
            }
        }
        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI()
        {
            var script = (BSCapsule)target;
            Editor editor = Editor.CreateEditor(script);

            return BSComponentInspector.Build(
                editor,
                new string[] { "radius", "height", "radialSegments", "heightSegments" },
                new string[] {  });
        }
    }
}
