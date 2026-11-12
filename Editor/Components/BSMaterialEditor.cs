using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using BS;

namespace BS.SDKEditor
{
    // editorForChildClasses so the deprecated Banter-prefixed subclasses get this inspector too.
    [CustomEditor(typeof(BSMaterial), true)]
    public class BSMaterialEditor : Editor
    {
        void OnEnable()
        {
            if (target is BSMaterial)
            {
                var script = (BSMaterial)target;
                var path = AssetDatabase.GetAssetPath(script);
            }
        }
        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI()
        {
            var script = (BSMaterial)target;
            Editor editor = Editor.CreateEditor(script);

            return BSComponentInspector.Build(
                editor,
                new string[] { "shaderName", "texture", "color", "side", "generateMipMaps", "cacheBust", "normalMap", "roughnessMap", "aoMap", "textureScale", "normalStrength" },
                new string[] {  });
        }
    }
}
