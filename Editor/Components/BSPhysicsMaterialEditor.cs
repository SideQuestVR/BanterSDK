using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using BS;

namespace BS.SDKEditor
{
    // editorForChildClasses so the deprecated Banter-prefixed subclasses get this inspector too.
    [CustomEditor(typeof(BSPhysicsMaterial), true)]
    public class BSPhysicsMaterialEditor : Editor
    {
        void OnEnable()
        {
            if (target is BSPhysicsMaterial)
            {
                var script = (BSPhysicsMaterial)target;
                var path = AssetDatabase.GetAssetPath(script);
            }
        }
        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI()
        {
            var script = (BSPhysicsMaterial)target;
            Editor editor = Editor.CreateEditor(script);

            return BSComponentInspector.Build(
                editor,
                new string[] { "dynamicFriction", "staticFriction", "bounciness", "frictionCombine", "bounceCombine" },
                new string[] {  });
        }
    }
}
