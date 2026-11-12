using System.Globalization;
using BS.LocalMultiplayer.Attachments;
using NUnit.Framework;
using UnityEngine;

namespace BS.LocalMultiplayer.Tests
{
    /// <summary>
    /// The "pilot" user-state value, verbatim from Greenfield's AttachmentNetworkBridge (:304-329):
    /// seatObjId|hx|hy|hz|qx|qy|qz|qw, the rider's hip relative to the seat.
    /// </summary>
    public class PilotWireCodecTests
    {
        const string SeatId = "kQx3-Seat_1";
        const float Tolerance = 1e-5f;

        static void AssertPose(PendingPilot pilot, Vector3 position, Quaternion rotation)
        {
            Assert.AreEqual(position.x, pilot.HipLocalPos.x, Tolerance);
            Assert.AreEqual(position.y, pilot.HipLocalPos.y, Tolerance);
            Assert.AreEqual(position.z, pilot.HipLocalPos.z, Tolerance);
            Assert.AreEqual(rotation.x, pilot.HipLocalRot.x, Tolerance);
            Assert.AreEqual(rotation.y, pilot.HipLocalRot.y, Tolerance);
            Assert.AreEqual(rotation.z, pilot.HipLocalRot.z, Tolerance);
            Assert.AreEqual(rotation.w, pilot.HipLocalRot.w, Tolerance);
        }

        [Test]
        public void Serialize_HipOnALevelSeat()
        {
            // The seated hip sits 0.2 m above the seat point (FlexaMover's seat hip offset).
            Assert.AreEqual(SeatId + "|0|0.2|0|0|0|0|1",
                AttachmentWireCodec.SerializePilot(SeatId, new Vector3(0f, 0.2f, 0f), Quaternion.identity));
        }

        [Test]
        public void RoundTrip_KeepsSeatAndPose()
        {
            var position = new Vector3(0.05f, 0.21f, -0.3f);
            var rotation = new Quaternion(0.0160335f, 0.3214395f, -0.0054608f, 0.9467927f);
            Assert.IsTrue(AttachmentWireCodec.TryParsePilot(AttachmentWireCodec.SerializePilot(SeatId, position, rotation), out var pilot));
            Assert.AreEqual(SeatId, pilot.SeatObjId);
            AssertPose(pilot, position, rotation);
        }

        [Test]
        public void SeatIdAlone_GluesToTheSeatOrigin()
        {
            Assert.IsTrue(AttachmentWireCodec.TryParsePilot(SeatId, out var pilot));
            Assert.AreEqual(SeatId, pilot.SeatObjId);
            AssertPose(pilot, Vector3.zero, Quaternion.identity);
        }

        [Test]
        public void FewerThanEightTokens_IsTheSeatAlone()
        {
            Assert.IsTrue(AttachmentWireCodec.TryParsePilot(SeatId + "|1|2|3", out var pilot));
            Assert.AreEqual(SeatId, pilot.SeatObjId);
            AssertPose(pilot, Vector3.zero, Quaternion.identity);
        }

        [Test]
        public void UnparseableFloats_KeepTheSeat_WithNoOffset()
        {
            Assert.IsTrue(AttachmentWireCodec.TryParsePilot(SeatId + "|a|b|c|d|e|f|g", out var pilot));
            Assert.AreEqual(SeatId, pilot.SeatObjId);
            AssertPose(pilot, Vector3.zero, Quaternion.identity);
        }

        [Test]
        public void UnparseableRotation_KeepsThePosition()
        {
            // The position is assigned before the rotation is parsed.
            Assert.IsTrue(AttachmentWireCodec.TryParsePilot(SeatId + "|1|2|3|x|0|0|1", out var pilot));
            AssertPose(pilot, new Vector3(1f, 2f, 3f), Quaternion.identity);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("|0|0.2|0|0|0|0|1")]
        [TestCase("|")]
        public void NoSeat_IsRejected(string value)
        {
            Assert.IsFalse(AttachmentWireCodec.TryParsePilot(value, out _));
        }

        [Test]
        public void Codec_IgnoresTheCurrentCulture()
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                var text = AttachmentWireCodec.SerializePilot(SeatId, new Vector3(0f, 0.2f, 0f), Quaternion.identity);
                Assert.AreEqual(SeatId + "|0|0.2|0|0|0|0|1", text);
                Assert.IsTrue(AttachmentWireCodec.TryParsePilot(text, out var pilot));
                AssertPose(pilot, new Vector3(0f, 0.2f, 0f), Quaternion.identity);
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }
    }
}
