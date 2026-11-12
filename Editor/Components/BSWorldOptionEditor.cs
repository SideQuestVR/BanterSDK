using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using BS;

namespace BS.SDKEditor
{
    // editorForChildClasses so the deprecated Banter-prefixed subclasses get this inspector too.
    [CustomEditor(typeof(BSWorldOption), true)]
    public class BSWorldOptionEditor : Editor
    {
        void OnEnable()
        {
            if (target is BSWorldOption)
            {
                var script = (BSWorldOption)target;
                var path = AssetDatabase.GetAssetPath(script);
            }
        }
        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI()
        {
            var script = (BSWorldOption)target;
            Editor editor = Editor.CreateEditor(script);

            return BSComponentInspector.Build(
                editor,
                new string[] { "slot", "label", "isToggle", "isOn" },
                new string[] {  });
        }
    }
}
