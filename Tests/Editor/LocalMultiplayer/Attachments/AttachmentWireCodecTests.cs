using System.Globalization;
using BS.LocalMultiplayer.Attachments;
using NUnit.Framework;
using UnityEngine;

namespace BS.LocalMultiplayer.Tests
{
    /// <summary>
    /// The "attachment_&lt;Id&gt;" user-state value, verbatim from Greenfield's AttachmentNetworkBridge (:331-369):
    /// v1|attachmentType|avatarAttachmentType|avatarAttachmentPoint|physicsAttachmentPoint|jointAvatar|px|py|pz|rx|ry|rz|rw.
    /// </summary>
    public class AttachmentWireCodecTests
    {
        // A NonPhysics hat on the left hand, 10 cm out, 20 cm down and 30 cm forward.
        const string Golden = "v1|1|0|8|0|1|0.1|-0.2|0.3|0|0|0|1";
        const float Tolerance = 1e-5f;

        static BSAttachment LeftHandHat() => new BSAttachment
        {
            attachmentType = AttachmentType.NonPhysics,
            avatarAttachmentType = AvatarAttachmentType.AttachToAvatar,
            avatarAttachmentPoint = AvatarBoneName.LEFTARM_HAND,
            physicsAttachmentPoint = PhysicsAttachmentPoint.Head,
            jointAvatar = true,
            attachmentPosition = new Vector3(0.1f, -0.2f, 0.3f),
            attachmentRotation = Quaternion.identity,
        };

        static void AssertSame(BSAttachment expected, BSAttachment actual)
        {
            Assert.AreEqual(expected.attachmentType, actual.attachmentType);
            Assert.AreEqual(expected.avatarAttachmentType, actual.avatarAttachmentType);
            Assert.AreEqual(expected.avatarAttachmentPoint, actual.avatarAttachmentPoint);
            Assert.AreEqual(expected.physicsAttachmentPoint, actual.physicsAttachmentPoint);
            Assert.AreEqual(expected.jointAvatar, actual.jointAvatar);
            Assert.AreEqual(expected.attachmentPosition.x, actual.attachmentPosition.x, Tolerance);
            Assert.AreEqual(expected.attachmentPosition.y, actual.attachmentPosition.y, Tolerance);
            Assert.AreEqual(expected.attachmentPosition.z, actual.attachmentPosition.z, Tolerance);
            Assert.AreEqual(expected.attachmentRotation.x, actual.attachmentRotation.x, Tolerance);
            Assert.AreEqual(expected.attachmentRotation.y, actual.attachmentRotation.y, Tolerance);
            Assert.AreEqual(expected.attachmentRotation.z, actual.attachmentRotation.z, Tolerance);
            Assert.AreEqual(expected.attachmentRotation.w, actual.attachmentRotation.w, Tolerance);
        }

        [Test]
        public void Serialize_MatchesTheGoldenString()
        {
            Assert.AreEqual(Golden, AttachmentWireCodec.Serialize(LeftHandHat()));
        }

        [Test]
        public void Deserialize_TheGoldenString()
        {
            Assert.IsTrue(AttachmentWireCodec.TryDeserialize(Golden, out var data));
            AssertSame(LeftHandHat(), data);
        }

        [Test]
        public void RoundTrip_KeepsEveryField()
        {
            var cases = new[]
            {
                LeftHandHat(),
                new BSAttachment
                {
                    attachmentType = AttachmentType.Physics,
                    avatarAttachmentType = AvatarAttachmentType.AttachToAvatar,
                    avatarAttachmentPoint = AvatarBoneName.RIGHTARM_HAND_THUMB3,
                    physicsAttachmentPoint = PhysicsAttachmentPoint.Torso,
                    jointAvatar = false,
                    attachmentPosition = new Vector3(-1.25f, 1000.5f, 0.0001f),
                    attachmentRotation = new Quaternion(0.1830127f, -0.6830127f, 0.1830127f, 0.6830127f),
                },
                new BSAttachment
                {
                    attachmentType = AttachmentType.Physics,
                    avatarAttachmentType = AvatarAttachmentType.AvatarAttachTo,
                    avatarAttachmentPoint = AvatarBoneName.HIPS,
                    physicsAttachmentPoint = PhysicsAttachmentPoint.RightHand,
                    jointAvatar = true,
                    attachmentPosition = Vector3.zero,
                    attachmentRotation = new Quaternion(0f, 0.70710677f, 0f, 0.70710677f),
                },
            };
            foreach (var original in cases)
            {
                Assert.IsTrue(AttachmentWireCodec.TryDeserialize(AttachmentWireCodec.Serialize(original), out var copy));
                AssertSame(original, copy);
            }
        }

        [Test]
        public void Decode_ForcesAutoSync()
        {
            // Whatever the wearer had, a broadcast attachment was an autoSync one (only those are sent).
            var hat = LeftHandHat();
            hat.autoSync = false;
            Assert.IsTrue(AttachmentWireCodec.TryDeserialize(AttachmentWireCodec.Serialize(hat), out var data));
            Assert.IsTrue(data.autoSync);
        }

        [Test]
        public void Decode_LeavesWhatIsNotSent_AtItsDefaults()
        {
            // isSeat, the unseat flags, autoAttach, the uid and the object are not part of the value.
            Assert.IsTrue(AttachmentWireCodec.TryDeserialize(Golden, out var data));
            Assert.IsFalse(data.isSeat);
            Assert.IsFalse(data.autoAttach);
            Assert.IsTrue(data.unseatOnMove);
            Assert.IsTrue(data.unseatOnJump);
            Assert.IsNull(data.uid);
            Assert.IsNull(data.attachedObject.gameObject);
        }

        [Test]
        public void Codec_IgnoresTheCurrentCulture()
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                // German writes 0,1: the wire must not.
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                Assert.AreEqual(Golden, AttachmentWireCodec.Serialize(LeftHandHat()));
                Assert.IsTrue(AttachmentWireCodec.TryDeserialize(Golden, out var data));
                AssertSame(LeftHandHat(), data);
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        [Test]
        public void JointAvatar_IsOnlyTheDigitOne()
        {
            Assert.IsTrue(AttachmentWireCodec.TryDeserialize("v1|1|0|8|0|true|0.1|-0.2|0.3|0|0|0|1", out var data));
            Assert.IsFalse(data.jointAvatar);
            Assert.IsTrue(AttachmentWireCodec.TryDeserialize("v1|1|0|8|0|0|0.1|-0.2|0.3|0|0|0|1", out data));
            Assert.IsFalse(data.jointAvatar);
        }

        [Test]
        public void ExtraTokens_AreIgnored()
        {
            Assert.IsTrue(AttachmentWireCodec.TryDeserialize(Golden + "|later|fields", out var data));
            AssertSame(LeftHandHat(), data);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("v1|1|0|8|0|1|0.1|-0.2|0.3|0|0|0")]
        [TestCase("v2|1|0|8|0|1|0.1|-0.2|0.3|0|0|0|1")]
        [TestCase("V1|1|0|8|0|1|0.1|-0.2|0.3|0|0|0|1")]
        [TestCase("v1|one|0|8|0|1|0.1|-0.2|0.3|0|0|0|1")]
        [TestCase("v1|1|0|8.5|0|1|0.1|-0.2|0.3|0|0|0|1")]
        [TestCase("v1|1|0|8|0|1|x|-0.2|0.3|0|0|0|1")]
        [TestCase("v1|1|0|8|0|1|0.1|-0.2|0.3|0|0|0|")]
        [TestCase("1|0|8|0|1|0.1|-0.2|0.3|0|0|0|1")]
        public void Malformed_IsRejected(string value)
        {
            Assert.IsFalse(AttachmentWireCodec.TryDeserialize(value, out var data));
            Assert.IsNull(data);
        }

        [Test]
        public void KeyNames_MatchProduction()
        {
            Assert.AreEqual("attachment_", AttachmentWireCodec.KeyPrefix);
            Assert.AreEqual("pilot", AttachmentWireCodec.PilotKey);
        }
    }
}
