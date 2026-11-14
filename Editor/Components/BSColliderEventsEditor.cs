using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using BS;

namespace BS.SDKEditor
{
    // editorForChildClasses so the deprecated Banter-prefixed subclasses get this inspector too.
    [CustomEditor(typeof(BSColliderEvents), true)]
    public class BSColliderEventsEditor : Editor
    {
        void OnEnable()
        {
            if (target is BSColliderEvents)
            {
                var script = (BSColliderEvents)target;
                var path = AssetDatabase.GetAssetPath(script);
            }
        }
        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI()
        {
            var script = (BSColliderEvents)target;
            Editor editor = Editor.CreateEditor(script);

            return BSComponentInspector.Build(
                editor,
                new string[] {  },
                new string[] {  });
        }
    }
}
