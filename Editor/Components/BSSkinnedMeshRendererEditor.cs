using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using BS;

namespace BS.SDKEditor
{
    // editorForChildClasses so the deprecated Banter-prefixed subclasses get this inspector too.
    [CustomEditor(typeof(BSSkinnedMeshRenderer), true)]
    public class BSSkinnedMeshRendererEditor : Editor
    {
        void OnEnable()
        {
            if (target is BSSkinnedMeshRenderer)
            {
                var script = (BSSkinnedMeshRenderer)target;
                var path = AssetDatabase.GetAssetPath(script);
            }
        }
        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI()
        {
            var script = (BSSkinnedMeshRenderer)target;
            Editor editor = Editor.CreateEditor(script);

            return BSComponentInspector.Build(
                editor,
                new string[] { "blendShapes", "bones", "rootBoneInstanceId", "updateWhenOffscreen", "skinnedMotionVectors", "quality" },
                new string[] {  });
        }
    }
}
