using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using BS;

namespace BS.SDKEditor
{
    // editorForChildClasses so the deprecated Banter-prefixed subclasses get this inspector too.
    [CustomEditor(typeof(BSScriptGraph), true)]
    public class BSScriptGraphEditor : Editor
    {
        void OnEnable()
        {
            if (target is BSScriptGraph)
            {
                var script = (BSScriptGraph)target;
                var path = AssetDatabase.GetAssetPath(script);
            }
        }
        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI()
        {
            var script = (BSScriptGraph)target;
            Editor editor = Editor.CreateEditor(script);

            return BSComponentInspector.Build(
                editor,
                new string[] { "machineCount", "graphTitles" },
                new string[] {  });
        }
    }
}
