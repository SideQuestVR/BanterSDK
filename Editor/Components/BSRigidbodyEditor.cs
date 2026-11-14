using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using BS;

namespace BS.SDKEditor
{
    // editorForChildClasses so the deprecated Banter-prefixed subclasses get this inspector too.
    [CustomEditor(typeof(BSRigidbody), true)]
    public class BSRigidbodyEditor : Editor
    {
        void OnEnable()
        {
            if (target is BSRigidbody)
            {
                var script = (BSRigidbody)target;
                var path = AssetDatabase.GetAssetPath(script);
            }
        }
        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI()
        {
            var script = (BSRigidbody)target;
            Editor editor = Editor.CreateEditor(script);

            var syncSection = new VisualElement();
            var titleSynced = new Label("SYNC RIGIDBODY TO JS");
            titleSynced.style.fontSize = 14;
            syncSection.Add(titleSynced);

            var containervelocity = new VisualElement();
            containervelocity.AddToClassList("toggle-container");
            var labelvelocity = new Label("velocity");
            labelvelocity.style.unityFontStyleAndWeight = FontStyle.Bold;
            containervelocity.Add(labelvelocity);
            var togglevelocity = new Toggle();
            togglevelocity.AddToClassList("switch");
            togglevelocity.value = script._velocity;
            togglevelocity.RegisterValueChangedCallback(evt =>
            {
                script._velocity = evt.newValue;
                EditorUtility.SetDirty(script);
            });
            containervelocity.Add(togglevelocity);
            syncSection.Add(containervelocity);

            var containerangularVelocity = new VisualElement();
            containerangularVelocity.AddToClassList("toggle-container");
            var labelangularVelocity = new Label("angularVelocity");
            labelangularVelocity.style.unityFontStyleAndWeight = FontStyle.Bold;
            containerangularVelocity.Add(labelangularVelocity);
            var toggleangularVelocity = new Toggle();
            toggleangularVelocity.AddToClassList("switch");
            toggleangularVelocity.value = script._angularVelocity;
            toggleangularVelocity.RegisterValueChangedCallback(evt =>
            {
                script._angularVelocity = evt.newValue;
                EditorUtility.SetDirty(script);
            });
            containerangularVelocity.Add(toggleangularVelocity);
            syncSection.Add(containerangularVelocity);

            return BSComponentInspector.Build(
                editor,
                new string[] { "mass", "drag", "angularDrag", "isKinematic", "useGravity", "centerOfMass", "collisionDetectionMode", "freezePositionX", "freezePositionY", "freezePositionZ", "freezeRotationX", "freezeRotationY", "freezeRotationZ" },
                new string[] { "velocity", "angularVelocity" }, syncSection);
        }
    }
}
