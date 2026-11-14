using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using BS;

namespace BS.SDKEditor
{
    // editorForChildClasses so the deprecated Banter-prefixed subclasses get this inspector too.
    [CustomEditor(typeof(BSUIPanel), true)]
    public class BSUIPanelEditor : Editor
    {
        void OnEnable()
        {
            if (target is BSUIPanel)
            {
                var script = (BSUIPanel)target;
                var path = AssetDatabase.GetAssetPath(script);
            }
        }
        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI()
        {
            var script = (BSUIPanel)target;
            Editor editor = Editor.CreateEditor(script);

            return BSComponentInspector.Build(
                editor,
                new string[] { "resolution", "screenSpace", "meshInput", "enableHaptics", "clickHaptic", "enterHaptic", "exitHaptic", "enableSounds", "clickSoundUrl", "enterSoundUrl", "exitSoundUrl" },
                new string[] {  });
        }
    }
}
