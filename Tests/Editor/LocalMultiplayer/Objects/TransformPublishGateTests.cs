using NUnit.Framework;
using UnityEngine;

namespace BS.LocalMultiplayer.Tests
{
    using BS.LocalMultiplayer.Objects;

    /// <summary>
    /// When a synced object's owner publishes (PacketPartyTransformRuntime.Tick): at most 30 Hz, only past
    /// 1 mm / 0.1 degree / 0.01 m/s (inclusive thresholds), a 1 s stationary heartbeat, and always first.
    /// </summary>
    public class TransformPublishGateTests
    {
        static SynchronizedTransformSample Pose(Vector3 position, Quaternion rotation, Vector3 velocity)
            => SynchronizedTransformSample.Full(position, rotation, velocity);

        static SynchronizedTransformSample Rest => Pose(Vector3.zero, Quaternion.identity, Vector3.zero);

        // A turn about Y built by hand: Quaternion's Euler constructor is native, this keeps the test runnable anywhere.
        static Quaternion Yaw(double degrees)
        {
            double half = degrees * System.Math.PI / 360.0;
            return new Quaternion(0f, (float)System.Math.Sin(half), 0f, (float)System.Math.Cos(half));
        }

        static TransformPublishGate SentAtZero()
        {
            var gate = new TransformPublishGate();
            Assert.IsTrue(gate.ShouldSend(0, Rest, out _));
            gate.MarkSent(0, Rest);
            return gate;
        }

        [Test]
        public void Defaults_AreProductions()
        {
            var gate = new TransformPublishGate();
            Assert.AreEqual(30f, gate.MaximumPublishHz);
            Assert.AreEqual(1000.0, gate.HeartbeatMs);
            Assert.AreEqual(0.001f * 0.001f, gate.PositionThresholdSquared);
            Assert.AreEqual(0.1f, gate.RotationThresholdDegrees);
            Assert.AreEqual(0.01f * 0.01f, gate.VelocityThresholdSquared);
        }

        [Test]
        public void ValidFor_IsTheRoundedInterval()
        {
            Assert.AreEqual(33, new TransformPublishGate().ValidForMs);
            Assert.AreEqual(1000.0 / 30.0, new TransformPublishGate().MinimumIntervalMs, 1e-9);
        }

        [Test]
        public void FirstSample_IsAlwaysSent_AsAKeyframe()
        {
            var gate = new TransformPublishGate();
            Assert.IsTrue(gate.IsDue(0));
            Assert.IsTrue(gate.ShouldSend(0, Rest, out bool keyframe));
            Assert.IsTrue(keyframe);
            Assert.AreEqual(0, gate.MarkSent(0, Rest));
            Assert.AreEqual(1, gate.Sequence);
        }

        [Test]
        public void RateGate_HoldsFor33Ms()
        {
            var gate = SentAtZero();
            Assert.IsFalse(gate.IsDue(16));
            Assert.IsFalse(gate.IsDue(33.3));
            Assert.IsTrue(gate.IsDue(33.34));
        }

        [Test]
        public void Position_WithinOneMillimetre_IsNotSent()
        {
            var gate = SentAtZero();
            Assert.IsFalse(gate.ShouldSend(100, Pose(new Vector3(0.001f, 0, 0), Quaternion.identity, Vector3.zero), out _));
            Assert.IsFalse(gate.ShouldSend(100, Pose(new Vector3(0, 0, -0.0005f), Quaternion.identity, Vector3.zero), out _));
        }

        [Test]
        public void Position_PastOneMillimetre_IsSent()
        {
            var gate = SentAtZero();
            Assert.IsTrue(gate.ShouldSend(100, Pose(new Vector3(0.0011f, 0, 0), Quaternion.identity, Vector3.zero), out bool keyframe));
            Assert.IsFalse(keyframe);
        }

        [Test]
        public void Rotation_BelowTheThreshold_IsNotSent()
        {
            var gate = SentAtZero();
            Assert.IsFalse(gate.ShouldSend(100, Pose(Vector3.zero, Yaw(0.05), Vector3.zero), out _));
        }

        [Test]
        public void Rotation_OfOneDegree_IsSent()
        {
            // Quaternion.Angle reads 0 below ~0.16 degrees (its dot epsilon), so production's 0.1 degree threshold
            // takes effect from there; a whole degree is clearly past it.
            var gate = SentAtZero();
            Assert.IsTrue(gate.ShouldSend(100, Pose(Vector3.zero, Yaw(1), Vector3.zero), out _));
        }

        [Test]
        public void Velocity_WithinOneCentimetrePerSecond_IsNotSent()
        {
            var gate = SentAtZero();
            Assert.IsFalse(gate.ShouldSend(100, Pose(Vector3.zero, Quaternion.identity, new Vector3(0.01f, 0, 0)), out _));
        }

        [Test]
        public void Velocity_PastOneCentimetrePerSecond_IsSent()
        {
            var gate = SentAtZero();
            Assert.IsTrue(gate.ShouldSend(100, Pose(Vector3.zero, Quaternion.identity, new Vector3(0, 0.02f, 0)), out _));
        }

        [Test]
        public void ThresholdsCompareAgainstTheLastSentSample()
        {
            // Creeping 0.6 mm per tick: unsent ticks don't move the reference, so the second step goes out.
            var gate = SentAtZero();
            var first = Pose(new Vector3(0.0006f, 0, 0), Quaternion.identity, Vector3.zero);
            Assert.IsFalse(gate.ShouldSend(40, first, out _));
            var second = Pose(new Vector3(0.0012f, 0, 0), Quaternion.identity, Vector3.zero);
            Assert.IsTrue(gate.ShouldSend(80, second, out _));
        }

        [Test]
        public void Heartbeat_SendsAStationaryPoseEverySecond()
        {
            var gate = SentAtZero();
            Assert.IsFalse(gate.ShouldSend(999, Rest, out _));
            Assert.IsTrue(gate.ShouldSend(1000, Rest, out bool keyframe));
            Assert.IsTrue(keyframe);
            gate.MarkSent(1000, Rest);
            Assert.IsFalse(gate.ShouldSend(1999.9, Rest, out _));
            Assert.IsTrue(gate.ShouldSend(2000, Rest, out _));
        }

        [Test]
        public void ChangedFields_AreAlwaysSent()
        {
            var gate = SentAtZero();
            Assert.IsTrue(gate.ShouldSend(100, SynchronizedTransformSample.PositionOnly(Vector3.zero), out _));
        }

        [Test]
        public void Sequence_WrapsAtSixteenBits()
        {
            var gate = new TransformPublishGate();
            for (int i = 0; i < ushort.MaxValue; i++) gate.MarkSent(i, Rest);
            Assert.AreEqual(ushort.MaxValue, gate.Sequence);
            Assert.AreEqual(ushort.MaxValue, gate.MarkSent(ushort.MaxValue, Rest));
            Assert.AreEqual(0, gate.Sequence);
        }

        [Test]
        public void Reset_StartsANewStream()
        {
            var gate = SentAtZero();
            gate.MarkSent(40, Rest);
            gate.Reset();
            Assert.AreEqual(0, gate.Sequence);
            Assert.IsFalse(gate.HasSent);
            Assert.IsTrue(gate.IsDue(41));
            Assert.IsTrue(gate.ShouldSend(41, Rest, out bool keyframe));
            Assert.IsTrue(keyframe);
        }

        [Test]
        public void SimulatedSecond_TickedEveryMillisecond_Sends30Frames()
        {
            // A body moving all the time: the gate opens on the first tick at least 33.3 ms after the last send.
            var gate = new TransformPublishGate();
            int sent = 0;
            for (int tick = 0; tick < 1000; tick++)
            {
                if (!gate.IsDue(tick)) continue;
                var sample = Pose(new Vector3(tick * 0.003f, 0, 0), Quaternion.identity, new Vector3(3, 0, 0));
                if (!gate.ShouldSend(tick, sample, out _)) continue;
                gate.MarkSent(tick, sample);
                sent++;
            }
            Assert.AreEqual(30, sent);
        }

        [Test]
        public void SimulatedSecond_At60Fps_SendsNoMoreThan30Frames()
        {
            // At exactly 60 fps the strict "younger than 33.3 ms" test sometimes skips a 2-frame gap through double
            // rounding (production's arithmetic, kept): never more than 30 frames a second.
            var gate = new TransformPublishGate();
            int sent = 0;
            for (int frame = 0; frame < 60; frame++)
            {
                double now = frame * (1000.0 / 60.0);
                if (!gate.IsDue(now)) continue;
                var sample = Pose(new Vector3(frame * 0.05f, 0, 0), Quaternion.identity, new Vector3(3, 0, 0));
                if (!gate.ShouldSend(now, sample, out _)) continue;
                gate.MarkSent(now, sample);
                sent++;
            }
            Assert.LessOrEqual(sent, 30);
            Assert.GreaterOrEqual(sent, 20);
        }
    }
}
