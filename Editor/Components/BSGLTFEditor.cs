using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using BS;

namespace BS.SDKEditor
{
    // editorForChildClasses so the deprecated Banter-prefixed subclasses get this inspector too.
    [CustomEditor(typeof(BSGLTF), true)]
    public class BSGLTFEditor : Editor
    {
        void OnEnable()
        {
            if (target is BSGLTF)
            {
                var script = (BSGLTF)target;
                var path = AssetDatabase.GetAssetPath(script);
            }
        }
        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI()
        {
            var script = (BSGLTF)target;
            Editor editor = Editor.CreateEditor(script);

            return BSComponentInspector.Build(
                editor,
                new string[] { "url", "generateMipMaps", "addColliders", "nonConvexColliders", "slippery", "climbable", "legacyRotate", "childrenLayer" },
                new string[] {  });
        }
    }
}
