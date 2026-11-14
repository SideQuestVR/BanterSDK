using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using BS;

namespace BS.SDKEditor
{
    // editorForChildClasses so the deprecated Banter-prefixed subclasses get this inspector too.
    [CustomEditor(typeof(BSSphereCollider), true)]
    public class BSSphereColliderEditor : Editor
    {
        void OnEnable()
        {
            if (target is BSSphereCollider)
            {
                var script = (BSSphereCollider)target;
                var path = AssetDatabase.GetAssetPath(script);
            }
        }
        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI()
        {
            var script = (BSSphereCollider)target;
            Editor editor = Editor.CreateEditor(script);

            return BSComponentInspector.Build(
                editor,
                new string[] { "isTrigger", "radius" },
                new string[] {  });
        }
    }
}
