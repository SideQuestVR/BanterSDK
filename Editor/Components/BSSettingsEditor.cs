using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using BS;

namespace BS.SDKEditor
{
    // editorForChildClasses so the deprecated Banter-prefixed subclasses get this inspector too.
    [CustomEditor(typeof(BSSettings), true)]
    public class BSSettingsEditor : Editor
    {
        void OnEnable()
        {
            if (target is BSSettings)
            {
                var script = (BSSettings)target;
                var path = AssetDatabase.GetAssetPath(script);
            }
        }
        public override bool UseDefaultMargins() => false;
        public override VisualElement CreateInspectorGUI()
        {
            var script = (BSSettings)target;
            Editor editor = Editor.CreateEditor(script);

            return BSComponentInspector.Build(
                editor,
                new string[] { "enableTeleport", "enableForceGrab", "enableSpiderMan", "enableHandHold", "enableRadar", "enableNametags", "enablePortals", "enableGuests", "refreshRate", "clippingPlane", "physicsMoveSpeed", "physicsMoveAcceleration", "physicsAirControlSpeed", "physicsAirControlAcceleration", "physicsDrag", "physicsFreeFallAngularDrag", "physicsJumpStrength", "physicsHandPositionStrength", "physicsHandRotationStrength", "physicsHandSpringiness", "physicsGrappleRange", "physicsGrappleReelSpeed", "physicsGrappleSpringiness", "physicsGorillaMode", "isSettingsLocked", "isPhysicsSettingsLocked", "enableBirdFlight", "avatarSyncMode", "voiceDistanceMultiplier" },
                new string[] {  });
        }
    }
}
