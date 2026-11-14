using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using BS;

namespace BS.SDKEditor
{
    // editorForChildClasses so the deprecated Banter-prefixed subclasses get this inspector too.
    [CustomEditor(typeof(BSPhysicMaterial), true)]
    public class BSPhysicMaterialEditor : Editor
    {
        void OnEnable()
        {
            if (target is BSPhysicMaterial)
            {
                var script = (BSPhysicMaterial)target;
                var path = AssetDatabase.GetAssetPath(script);
            }
        }
        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI()
        {
            var script = (BSPhysicMaterial)target;
            Editor editor = Editor.CreateEditor(script);

            return BSComponentInspector.Build(
                editor,
                new string[] { "dynamicFriction", "staticFriction" },
                new string[] {  });
        }
    }
}
