using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using BS;

namespace BS.SDKEditor
{
    // editorForChildClasses so the deprecated Banter-prefixed subclasses get this inspector too.
    [CustomEditor(typeof(BSQuestHome), true)]
    public class BSQuestHomeEditor : Editor
    {
        void OnEnable()
        {
            if (target is BSQuestHome)
            {
                var script = (BSQuestHome)target;
                var path = AssetDatabase.GetAssetPath(script);
            }
        }
        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI()
        {
            var script = (BSQuestHome)target;
            Editor editor = Editor.CreateEditor(script);

            return BSComponentInspector.Build(
                editor,
                new string[] { "url", "addColliders", "climbable" },
                new string[] {  });
        }
    }
}
