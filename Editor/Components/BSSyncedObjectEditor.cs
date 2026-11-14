using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using BS;

namespace BS.SDKEditor
{
    // editorForChildClasses so the deprecated Banter-prefixed subclasses get this inspector too.
    [CustomEditor(typeof(BSSyncedObject), true)]
    public class BSSyncedObjectEditor : Editor
    {
        void OnEnable()
        {
            if (target is BSSyncedObject)
            {
                var script = (BSSyncedObject)target;
                var path = AssetDatabase.GetAssetPath(script);
            }
        }
        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI()
        {
            var script = (BSSyncedObject)target;
            Editor editor = Editor.CreateEditor(script);

            return BSComponentInspector.Build(
                editor,
                new string[] { "syncPosition", "syncRotation", "takeOwnershipOnCollision", "takeOwnershipOnGrab", "kinematicIfNotOwned", "resetAfterSeconds", "takeOwnershipOnProximity" },
                new string[] {  });
        }
    }
}
