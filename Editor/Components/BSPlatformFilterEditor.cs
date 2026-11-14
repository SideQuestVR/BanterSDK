using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using BS;

namespace BS.SDKEditor
{
    // editorForChildClasses so the deprecated Banter-prefixed subclasses get this inspector too.
    [CustomEditor(typeof(BSPlatformFilter), true)]
    public class BSPlatformFilterEditor : Editor
    {
        void OnEnable()
        {
            if (target is BSPlatformFilter)
            {
                var script = (BSPlatformFilter)target;
                var path = AssetDatabase.GetAssetPath(script);
            }
        }
        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI()
        {
            var script = (BSPlatformFilter)target;
            Editor editor = Editor.CreateEditor(script);

            return BSComponentInspector.Build(
                editor,
                new string[] { "includeOnMobile", "includeOnDesktop", "includeInVR", "includeInNonVR" },
                new string[] {  });
        }
    }
}
