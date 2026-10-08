// <mirror source="Assets/Systems/Attachments/AttachmentsSystem.cs" sha256="c0818b6e8641387ce32964d2e3416274df58bee0495cf8b80cd37209dcc6b319" mode="port" />
// DesktopAttachmentTargets is the switch of GetAttachmentTransform (:395-473) with FlexaBody's parts swapped for the
// desktop rig's; AttachmentJoint is the joint set-up of HandleAttachment (:302-319), lifted out so both are testable.
using BS;
using UnityEngine;

namespace BS.LocalMultiplayer.Attachments
{
    /// <summary>The local body part an attachment goes to: production's physics hands, camera and FlexaBall torso.</summary>
    internal enum DesktopAnchor
    {
        /// <summary>The rig's torso (FlexaBall): every bone without a hand or head equivalent, and the fallback.</summary>
        Torso,
        /// <summary>The rig's left hand anchor (PhysHands[0]).</summary>
        LeftHand,
        /// <summary>The rig's right hand anchor (PhysHands[1]), which follows the mouse hand while it grabs.</summary>
        RightHand,
        /// <summary>Camera.main: the desktop player's head camera.</summary>
        Head,
    }

    internal static class DesktopAttachmentTargets
    {
        /// <summary>
        /// Which body part GetAttachmentTransform picks. NonPhysics attachments go by bone name (hands, lower arms and
        /// fingers to the hands, head and neck to the camera); Physics attachments by physics point. Everything else,
        /// hips, spine, chest, shoulders, upper arms and legs included, lands on the torso. When the picked part is
        /// missing at runtime (no hand, no main camera) production falls back to the torso as well.
        /// </summary>
        public static DesktopAnchor Resolve(AttachmentType attachmentType, AvatarBoneName avatarAttachmentPoint,
            PhysicsAttachmentPoint physicsAttachmentPoint)
        {
            // For non-physics attachments, use avatarAttachmentPoint (bone name)
            // For physics attachments, use physicsAttachmentPoint
            if (attachmentType == AttachmentType.NonPhysics)
            {
                switch (avatarAttachmentPoint)
                {
                    case AvatarBoneName.LEFTARM_HAND:
                    case AvatarBoneName.LEFTARM_LOWER:
                    case AvatarBoneName.LEFTARM_HAND_INDEX1:
                    case AvatarBoneName.LEFTARM_HAND_INDEX2:
                    case AvatarBoneName.LEFTARM_HAND_INDEX3:
                    case AvatarBoneName.LEFTARM_HAND_MIDDLE1:
                    case AvatarBoneName.LEFTARM_HAND_MIDDLE2:
                    case AvatarBoneName.LEFTARM_HAND_MIDDLE3:
                    case AvatarBoneName.LEFTARM_HAND_RING1:
                    case AvatarBoneName.LEFTARM_HAND_RING2:
                    case AvatarBoneName.LEFTARM_HAND_RING3:
                    case AvatarBoneName.LEFTARM_HAND_PINKY1:
                    case AvatarBoneName.LEFTARM_HAND_PINKY2:
                    case AvatarBoneName.LEFTARM_HAND_PINKY3:
                    case AvatarBoneName.LEFTARM_HAND_THUMB1:
                    case AvatarBoneName.LEFTARM_HAND_THUMB2:
                    case AvatarBoneName.LEFTARM_HAND_THUMB3:
                        return DesktopAnchor.LeftHand;

                    case AvatarBoneName.RIGHTARM_HAND:
                    case AvatarBoneName.RIGHTARM_LOWER:
                    case AvatarBoneName.RIGHTARM_HAND_INDEX1:
                    case AvatarBoneName.RIGHTARM_HAND_INDEX2:
                    case AvatarBoneName.RIGHTARM_HAND_INDEX3:
                    case AvatarBoneName.RIGHTARM_HAND_MIDDLE1:
                    case AvatarBoneName.RIGHTARM_HAND_MIDDLE2:
                    case AvatarBoneName.RIGHTARM_HAND_MIDDLE3:
                    case AvatarBoneName.RIGHTARM_HAND_RING1:
                    case AvatarBoneName.RIGHTARM_HAND_RING2:
                    case AvatarBoneName.RIGHTARM_HAND_RING3:
                    case AvatarBoneName.RIGHTARM_HAND_PINKY1:
                    case AvatarBoneName.RIGHTARM_HAND_PINKY2:
                    case AvatarBoneName.RIGHTARM_HAND_PINKY3:
                    case AvatarBoneName.RIGHTARM_HAND_THUMB1:
                    case AvatarBoneName.RIGHTARM_HAND_THUMB2:
                    case AvatarBoneName.RIGHTARM_HAND_THUMB3:
                        return DesktopAnchor.RightHand;

                    case AvatarBoneName.HEAD:
                    case AvatarBoneName.NECK:
                        return DesktopAnchor.Head;
                }
            }
            else // Physics
            {
                switch (physicsAttachmentPoint)
                {
                    case PhysicsAttachmentPoint.LeftHand:
                        return DesktopAnchor.LeftHand;
                    case PhysicsAttachmentPoint.RightHand:
                        return DesktopAnchor.RightHand;
                    case PhysicsAttachmentPoint.Head:
                        return DesktopAnchor.Head;
                }
            }

            // Fallback to torso
            return DesktopAnchor.Torso;
        }
    }

    /// <summary>The joint a local Physics attachment gets (AttachmentsSystem.HandleAttachment).</summary>
    internal static class AttachmentJoint
    {
        public const float SlerpPositionSpring = 1000000;
        public const float SlerpPositionDamper = 10000;

        /// <summary>
        /// Linear motion locked, rotation free with a stiff slerp drive, and the connected anchor at the body
        /// part's origin. With the joint's own anchor at the object's origin too, the object's origin is pinned to
        /// the body part's: the authored attachment offsets play no part.
        /// </summary>
        public static void Configure(ConfigurableJoint joint)
        {
            joint.xMotion = ConfigurableJointMotion.Locked;
            joint.yMotion = ConfigurableJointMotion.Locked;
            joint.zMotion = ConfigurableJointMotion.Locked;
            joint.angularXMotion = ConfigurableJointMotion.Free;
            joint.angularYMotion = ConfigurableJointMotion.Free;
            joint.angularZMotion = ConfigurableJointMotion.Free;
            joint.enableCollision = false;
            joint.rotationDriveMode = RotationDriveMode.Slerp;
            joint.slerpDrive = new JointDrive()
            {
                positionSpring = SlerpPositionSpring,
                positionDamper = SlerpPositionDamper,
                maximumForce = Mathf.Infinity
            };
            joint.autoConfigureConnectedAnchor = false;
            joint.connectedAnchor = Vector3.zero;
        }
    }
}
