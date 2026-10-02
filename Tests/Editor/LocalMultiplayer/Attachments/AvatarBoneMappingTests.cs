using System;
using System.Collections.Generic;
using BS.LocalMultiplayer.Attachments;
using NUnit.Framework;
using UnityEngine;

namespace BS.LocalMultiplayer.Tests
{
    /// <summary>
    /// Which orb bone a peer's attachment is reproduced on (Greenfield AvatarBoneMapping, copied verbatim): every
    /// AvatarBoneName to its HumanBodyBones namesake, physics points to their bone or the hips.
    /// </summary>
    public class AvatarBoneMappingTests
    {
        static AvatarBoneName[] AllBoneNames() => (AvatarBoneName[])Enum.GetValues(typeof(AvatarBoneName));

        [Test]
        public void EveryBoneName_Maps()
        {
            var names = AllBoneNames();
            Assert.AreEqual(51, names.Length);
            foreach (var name in names)
            {
                Assert.IsTrue(AvatarBoneMapping.TryGetHumanBone(name, out _), name.ToString());
            }
        }

        [Test]
        public void EveryBoneName_MapsToItsOwnBone()
        {
            var seen = new HashSet<HumanBodyBones>();
            foreach (var name in AllBoneNames())
            {
                AvatarBoneMapping.TryGetHumanBone(name, out var bone);
                Assert.IsTrue(seen.Add(bone), $"{name} shares {bone}");
                Assert.AreNotEqual(HumanBodyBones.LastBone, bone);
            }
        }

        [TestCase(AvatarBoneName.HEAD, HumanBodyBones.Head)]
        [TestCase(AvatarBoneName.NECK, HumanBodyBones.Neck)]
        [TestCase(AvatarBoneName.HIPS, HumanBodyBones.Hips)]
        [TestCase(AvatarBoneName.SPINE, HumanBodyBones.Spine)]
        [TestCase(AvatarBoneName.CHEST, HumanBodyBones.Chest)]
        [TestCase(AvatarBoneName.LEFTARM_SHOULDER, HumanBodyBones.LeftShoulder)]
        [TestCase(AvatarBoneName.LEFTARM_LOWER, HumanBodyBones.LeftLowerArm)]
        [TestCase(AvatarBoneName.LEFTARM_HAND, HumanBodyBones.LeftHand)]
        [TestCase(AvatarBoneName.RIGHTARM_UPPER, HumanBodyBones.RightUpperArm)]
        [TestCase(AvatarBoneName.RIGHTARM_HAND, HumanBodyBones.RightHand)]
        [TestCase(AvatarBoneName.LEFTLEG_TOES, HumanBodyBones.LeftToes)]
        [TestCase(AvatarBoneName.RIGHTLEG_FOOT, HumanBodyBones.RightFoot)]
        [TestCase(AvatarBoneName.LEFTARM_HAND_PINKY1, HumanBodyBones.LeftLittleProximal)]
        [TestCase(AvatarBoneName.LEFTARM_HAND_THUMB3, HumanBodyBones.LeftThumbDistal)]
        [TestCase(AvatarBoneName.RIGHTARM_HAND_INDEX2, HumanBodyBones.RightIndexIntermediate)]
        [TestCase(AvatarBoneName.RIGHTARM_HAND_THUMB3, HumanBodyBones.RightThumbDistal)]
        public void BoneName_MapsToItsNamesake(AvatarBoneName name, HumanBodyBones expected)
        {
            Assert.IsTrue(AvatarBoneMapping.TryGetHumanBone(name, out var bone));
            Assert.AreEqual(expected, bone);
        }

        [Test]
        public void UnknownBoneName_DoesNotMap()
        {
            // An unmappable bone drops the instruction (AttachmentNetworkBridge.cs:276).
            Assert.IsFalse(AvatarBoneMapping.TryGetHumanBone((AvatarBoneName)51, out _));
            Assert.IsFalse(AvatarBoneMapping.TryGetHumanBone((AvatarBoneName)(-1), out _));
        }

        [TestCase(PhysicsAttachmentPoint.Head, HumanBodyBones.Head)]
        [TestCase(PhysicsAttachmentPoint.LeftHand, HumanBodyBones.LeftHand)]
        [TestCase(PhysicsAttachmentPoint.RightHand, HumanBodyBones.RightHand)]
        [TestCase(PhysicsAttachmentPoint.Torso, HumanBodyBones.Hips)]
        [TestCase((PhysicsAttachmentPoint)42, HumanBodyBones.Hips)]
        public void PhysicsPoint_MapsToItsBone(PhysicsAttachmentPoint point, HumanBodyBones expected)
        {
            Assert.AreEqual(expected, AvatarBoneMapping.FromPhysicsPoint(point));
        }
    }
}
