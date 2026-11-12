using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using BS;

namespace BS.SDKEditor
{
    // editorForChildClasses so the deprecated Banter-prefixed subclasses get this inspector too.
    [CustomEditor(typeof(BSFixedJoint), true)]
    public class BSFixedJointEditor : Editor
    {
        void OnEnable()
        {
            if (target is BSFixedJoint)
            {
                var script = (BSFixedJoint)target;
                var path = AssetDatabase.GetAssetPath(script);
            }
        }
        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI()
        {
            var script = (BSFixedJoint)target;
            Editor editor = Editor.CreateEditor(script);

            return BSComponentInspector.Build(
                editor,
                new string[] { "anchor", "connectedAnchor", "autoConfigureConnectedAnchor", "breakForce", "breakTorque", "enableCollision", "enablePreprocessing", "connectedMassScale", "massScale", "connectedBody" },
                new string[] {  });
        }
    }
}
