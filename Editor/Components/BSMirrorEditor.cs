using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using BS;

namespace BS.SDKEditor
{
    // editorForChildClasses so the deprecated Banter-prefixed subclasses get this inspector too.
    [CustomEditor(typeof(BSMirror), true)]
    public class BSMirrorEditor : Editor
    {
        void OnEnable()
        {
            if (target is BSMirror)
            {
                var script = (BSMirror)target;
                var path = AssetDatabase.GetAssetPath(script);
            }
        }
        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI()
        {
            var script = (BSMirror)target;
            Editor editor = Editor.CreateEditor(script);

            return BSComponentInspector.Build(
                editor,
                new string[] { "renderTextureSize", "cameraClear", "backgroundColor" },
                new string[] {  });
        }
    }
}
