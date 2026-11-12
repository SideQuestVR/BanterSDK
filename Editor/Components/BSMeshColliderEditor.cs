using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using BS;

namespace BS.SDKEditor
{
    // editorForChildClasses so the deprecated Banter-prefixed subclasses get this inspector too.
    [CustomEditor(typeof(BSMeshCollider), true)]
    public class BSMeshColliderEditor : Editor
    {
        void OnEnable()
        {
            if (target is BSMeshCollider)
            {
                var script = (BSMeshCollider)target;
                var path = AssetDatabase.GetAssetPath(script);
            }
        }
        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI()
        {
            var script = (BSMeshCollider)target;
            Editor editor = Editor.CreateEditor(script);

            return BSComponentInspector.Build(
                editor,
                new string[] { "convex", "isTrigger" },
                new string[] {  });
        }
    }
}
