using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using BS;

namespace BS.SDKEditor
{
    // editorForChildClasses so the deprecated Banter-prefixed subclasses get this inspector too.
    [CustomEditor(typeof(BSBillboard), true)]
    public class BSBillboardEditor : Editor
    {
        void OnEnable()
        {
            if (target is BSBillboard)
            {
                var script = (BSBillboard)target;
                var path = AssetDatabase.GetAssetPath(script);
            }
        }
        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI()
        {
            var script = (BSBillboard)target;
            Editor editor = Editor.CreateEditor(script);

            return BSComponentInspector.Build(
                editor,
                new string[] { "smoothing", "enableXAxis", "enableYAxis", "enableZAxis" },
                new string[] {  });
        }
    }
}
