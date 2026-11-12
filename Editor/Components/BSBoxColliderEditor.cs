using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using BS;

namespace BS.SDKEditor
{
    // editorForChildClasses so the deprecated Banter-prefixed subclasses get this inspector too.
    [CustomEditor(typeof(BSBoxCollider), true)]
    public class BSBoxColliderEditor : Editor
    {
        void OnEnable()
        {
            if (target is BSBoxCollider)
            {
                var script = (BSBoxCollider)target;
                var path = AssetDatabase.GetAssetPath(script);
            }
        }
        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI()
        {
            var script = (BSBoxCollider)target;
            Editor editor = Editor.CreateEditor(script);

            return BSComponentInspector.Build(
                editor,
                new string[] { "isTrigger", "center", "size" },
                new string[] {  });
        }
    }
}
