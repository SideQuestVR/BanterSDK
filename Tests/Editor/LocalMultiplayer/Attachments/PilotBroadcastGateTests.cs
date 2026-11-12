using BS.LocalMultiplayer.Attachments;
using NUnit.Framework;
using UnityEngine;

namespace BS.LocalMultiplayer.Tests
{
    /// <summary>
    /// When the rider's seat-relative hip goes out (AttachmentNetworkBridge.BroadcastLocalPilot): more than 1 cm or
    /// 1 degree of change, at most every 0.1 s, and always once after sitting down.
    /// </summary>
    public class PilotBroadcastGateTests
    {
        static readonly Vector3 Hip = new Vector3(0f, 0.2f, 0f);

        // A turn about the seat's up axis.
        static Quaternion Yaw(float degrees)
        {
            var half = degrees * Mathf.Deg2Rad * 0.5f;
            return new Quaternion(0f, Mathf.Sin(half), 0f, Mathf.Cos(half));
        }

        static PilotBroadcastGate SentAt(float time)
        {
            var gate = new PilotBroadcastGate();
            Assert.IsTrue(gate.ShouldSend(Hip, Quaternion.identity, time));
            gate.MarkSent(Hip, Quaternion.identity, time);
            return gate;
        }

        [Test]
        public void Constants_MatchProduction()
        {
            Assert.AreEqual(0.01f, PilotBroadcastGate.PilotPosEpsilon);
            Assert.AreEqual(1f, PilotBroadcastGate.PilotRotEpsilon);
            Assert.AreEqual(0.1f, PilotBroadcastGate.PilotMinInterval);
        }

        [Test]
        public void FirstPose_AlwaysGoesOut()
        {
            var gate = new PilotBroadcastGate();
            Assert.IsFalse(gate.HasSent);
            Assert.IsTrue(gate.ShouldSend(Vector3.zero, Quaternion.identity, 0f));
        }

        [Test]
        public void StillRider_SendsNothingMore()
        {
            var gate = SentAt(1f);
            Assert.IsFalse(gate.ShouldSend(Hip, Quaternion.identity, 5f));
            Assert.IsFalse(gate.ShouldSend(Hip + new Vector3(0.009f, 0f, 0f), Quaternion.identity, 5f));
            Assert.IsFalse(gate.ShouldSend(Hip, Yaw(0.9f), 5f));
        }

        [Test]
        public void MoreThanACentimetre_GoesOut()
        {
            var gate = SentAt(1f);
            Assert.IsTrue(gate.ShouldSend(Hip + new Vector3(0f, 0f, 0.011f), Quaternion.identity, 1.2f));
        }

        [Test]
        public void MoreThanADegree_GoesOut()
        {
            var gate = SentAt(1f);
            Assert.IsTrue(gate.ShouldSend(Hip, Yaw(1.5f), 1.2f));
        }

        [Test]
        public void AtMostTenTimesASecond()
        {
            var gate = SentAt(1f);
            var moved = Hip + new Vector3(0.05f, 0f, 0f);
            Assert.IsFalse(gate.ShouldSend(moved, Quaternion.identity, 1.05f));
            Assert.IsTrue(gate.ShouldSend(moved, Quaternion.identity, 1.15f));
        }

        [Test]
        public void ChangesAreMeasuredFromTheLastSentPose()
        {
            // Creeping half a centimetre a step never adds up while nothing is sent... until the total passes 1 cm.
            var gate = SentAt(1f);
            Assert.IsFalse(gate.ShouldSend(Hip + new Vector3(0.005f, 0f, 0f), Quaternion.identity, 2f));
            Assert.IsTrue(gate.ShouldSend(Hip + new Vector3(0.0105f, 0f, 0f), Quaternion.identity, 3f));
        }

        [Test]
        public void NewSeat_GoesOutAtOnce()
        {
            var gate = SentAt(1f);
            gate.ForceNextSend();
            Assert.IsFalse(gate.HasSent);
            // Same pose and inside the 0.1 s window: sitting down again still publishes.
            Assert.IsTrue(gate.ShouldSend(Hip, Quaternion.identity, 1.01f));
        }
    }
}
