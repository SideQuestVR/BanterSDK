using BS.LocalMultiplayer.Attachments;
using NUnit.Framework;
using UnityEngine;

namespace BS.LocalMultiplayer.Tests
{
    /// <summary>
    /// The joint a local Physics attachment gets (Greenfield AttachmentsSystem.cs:271-288): linear motion locked to
    /// the body part's origin, rotation free under a stiff slerp drive.
    /// </summary>
    public class AttachmentJointTests
    {
        GameObject _go;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("AttachmentJointTests");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
        }

        ConfigurableJoint Configured()
        {
            var joint = _go.AddComponent<ConfigurableJoint>();
            AttachmentJoint.Configure(joint);
            return joint;
        }

        [Test]
        public void LinearMotion_IsLocked()
        {
            var joint = Configured();
            Assert.AreEqual(ConfigurableJointMotion.Locked, joint.xMotion);
            Assert.AreEqual(ConfigurableJointMotion.Locked, joint.yMotion);
            Assert.AreEqual(ConfigurableJointMotion.Locked, joint.zMotion);
        }

        [Test]
        public void AngularMotion_IsFree()
        {
            var joint = Configured();
            Assert.AreEqual(ConfigurableJointMotion.Free, joint.angularXMotion);
            Assert.AreEqual(ConfigurableJointMotion.Free, joint.angularYMotion);
            Assert.AreEqual(ConfigurableJointMotion.Free, joint.angularZMotion);
        }

        [Test]
        public void Rotation_FollowsAStiffSlerpDrive()
        {
            var joint = Configured();
            Assert.AreEqual(RotationDriveMode.Slerp, joint.rotationDriveMode);
            Assert.AreEqual(1000000f, joint.slerpDrive.positionSpring);
            Assert.AreEqual(10000f, joint.slerpDrive.positionDamper);
            // Production asks for Mathf.Infinity (AttachmentsSystem.cs:285). Unity reads back 3.40282326E+38 instead
            // (one float step under float.MaxValue), so the drive must hold whatever Infinity comes back as.
            Assert.That(joint.slerpDrive.maximumForce, Is.EqualTo(StoredMaximumForce(Mathf.Infinity)));
            Assert.That(joint.slerpDrive.maximumForce, Is.GreaterThan(1e38f), "an unlimited drive force");
        }

        // What a fresh joint reads back after asking for a slerp drive with this force limit (and production's
        // spring and damper).
        static float StoredMaximumForce(float requested)
        {
            var go = new GameObject("AttachmentJointTests.Reference");
            try
            {
                var joint = go.AddComponent<ConfigurableJoint>();
                joint.slerpDrive = new JointDrive
                {
                    positionSpring = AttachmentJoint.SlerpPositionSpring,
                    positionDamper = AttachmentJoint.SlerpPositionDamper,
                    maximumForce = requested
                };
                return joint.slerpDrive.maximumForce;
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Origin_IsPinnedToTheBodyPartsOrigin()
        {
            // With both anchors at the origins, the authored attachment offsets play no part.
            var joint = Configured();
            Assert.IsFalse(joint.autoConfigureConnectedAnchor);
            Assert.AreEqual(Vector3.zero, joint.connectedAnchor);
            Assert.AreEqual(Vector3.zero, joint.anchor);
        }

        [Test]
        public void Collisions_WithTheBodyPart_AreOff()
        {
            Assert.IsFalse(Configured().enableCollision);
        }

        [Test]
        public void Constants_MatchProduction()
        {
            Assert.AreEqual(1000000f, AttachmentJoint.SlerpPositionSpring);
            Assert.AreEqual(10000f, AttachmentJoint.SlerpPositionDamper);
        }
    }
}
