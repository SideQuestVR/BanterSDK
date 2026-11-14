using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using BS;

namespace BS.SDKEditor
{
    // editorForChildClasses so the deprecated Banter-prefixed subclasses get this inspector too.
    [CustomEditor(typeof(BSConfigurableJoint), true)]
    public class BSConfigurableJointEditor : Editor
    {
        void OnEnable()
        {
            if (target is BSConfigurableJoint)
            {
                var script = (BSConfigurableJoint)target;
                var path = AssetDatabase.GetAssetPath(script);
            }
        }
        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI()
        {
            var script = (BSConfigurableJoint)target;
            Editor editor = Editor.CreateEditor(script);

            return BSComponentInspector.Build(
                editor,
                new string[] { "targetPosition", "autoConfigureConnectedAnchor", "xMotion", "yMotion", "zMotion", "angularXMotion", "angularYMotion", "angularZMotion", "anchor", "axis", "secondaryAxis", "connectedAnchor", "targetRotation", "targetVelocity", "targetAngularVelocity", "enableCollision", "enablePreprocessing", "breakForce", "breakTorque", "connectedMassScale", "massScale", "rotationDriveMode", "configuredInWorldSpace", "swapBodies", "connectedBody", "linearLimit", "lowAngularXLimit", "highAngularXLimit", "angularYLimit", "angularZLimit", "xDrive", "yDrive", "zDrive", "angularXDrive", "angularYZDrive", "slerpDrive" },
                new string[] {  });
        }
    }
}
