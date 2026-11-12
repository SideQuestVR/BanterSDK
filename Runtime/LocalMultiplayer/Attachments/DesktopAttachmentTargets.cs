// <mirror source="Assets/Systems/Attachments/AttachmentsSystem.cs" sha256="548b5a4e1e011610ce98efbc5f65d6ca37eae658ee97ded74d47f69bf9e773d2" mode="port" />
// DesktopAttachmentTargets is the switch of GetAttachmentTransform (:411-470) with FlexaBody's parts swapped for the
// desktop rig's, lifted out so it is testable.
using BS;
using UnityEngine;

namespace BS.LocalMultiplayer.Attachments
{
    /// <summary>The local body part an attachment goes to: production's physics hands, camera, or the body.</summary>
    internal enum DesktopAnchor
    {
        /// <summary>
        /// The body: every bone without a hand or head equivalent, and the fallback. Body-driven: the player's own orb's
        /// bone for the point (production: the local avatar's humanoid bone), else the rig's torso.
        /// </summary>
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
        /// Which body part GetAttachmentTransform picks for a bone: hands, lower arms and fingers to the hands, head and
        /// neck to the camera. Everything else, hips, spine, chest, shoulders, upper arms and legs included, is
        /// body-driven (<see cref="DesktopAnchor.Torso"/>). When the picked part is missing at runtime (no hand, no main
        /// camera) production makes the attachment body-driven as well.
        /// </summary>
        public static DesktopAnchor Resolve(AvatarBoneName avatarAttachmentPoint)
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

            // Body-driven
            return DesktopAnchor.Torso;
        }
    }
}
