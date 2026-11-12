using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using BS;

namespace BS.SDKEditor
{
    // editorForChildClasses so the deprecated Banter-prefixed subclasses get this inspector too.
    [CustomEditor(typeof(BSHingeJoint), true)]
    public class BSHingeJointEditor : Editor
    {
        void OnEnable()
        {
            if (target is BSHingeJoint)
            {
                var script = (BSHingeJoint)target;
                var path = AssetDatabase.GetAssetPath(script);
            }
        }
        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI()
        {
            var script = (BSHingeJoint)target;
            Editor editor = Editor.CreateEditor(script);

            return BSComponentInspector.Build(
                editor,
                new string[] { "anchor", "axis", "connectedAnchor", "autoConfigureConnectedAnchor", "useLimits", "limits", "useMotor", "useSpring", "breakForce", "breakTorque", "enableCollision", "enablePreprocessing", "connectedMassScale", "massScale", "connectedBody" },
                new string[] {  });
        }
    }
}
