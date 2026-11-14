using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using BS;

namespace BS.SDKEditor
{
    // editorForChildClasses so the deprecated Banter-prefixed subclasses get this inspector too.
    [CustomEditor(typeof(BSCharacterJoint), true)]
    public class BSCharacterJointEditor : Editor
    {
        void OnEnable()
        {
            if (target is BSCharacterJoint)
            {
                var script = (BSCharacterJoint)target;
                var path = AssetDatabase.GetAssetPath(script);
            }
        }
        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI()
        {
            var script = (BSCharacterJoint)target;
            Editor editor = Editor.CreateEditor(script);

            return BSComponentInspector.Build(
                editor,
                new string[] { "anchor", "axis", "swingAxis", "connectedAnchor", "autoConfigureConnectedAnchor", "enableProjection", "projectionDistance", "projectionAngle", "breakForce", "breakTorque", "enableCollision", "enablePreprocessing", "connectedMassScale", "massScale", "connectedBody" },
                new string[] {  });
        }
    }
}
