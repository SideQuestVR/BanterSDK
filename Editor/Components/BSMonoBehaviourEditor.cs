using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using BS;

namespace BS.SDKEditor
{
    // editorForChildClasses so the deprecated Banter-prefixed subclasses get this inspector too.
    [CustomEditor(typeof(BSMonoBehaviour), true)]
    public class BSMonoBehaviourEditor : Editor
    {
        void OnEnable()
        {
            if (target is BSMonoBehaviour)
            {
                var script = (BSMonoBehaviour)target;
                var path = AssetDatabase.GetAssetPath(script);
            }
        }
        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI()
        {
            var script = (BSMonoBehaviour)target;
            Editor editor = Editor.CreateEditor(script);

            return BSComponentInspector.Build(
                editor,
                new string[] { "fps", "startFunction", "updateFunction", "destroyFunction" },
                new string[] {  });
        }
    }
}
