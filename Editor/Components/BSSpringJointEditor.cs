using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using BS;

namespace BS.SDKEditor
{
    // editorForChildClasses so the deprecated Banter-prefixed subclasses get this inspector too.
    [CustomEditor(typeof(BSSpringJoint), true)]
    public class BSSpringJointEditor : Editor
    {
        void OnEnable()
        {
            if (target is BSSpringJoint)
            {
                var script = (BSSpringJoint)target;
                var path = AssetDatabase.GetAssetPath(script);
            }
        }
        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI()
        {
            var script = (BSSpringJoint)target;
            Editor editor = Editor.CreateEditor(script);

            return BSComponentInspector.Build(
                editor,
                new string[] { "anchor", "connectedAnchor", "autoConfigureConnectedAnchor", "spring", "damper", "minDistance", "maxDistance", "tolerance", "breakForce", "breakTorque", "enableCollision", "enablePreprocessing", "connectedMassScale", "massScale", "connectedBody" },
                new string[] {  });
        }
    }
}
