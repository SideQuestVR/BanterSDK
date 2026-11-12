using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using BS;

namespace BS.SDKEditor
{
    // editorForChildClasses so the deprecated Banter-prefixed subclasses get this inspector too.
    [CustomEditor(typeof(BSMobius3d), true)]
    public class BSMobius3dEditor : Editor
    {
        void OnEnable()
        {
            if (target is BSMobius3d)
            {
                var script = (BSMobius3d)target;
                var path = AssetDatabase.GetAssetPath(script);
            }
        }
        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI()
        {
            var script = (BSMobius3d)target;
            Editor editor = Editor.CreateEditor(script);

            return BSComponentInspector.Build(
                editor,
                new string[] { "stacks", "slices" },
                new string[] {  });
        }
    }
}
