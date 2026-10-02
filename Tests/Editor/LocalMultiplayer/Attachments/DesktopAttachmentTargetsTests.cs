using System;
using BS.LocalMultiplayer.Attachments;
using NUnit.Framework;

namespace BS.LocalMultiplayer.Tests
{
    /// <summary>
    /// Which part of the local desktop rig an attachment goes to (Greenfield AttachmentsSystem.GetAttachmentTransform):
    /// hand bones, lower arms and fingers to the hands, head and neck to the camera, everything else to the torso.
    /// </summary>
    public class DesktopAttachmentTargetsTests
    {
        static DesktopAnchor NonPhysics(AvatarBoneName bone, PhysicsAttachmentPoint ignored = PhysicsAttachmentPoint.Head) =>
            DesktopAttachmentTargets.Resolve(AttachmentType.NonPhysics, bone, ignored);

        static DesktopAnchor Physics(PhysicsAttachmentPoint point, AvatarBoneName ignored = AvatarBoneName.HEAD) =>
            DesktopAttachmentTargets.Resolve(AttachmentType.Physics, ignored, point);

        [Test]
        public void NonPhysics_EveryBone()
        {
            int left = 0, right = 0, head = 0, torso = 0;
            foreach (AvatarBoneName bone in Enum.GetValues(typeof(AvatarBoneName)))
            {
                var name = bone.ToString();
                var anchor = NonPhysics(bone);
                DesktopAnchor expected;
                if (name.StartsWith("LEFTARM_HAND", StringComparison.Ordinal) || bone == AvatarBoneName.LEFTARM_LOWER)
                {
                    expected = DesktopAnchor.LeftHand;
                    left++;
                }
                else if (name.StartsWith("RIGHTARM_HAND", StringComparison.Ordinal) || bone == AvatarBoneName.RIGHTARM_LOWER)
                {
                    expected = DesktopAnchor.RightHand;
                    right++;
                }
                else if (bone == AvatarBoneName.HEAD || bone == AvatarBoneName.NECK)
                {
                    expected = DesktopAnchor.Head;
                    head++;
                }
                else
                {
                    expected = DesktopAnchor.Torso;
                    torso++;
                }
                Assert.AreEqual(expected, anchor, name);
            }
            // Hand + lower arm + 15 finger bones a side; hips, spine, chest, shoulders, upper arms and 8 leg bones.
            Assert.AreEqual(17, left);
            Assert.AreEqual(17, right);
            Assert.AreEqual(2, head);
            Assert.AreEqual(15, torso);
        }

        [TestCase(AvatarBoneName.HIPS)]
        [TestCase(AvatarBoneName.CHEST)]
        [TestCase(AvatarBoneName.LEFTARM_SHOULDER)]
        [TestCase(AvatarBoneName.LEFTARM_UPPER)]
        [TestCase(AvatarBoneName.RIGHTARM_UPPER)]
        [TestCase(AvatarBoneName.RIGHTLEG_TOES)]
        public void NonPhysics_BodyBones_GoToTheTorso(AvatarBoneName bone)
        {
            Assert.AreEqual(DesktopAnchor.Torso, NonPhysics(bone));
        }

        [Test]
        public void NonPhysics_IgnoresThePhysicsPoint()
        {
            Assert.AreEqual(DesktopAnchor.Torso, NonPhysics(AvatarBoneName.HIPS, PhysicsAttachmentPoint.LeftHand));
            Assert.AreEqual(DesktopAnchor.Head, NonPhysics(AvatarBoneName.NECK, PhysicsAttachmentPoint.Torso));
        }

        [Test]
        public void Physics_GoesByPhysicsPoint()
        {
            Assert.AreEqual(DesktopAnchor.Head, Physics(PhysicsAttachmentPoint.Head));
            Assert.AreEqual(DesktopAnchor.LeftHand, Physics(PhysicsAttachmentPoint.LeftHand));
            Assert.AreEqual(DesktopAnchor.RightHand, Physics(PhysicsAttachmentPoint.RightHand));
            Assert.AreEqual(DesktopAnchor.Torso, Physics(PhysicsAttachmentPoint.Torso));
        }

        [Test]
        public void Physics_IgnoresTheBoneName()
        {
            Assert.AreEqual(DesktopAnchor.Torso, Physics(PhysicsAttachmentPoint.Torso, AvatarBoneName.LEFTARM_HAND));
            Assert.AreEqual(DesktopAnchor.RightHand, Physics(PhysicsAttachmentPoint.RightHand, AvatarBoneName.HEAD));
        }

        [Test]
        public void UnknownAttachmentType_ResolvesLikePhysics()
        {
            // Anything but NonPhysics takes the physics branch.
            Assert.AreEqual(DesktopAnchor.LeftHand,
                DesktopAttachmentTargets.Resolve((AttachmentType)7, AvatarBoneName.HEAD, PhysicsAttachmentPoint.LeftHand));
        }
    }
}
