// <mirror source="Assets/Systems/Attachments/AvatarBoneMapping.cs" sha256="e6c1212ecbea364cdf6ccc0a1519a5a33843653fc27ad4ab703772b51c4c5cf9" mode="verbatim" />
// Verbatim apart from the namespace and the HAS_CREATOR_SDK guard (this assembly always has the SDK). The orb rig
// names its bones after HumanBodyBones, so this table resolves every AvatarBoneName / PhysicsAttachmentPoint on a
// remote orb exactly as production resolves it on a remote humanoid avatar.
using System.Collections.Generic;
using BS;
using UnityEngine;

namespace BS.LocalMultiplayer.Attachments
{
    /// <summary>
    /// Maps the SDK's <see cref="AvatarBoneName"/> (used by BSAttachedObject's avatarAttachmentPoint) to
    /// Unity's <see cref="HumanBodyBones"/>, so a networked attachment can be reproduced on a remote
    /// player's loaded humanoid avatar via <c>Animator.GetBoneTransform</c>. Both are humanoid enums, so
    /// this is a straight one-to-one mapping.
    /// </summary>
    internal static class AvatarBoneMapping
    {
        private static readonly Dictionary<AvatarBoneName, HumanBodyBones> Map = new()
        {
            { AvatarBoneName.HEAD, HumanBodyBones.Head },
            { AvatarBoneName.NECK, HumanBodyBones.Neck },
            { AvatarBoneName.HIPS, HumanBodyBones.Hips },
            { AvatarBoneName.SPINE, HumanBodyBones.Spine },
            { AvatarBoneName.CHEST, HumanBodyBones.Chest },

            { AvatarBoneName.LEFTARM_SHOULDER, HumanBodyBones.LeftShoulder },
            { AvatarBoneName.LEFTARM_UPPER, HumanBodyBones.LeftUpperArm },
            { AvatarBoneName.LEFTARM_LOWER, HumanBodyBones.LeftLowerArm },
            { AvatarBoneName.LEFTARM_HAND, HumanBodyBones.LeftHand },

            { AvatarBoneName.RIGHTARM_SHOULDER, HumanBodyBones.RightShoulder },
            { AvatarBoneName.RIGHTARM_UPPER, HumanBodyBones.RightUpperArm },
            { AvatarBoneName.RIGHTARM_LOWER, HumanBodyBones.RightLowerArm },
            { AvatarBoneName.RIGHTARM_HAND, HumanBodyBones.RightHand },

            { AvatarBoneName.LEFTLEG_UPPER, HumanBodyBones.LeftUpperLeg },
            { AvatarBoneName.LEFTLEG_LOWER, HumanBodyBones.LeftLowerLeg },
            { AvatarBoneName.LEFTLEG_FOOT, HumanBodyBones.LeftFoot },
            { AvatarBoneName.LEFTLEG_TOES, HumanBodyBones.LeftToes },

            { AvatarBoneName.RIGHTLEG_UPPER, HumanBodyBones.RightUpperLeg },
            { AvatarBoneName.RIGHTLEG_LOWER, HumanBodyBones.RightLowerLeg },
            { AvatarBoneName.RIGHTLEG_FOOT, HumanBodyBones.RightFoot },
            { AvatarBoneName.RIGHTLEG_TOES, HumanBodyBones.RightToes },

            { AvatarBoneName.LEFTARM_HAND_PINKY1, HumanBodyBones.LeftLittleProximal },
            { AvatarBoneName.LEFTARM_HAND_PINKY2, HumanBodyBones.LeftLittleIntermediate },
            { AvatarBoneName.LEFTARM_HAND_PINKY3, HumanBodyBones.LeftLittleDistal },
            { AvatarBoneName.LEFTARM_HAND_RING1, HumanBodyBones.LeftRingProximal },
            { AvatarBoneName.LEFTARM_HAND_RING2, HumanBodyBones.LeftRingIntermediate },
            { AvatarBoneName.LEFTARM_HAND_RING3, HumanBodyBones.LeftRingDistal },
            { AvatarBoneName.LEFTARM_HAND_MIDDLE1, HumanBodyBones.LeftMiddleProximal },
            { AvatarBoneName.LEFTARM_HAND_MIDDLE2, HumanBodyBones.LeftMiddleIntermediate },
            { AvatarBoneName.LEFTARM_HAND_MIDDLE3, HumanBodyBones.LeftMiddleDistal },
            { AvatarBoneName.LEFTARM_HAND_INDEX1, HumanBodyBones.LeftIndexProximal },
            { AvatarBoneName.LEFTARM_HAND_INDEX2, HumanBodyBones.LeftIndexIntermediate },
            { AvatarBoneName.LEFTARM_HAND_INDEX3, HumanBodyBones.LeftIndexDistal },
            { AvatarBoneName.LEFTARM_HAND_THUMB1, HumanBodyBones.LeftThumbProximal },
            { AvatarBoneName.LEFTARM_HAND_THUMB2, HumanBodyBones.LeftThumbIntermediate },
            { AvatarBoneName.LEFTARM_HAND_THUMB3, HumanBodyBones.LeftThumbDistal },

            { AvatarBoneName.RIGHTARM_HAND_PINKY1, HumanBodyBones.RightLittleProximal },
            { AvatarBoneName.RIGHTARM_HAND_PINKY2, HumanBodyBones.RightLittleIntermediate },
            { AvatarBoneName.RIGHTARM_HAND_PINKY3, HumanBodyBones.RightLittleDistal },
            { AvatarBoneName.RIGHTARM_HAND_RING1, HumanBodyBones.RightRingProximal },
            { AvatarBoneName.RIGHTARM_HAND_RING2, HumanBodyBones.RightRingIntermediate },
            { AvatarBoneName.RIGHTARM_HAND_RING3, HumanBodyBones.RightRingDistal },
            { AvatarBoneName.RIGHTARM_HAND_MIDDLE1, HumanBodyBones.RightMiddleProximal },
            { AvatarBoneName.RIGHTARM_HAND_MIDDLE2, HumanBodyBones.RightMiddleIntermediate },
            { AvatarBoneName.RIGHTARM_HAND_MIDDLE3, HumanBodyBones.RightMiddleDistal },
            { AvatarBoneName.RIGHTARM_HAND_INDEX1, HumanBodyBones.RightIndexProximal },
            { AvatarBoneName.RIGHTARM_HAND_INDEX2, HumanBodyBones.RightIndexIntermediate },
            { AvatarBoneName.RIGHTARM_HAND_INDEX3, HumanBodyBones.RightIndexDistal },
            { AvatarBoneName.RIGHTARM_HAND_THUMB1, HumanBodyBones.RightThumbProximal },
            { AvatarBoneName.RIGHTARM_HAND_THUMB2, HumanBodyBones.RightThumbIntermediate },
            { AvatarBoneName.RIGHTARM_HAND_THUMB3, HumanBodyBones.RightThumbDistal },
        };

        public static bool TryGetHumanBone(AvatarBoneName name, out HumanBodyBones bone) => Map.TryGetValue(name, out bone);

        /// <summary>Map a physics attachment point to the closest humanoid bone (remote reproduction is
        /// parent-constraint only, so physics points resolve to their bone).</summary>
        public static HumanBodyBones FromPhysicsPoint(PhysicsAttachmentPoint point) => point switch
        {
            PhysicsAttachmentPoint.Head => HumanBodyBones.Head,
            PhysicsAttachmentPoint.LeftHand => HumanBodyBones.LeftHand,
            PhysicsAttachmentPoint.RightHand => HumanBodyBones.RightHand,
            _ => HumanBodyBones.Hips, // Torso
        };
    }
}
