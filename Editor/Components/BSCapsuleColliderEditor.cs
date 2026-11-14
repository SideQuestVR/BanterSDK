using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using BS;

namespace BS.SDKEditor
{
    // editorForChildClasses so the deprecated Banter-prefixed subclasses get this inspector too.
    [CustomEditor(typeof(BSCapsuleCollider), true)]
    public class BSCapsuleColliderEditor : Editor
    {
        void OnEnable()
        {
            if (target is BSCapsuleCollider)
            {
                var script = (BSCapsuleCollider)target;
                var path = AssetDatabase.GetAssetPath(script);
            }
        }
        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI()
        {
            var script = (BSCapsuleCollider)target;
            Editor editor = Editor.CreateEditor(script);

            return BSComponentInspector.Build(
                editor,
                new string[] { "isTrigger", "radius", "height" },
                new string[] {  });
        }
    }
}
