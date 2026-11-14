using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using BS;

namespace BS.SDKEditor
{
    // editorForChildClasses so the deprecated Banter-prefixed subclasses get this inspector too.
    [CustomEditor(typeof(BSHeldEvents), true)]
    public class BSHeldEventsEditor : Editor
    {
        void OnEnable()
        {
            if (target is BSHeldEvents)
            {
                var script = (BSHeldEvents)target;
                var path = AssetDatabase.GetAssetPath(script);
            }
        }
        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI()
        {
            var script = (BSHeldEvents)target;
            Editor editor = Editor.CreateEditor(script);

            return BSComponentInspector.Build(
                editor,
                new string[] { "sensitivity", "fireRate", "auto", "blockLeftPrimary", "blockLeftSecondary", "blockRightPrimary", "blockRightSecondary", "blockLeftThumbstick", "blockLeftThumbstickClick", "blockRightThumbstick", "blockRightThumbstickClick", "blockLeftTrigger", "blockRightTrigger" },
                new string[] {  });
        }
    }
}
