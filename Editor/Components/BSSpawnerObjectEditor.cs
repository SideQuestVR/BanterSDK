using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using BS;

namespace BS.SDKEditor
{
    // editorForChildClasses so the deprecated Banter-prefixed subclasses get this inspector too.
    [CustomEditor(typeof(BSSpawnerObject), true)]
    public class BSSpawnerObjectEditor : Editor
    {
        void OnEnable()
        {
            if (target is BSSpawnerObject)
            {
                var script = (BSSpawnerObject)target;
                var path = AssetDatabase.GetAssetPath(script);
            }
        }
        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI()
        {
            var script = (BSSpawnerObject)target;
            Editor editor = Editor.CreateEditor(script);

            return BSComponentInspector.Build(
                editor,
                new string[] { "maxSpawned", "forceKinematic" },
                new string[] {  });
        }
    }
}
