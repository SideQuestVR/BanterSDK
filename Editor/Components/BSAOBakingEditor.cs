using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using BS;

namespace BS.SDKEditor
{
    // editorForChildClasses so the deprecated Banter-prefixed subclasses get this inspector too.
    [CustomEditor(typeof(BSAOBaking), true)]
    public class BSAOBakingEditor : Editor
    {
        void OnEnable()
        {
            if (target is BSAOBaking)
            {
                var script = (BSAOBaking)target;
                var path = AssetDatabase.GetAssetPath(script);
            }
        }
        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI()
        {
            var script = (BSAOBaking)target;
            Editor editor = Editor.CreateEditor(script);

            return BSComponentInspector.Build(
                editor,
                new string[] { "subdivisionLevel", "sampleCount", "aoIntensity", "aoBias", "aoRadius", "hideSourceObjects", "targetShaderName", "isProcessing", "progress" },
                new string[] {  });
        }
    }
}
