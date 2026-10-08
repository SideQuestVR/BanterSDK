// <mirror source="Assets/Systems/Attachments/AttachmentsSystem.cs" sha256="b076dc451c46ae3750aa426065d983c21944a1b7c7d5099d053c1edf53e878ef" mode="port" />
// DesktopAttachmentTargets is the switch of GetAttachmentTransform (:361-416) with FlexaBody's parts swapped for the
// desktop rig's, lifted out so it is testable.
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
        /// Which body part GetAttachmentTransform picks for a bone: hands, lower arms and fingers to the hands, head and
        /// neck to the camera. Everything else, hips, spine, chest, shoulders, upper arms and legs included, lands on the
        /// torso. When the picked part is missing at runtime (no hand, no main camera) production falls back to the
        /// torso as well.
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

            // Fallback to torso
            return DesktopAnchor.Torso;
        }
    }
}
