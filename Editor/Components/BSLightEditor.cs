using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using BS;

namespace BS.SDKEditor
{
    // editorForChildClasses so the deprecated Banter-prefixed subclasses get this inspector too.
    [CustomEditor(typeof(BSLight), true)]
    public class BSLightEditor : Editor
    {
        void OnEnable()
        {
            if (target is BSLight)
            {
                var script = (BSLight)target;
                var path = AssetDatabase.GetAssetPath(script);
            }
        }
        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI()
        {
            var script = (BSLight)target;
            Editor editor = Editor.CreateEditor(script);

            return BSComponentInspector.Build(
                editor,
                new string[] { "type", "color", "intensity", "range", "spotAngle", "innerSpotAngle", "shadows" },
                new string[] {  });
        }
    }
}
