using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using BS;

namespace BS.SDKEditor
{
    // editorForChildClasses so the deprecated Banter-prefixed subclasses get this inspector too.
    [CustomEditor(typeof(BSAvatarMirror), true)]
    public class BSAvatarMirrorEditor : Editor
    {
        void OnEnable()
        {
            if (target is BSAvatarMirror)
            {
                var script = (BSAvatarMirror)target;
                var path = AssetDatabase.GetAssetPath(script);
            }
        }
        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI()
        {
            var script = (BSAvatarMirror)target;
            Editor editor = Editor.CreateEditor(script);

            return BSComponentInspector.Build(
                editor,
                new string[] { "shared", "heightOffset", "loadDistance" },
                new string[] {  });
        }
    }
}
