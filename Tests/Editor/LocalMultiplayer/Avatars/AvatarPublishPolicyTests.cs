using BS.LocalMultiplayer.Avatars;
using NUnit.Framework;
using UnityEngine;

namespace BS.LocalMultiplayer.Tests
{
    /// <summary>
    /// When the local player's pose goes out, as production's participant publisher decides it
    /// (PacketPartyTransformRuntime.Tick with PacketPartySynchronizedTransform's defaults).
    /// </summary>
    public class AvatarPublishPolicyTests
    {
        const float Tolerance = 1e-4f;

        [Test]
        public void Constants_AreProductions()
        {
            Assert.That(PosePublishPolicy.MaximumPublishHz, Is.EqualTo(30f));
            Assert.That(PosePublishPolicy.StationaryHeartbeatSeconds, Is.EqualTo(1f));
            Assert.That(PosePublishPolicy.PositionThresholdMeters, Is.EqualTo(0.001f));
            Assert.That(PosePublishPolicy.RotationThresholdDegrees, Is.EqualTo(0.1f));
            Assert.That(PosePublishPolicy.VelocityThresholdMetersPerSecond, Is.EqualTo(0.01f));
            Assert.That(new PosePublishPolicy().ValidForMs, Is.EqualTo(33));
        }

        [Test]
        public void FirstFrame_GoesOut_AsADiscontinuity()
        {
            var policy = new PosePublishPolicy();
            Assert.That(policy.IsDue(0), Is.True);
            Assert.That(policy.TryCommit(0, Sample(Vector3.zero), false, out var sequence, out var discontinuity), Is.True);
            Assert.That(sequence, Is.EqualTo(0));
            Assert.That(discontinuity, Is.True);

            Assert.That(policy.TryCommit(40, Sample(Vector3.one), false, out sequence, out discontinuity), Is.True);
            Assert.That(sequence, Is.EqualTo(1));
            Assert.That(discontinuity, Is.False);
        }

        [Test]
        public void RateGate_HoldsFramesCloserThanAThirtiethOfASecond()
        {
            var policy = new PosePublishPolicy();
            policy.TryCommit(1000, Sample(Vector3.zero), true, out _, out _);
            Assert.That(policy.IsDue(1033.0), Is.False);
            Assert.That(policy.IsDue(1033.34), Is.True);
        }

        [Test]
        public void StillPoseWithoutTheExtension_WaitsForTheHeartbeat()
        {
            var policy = new PosePublishPolicy();
            var still = Sample(new Vector3(1, 2, 3));
            Assert.That(policy.TryCommit(0, still, false, out _, out _), Is.True);
            Assert.That(policy.TryCommit(40, still, false, out _, out _), Is.False);
            Assert.That(policy.TryCommit(999, still, false, out _, out _), Is.False);
            Assert.That(policy.TryCommit(1000, still, false, out _, out _), Is.True);
        }

        [TestCase(0.0005f, false)]
        [TestCase(0.002f, true)]
        public void PositionThreshold_IsOneMillimetre(float move, bool sends)
        {
            var policy = new PosePublishPolicy();
            policy.TryCommit(0, Sample(Vector3.zero), false, out _, out _);
            Assert.That(policy.TryCommit(40, Sample(new Vector3(move, 0, 0)), false, out _, out _), Is.EqualTo(sends));
        }

        [TestCase(0.05f, false)]
        [TestCase(0.5f, true)]
        public void RotationThreshold_IsATenthOfADegree(float yaw, bool sends)
        {
            var policy = new PosePublishPolicy();
            policy.TryCommit(0, Sample(Vector3.zero), false, out _, out _);
            Assert.That(policy.TryCommit(40, Sample(Vector3.zero, yaw), false, out _, out _), Is.EqualTo(sends));
        }

        [TestCase(0.005f, false)]
        [TestCase(0.02f, true)]
        public void VelocityThreshold_IsOneCentimetreASecond(float speed, bool sends)
        {
            var policy = new PosePublishPolicy();
            policy.TryCommit(0, Sample(Vector3.zero), false, out _, out _);
            Assert.That(policy.TryCommit(40, Sample(Vector3.zero, 0f, new Vector3(0, 0, speed)), false, out _, out _), Is.EqualTo(sends));
        }

        [Test]
        public void Changes_AreMeasuredFromTheLastFrameSent()
        {
            var policy = new PosePublishPolicy();
            policy.TryCommit(0, Sample(Vector3.zero), false, out _, out _);
            // Two sub-threshold steps add up to one that sends.
            Assert.That(policy.TryCommit(40, Sample(new Vector3(0.0006f, 0, 0)), false, out _, out _), Is.False);
            Assert.That(policy.TryCommit(80, Sample(new Vector3(0.0012f, 0, 0)), false, out _, out _), Is.True);
        }

        [Test]
        public void TheExtension_MakesEveryDueFrameGoOut()
        {
            var policy = new PosePublishPolicy();
            var still = Sample(Vector3.zero);
            for (int i = 0; i < 10; i++)
            {
                double now = i * 34.0;
                Assert.That(policy.IsDue(now), Is.True);
                Assert.That(policy.TryCommit(now, still, true, out var sequence, out _), Is.True);
                Assert.That(sequence, Is.EqualTo(i));
            }
        }

        [Test]
        public void Reset_StartsANewRegistration_AndKeepsCountingTheSequence()
        {
            var policy = new PosePublishPolicy();
            policy.TryCommit(0, Sample(Vector3.zero), true, out _, out _);
            policy.TryCommit(40, Sample(Vector3.zero), true, out _, out _);

            policy.Reset();
            // Due at once, sent even when still, flagged, and still newer than what receivers have.
            Assert.That(policy.IsDue(41), Is.True);
            Assert.That(policy.TryCommit(41, Sample(Vector3.zero), false, out var sequence, out var discontinuity), Is.True);
            Assert.That(discontinuity, Is.True);
            Assert.That(sequence, Is.EqualTo(2));
        }

        [Test]
        public void Sequence_WrapsAt16Bits()
        {
            var policy = new PosePublishPolicy();
            ushort last = 0;
            for (int i = 0; i <= ushort.MaxValue + 1; i++)
            {
                policy.TryCommit(i * 34.0, Sample(Vector3.zero), true, out last, out _);
            }
            Assert.That(last, Is.EqualTo(0));
        }

        [Test]
        public void Sampler_MeasuresVelocityBetweenSamples()
        {
            var sampler = new RootPoseSampler();
            var first = sampler.Sample(Vector3.zero, Quaternion.identity, 1000);
            Assert.That(first.Velocity, Is.EqualTo(Vector3.zero));
            Assert.That(first.HasRotation && first.HasVelocity, Is.True);

            var second = sampler.Sample(new Vector3(0.1f, 0, 0), Quaternion.identity, 1050);
            Assert.That(second.Velocity.x, Is.EqualTo(2f).Within(Tolerance));

            sampler.Reset();
            var third = sampler.Sample(new Vector3(5, 0, 0), Quaternion.identity, 1100);
            Assert.That(third.Velocity, Is.EqualTo(Vector3.zero));
        }

        static PoseSample Sample(Vector3 position, float yaw = 0f, Vector3 velocity = default) => new PoseSample
        {
            Position = position,
            Rotation = Quaternion.Euler(0f, yaw, 0f),
            Velocity = velocity,
            HasRotation = true,
            HasVelocity = true
        };
    }
}
