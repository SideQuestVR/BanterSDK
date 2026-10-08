using System;
using BS.LocalMultiplayer.Attachments;
using NUnit.Framework;

namespace BS.LocalMultiplayer.Tests
{
    /// <summary>
    /// Which part of the local desktop rig an attachment goes to (Greenfield AttachmentsSystem.GetAttachmentTransform):
    /// hand bones, lower arms and fingers to the hands, head and neck to the camera, everything else to the torso. Objects
    /// attach to a player without physics, so an old Physics request first becomes a bone (BSAttachment.WithoutPhysicsOnPlayer).
    /// </summary>
    public class DesktopAttachmentTargetsTests
    {
        static DesktopAnchor NonPhysics(AvatarBoneName bone) => DesktopAttachmentTargets.Resolve(bone);

        static BSAttachment OnPlayer(AttachmentType type, AvatarBoneName bone, PhysicsAttachmentPoint point) => new BSAttachment
        {
            avatarAttachmentType = AvatarAttachmentType.AttachToAvatar,
            attachmentType = type,
            avatarAttachmentPoint = bone,
            physicsAttachmentPoint = point,
        };

        static DesktopAnchor Physics(PhysicsAttachmentPoint point, AvatarBoneName bone = AvatarBoneName.HEAD) =>
            DesktopAttachmentTargets.Resolve(OnPlayer(AttachmentType.Physics, bone, point).WithoutPhysicsOnPlayer().avatarAttachmentPoint);

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
        public void OldPhysicsRequest_GoesWhereItsPhysicsPointWas()
        {
            Assert.AreEqual(DesktopAnchor.Head, Physics(PhysicsAttachmentPoint.Head));
            Assert.AreEqual(DesktopAnchor.LeftHand, Physics(PhysicsAttachmentPoint.LeftHand));
            Assert.AreEqual(DesktopAnchor.RightHand, Physics(PhysicsAttachmentPoint.RightHand));
            Assert.AreEqual(DesktopAnchor.Torso, Physics(PhysicsAttachmentPoint.Torso));
        }

        [Test]
        public void OldPhysicsRequest_KeepsABoneThatWasSet()
        {
            // HEAD is avatarAttachmentPoint's default, so only another bone counts as set.
            Assert.AreEqual(DesktopAnchor.LeftHand, Physics(PhysicsAttachmentPoint.Head, AvatarBoneName.LEFTARM_HAND));
            Assert.AreEqual(DesktopAnchor.Torso, Physics(PhysicsAttachmentPoint.RightHand, AvatarBoneName.CHEST));
        }

        [Test]
        public void WithoutPhysicsOnPlayer_OnlyTouchesPhysicsObjectsOnAPlayer()
        {
            var physics = OnPlayer(AttachmentType.Physics, AvatarBoneName.HEAD, PhysicsAttachmentPoint.Torso);
            var converted = physics.WithoutPhysicsOnPlayer();
            Assert.AreNotSame(physics, converted, "the component's own attachment is left as it was");
            Assert.AreEqual(AttachmentType.NonPhysics, converted.attachmentType);
            Assert.AreEqual(AvatarBoneName.HIPS, converted.avatarAttachmentPoint);
            Assert.AreEqual(AttachmentType.Physics, physics.attachmentType);

            var nonPhysics = OnPlayer(AttachmentType.NonPhysics, AvatarBoneName.HEAD, PhysicsAttachmentPoint.LeftHand);
            Assert.AreSame(nonPhysics, nonPhysics.WithoutPhysicsOnPlayer());

            // The player on a seat or vehicle stays a Physics attachment.
            var seat = new BSAttachment { avatarAttachmentType = AvatarAttachmentType.AvatarAttachTo, attachmentType = AttachmentType.Physics };
            Assert.AreSame(seat, seat.WithoutPhysicsOnPlayer());
            Assert.AreEqual(AttachmentType.Physics, seat.attachmentType);
        }
    }
}
