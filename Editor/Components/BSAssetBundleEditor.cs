using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using BS;

namespace BS.SDKEditor
{
    // editorForChildClasses so the deprecated Banter-prefixed subclasses get this inspector too.
    [CustomEditor(typeof(BSAssetBundle), true)]
    public class BSAssetBundleEditor : Editor
    {
        void OnEnable()
        {
            if (target is BSAssetBundle)
            {
                var script = (BSAssetBundle)target;
                var path = AssetDatabase.GetAssetPath(script);
            }
        }
        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI()
        {
            var script = (BSAssetBundle)target;
            Editor editor = Editor.CreateEditor(script);

            return BSComponentInspector.Build(
                editor,
                new string[] { "windowsUrl", "osxUrl", "linuxUrl", "androidUrl", "iosUrl", "vosUrl", "isScene", "legacyShaderFix" },
                new string[] {  });
        }
    }
}
