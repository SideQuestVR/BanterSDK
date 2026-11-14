using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using BS;

namespace BS.SDKEditor
{
    // editorForChildClasses so the deprecated Banter-prefixed subclasses get this inspector too.
    [CustomEditor(typeof(BSInvertedMesh), true)]
    public class BSInvertedMeshEditor : Editor
    {
        void OnEnable()
        {
            if (target is BSInvertedMesh)
            {
                var script = (BSInvertedMesh)target;
                var path = AssetDatabase.GetAssetPath(script);
            }
        }
        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI()
        {
            var script = (BSInvertedMesh)target;
            Editor editor = Editor.CreateEditor(script);

            return BSComponentInspector.Build(
                editor,
                new string[] {  },
                new string[] {  });
        }
    }
}
