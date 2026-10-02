// <mirror source="Packages/com.sidequest.packetparty/Tests/Editor/TransformRuntimeTests.cs" sha256="0120f871eada3ad4d5fdfad5e3bff4e05ddbaf66817fc5771bc0f89bc48f365f" mode="port" />
// StructuralBaselineDoesNotConsumeLiveSequenceZero (:879-887) and PlaybackExtrapolationAdaptsToAThinnedDeliveryInterval
// (:910-924) are production's, on the ported buffer; the rest pin the port's other paths.
using BS.LocalMultiplayer.Avatars;
using NUnit.Framework;
using UnityEngine;

namespace BS.LocalMultiplayer.Tests
{
    /// <summary>The buffer a remote player's root plays back from (PacketParty's TransformPlaybackBuffer).</summary>
    public class AvatarPlaybackBufferTests
    {
        const float Tolerance = 1e-4f;

        [Test]
        public void StructuralBaselineDoesNotConsumeLiveSequenceZero()
        {
            var playback = new PosePlaybackBuffer(4);
            playback.Seed(Frame(0, 0, 1, true));
            Assert.That(playback.Push(Frame(0, 500, 2, true)), Is.True);
            Assert.That(playback.TrySample(500, 100, out var sample), Is.True);
            Assert.That(sample.Position.x, Is.EqualTo(2));
        }

        [Test]
        public void PlaybackExtrapolationAdaptsToAThinnedDeliveryInterval()
        {
            var playback = new PosePlaybackBuffer(4);
            var first = Frame(0, 0, 0, false);
            first.ValidForMs = 1000;
            first.Sample = Full(Vector3.zero, Quaternion.identity, new Vector3(2, 0, 0));
            var second = Frame(1, 1000, 2, false);
            second.ValidForMs = 1000;
            second.Sample = Full(new Vector3(2, 0, 0), Quaternion.identity, new Vector3(2, 0, 0));
            Assert.That(playback.Push(first, 0), Is.True);
            Assert.That(playback.Push(second, 1000), Is.True);
            Assert.That(playback.TrySample(1750, 150, out var sample), Is.True);
            Assert.That(sample.Position.x, Is.EqualTo(3.5f).Within(.001f));
        }

        [Test]
        public void Empty_HasNoSample()
        {
            Assert.That(new PosePlaybackBuffer().TrySample(0, 100, out _), Is.False);
        }

        [Test]
        public void BeforeTheOldestFrame_PlaysTheOldestAsIs()
        {
            var playback = new PosePlaybackBuffer();
            playback.Push(Frame(0, 1000, 3, false));
            playback.Push(Frame(1, 1033, 4, false));
            Assert.That(playback.TrySample(900, 100, out var sample), Is.True);
            Assert.That(sample.Position.x, Is.EqualTo(3));
        }

        [Test]
        public void BetweenTwoFrames_LerpsPosition_AndSlerpsRotation()
        {
            var playback = new PosePlaybackBuffer();
            var first = Frame(0, 0, 0, false);
            first.Sample = Full(Vector3.zero, Quaternion.identity, Vector3.zero);
            var second = Frame(1, 100, 10, false);
            second.Sample = Full(new Vector3(10, 0, 0), Quaternion.Euler(0, 90, 0), Vector3.zero);
            playback.Push(first);
            playback.Push(second);

            Assert.That(playback.TrySample(25, 100, out var sample), Is.True);
            Assert.That(sample.Position.x, Is.EqualTo(2.5f).Within(Tolerance));
            Assert.That(Quaternion.Angle(sample.Rotation, Quaternion.Euler(0, 22.5f, 0)), Is.LessThan(0.01f));
        }

        [Test]
        public void PastTheNewest_ExtrapolatesAlongVelocity_ForAtMostTheCap()
        {
            var playback = new PosePlaybackBuffer();
            var velocity = new Vector3(1, 0, 0);
            var first = Frame(0, 0, 0, false);
            first.Sample = Full(Vector3.zero, Quaternion.identity, velocity);
            var second = Frame(1, 33, 0.033f, false);
            second.Sample = Full(new Vector3(0.033f, 0, 0), Quaternion.identity, velocity);
            playback.Push(first);
            playback.Push(second);

            // 50 ms past the newest frame: under the cap.
            Assert.That(playback.TrySample(83, 100, out var near), Is.True);
            Assert.That(near.Position.x, Is.EqualTo(0.083f).Within(Tolerance));
            // A second past it: capped at max(100, 1.25 x max(33, 33)) = 100 ms.
            Assert.That(playback.TrySample(1033, 100, out var far), Is.True);
            Assert.That(far.Position.x, Is.EqualTo(0.133f).Within(Tolerance));
        }

        [Test]
        public void PastTheNewest_WithoutVelocity_Holds()
        {
            var playback = new PosePlaybackBuffer();
            playback.Push(Frame(0, 0, 5, false));
            Assert.That(playback.TrySample(5000, 100, out var sample), Is.True);
            Assert.That(sample.Position.x, Is.EqualTo(5));
        }

        [Test]
        public void OlderOrRepeatedSequence_IsRejected()
        {
            var playback = new PosePlaybackBuffer();
            Assert.That(playback.Push(Frame(10, 0, 1, false)), Is.True);
            Assert.That(playback.Push(Frame(10, 33, 2, false)), Is.False);
            Assert.That(playback.Push(Frame(9, 66, 3, false)), Is.False);
            Assert.That(playback.Count, Is.EqualTo(1));
        }

        [Test]
        public void SequenceWrapsAt16Bits()
        {
            var playback = new PosePlaybackBuffer();
            Assert.That(playback.Push(Frame(65535, 0, 1, false)), Is.True);
            Assert.That(playback.Push(Frame(0, 33, 2, false)), Is.True);
            Assert.That(playback.Count, Is.EqualTo(2));
        }

        [Test]
        public void Discontinuity_StartsTheBufferOver()
        {
            var playback = new PosePlaybackBuffer();
            playback.Push(Frame(0, 0, 1, false));
            playback.Push(Frame(1, 33, 2, false));
            Assert.That(playback.Push(Frame(2, 66, 50, true)), Is.True);
            Assert.That(playback.Count, Is.EqualTo(1));
            Assert.That(playback.TrySample(0, 100, out var sample), Is.True);
            Assert.That(sample.Position.x, Is.EqualTo(50));
        }

        [Test]
        public void NewGeneration_StartsOver_WhateverItsSequence()
        {
            var playback = new PosePlaybackBuffer();
            playback.Push(Frame(5000, 0, 1, false, 1));
            Assert.That(playback.Push(Frame(0, 33, 2, false, 2)), Is.True);
            Assert.That(playback.Count, Is.EqualTo(1));
        }

        [Test]
        public void Timestamps_NeverRunBackwards()
        {
            var playback = new PosePlaybackBuffer();
            playback.Push(Frame(0, 100, 1, false));
            playback.Push(Frame(1, 90, 2, false));
            // The late-stamped frame is timed at 100 too, so it is the newest from then on.
            Assert.That(playback.TrySample(95, 100, out var before), Is.True);
            Assert.That(before.Position.x, Is.EqualTo(1));
            Assert.That(playback.TrySample(150, 100, out var after), Is.True);
            Assert.That(after.Position.x, Is.EqualTo(2));
        }

        [Test]
        public void Overflow_DropsTheOldest()
        {
            var playback = new PosePlaybackBuffer(2);
            playback.Push(Frame(0, 0, 1, false));
            playback.Push(Frame(1, 33, 2, false));
            playback.Push(Frame(2, 66, 3, false));
            Assert.That(playback.Count, Is.EqualTo(2));
            Assert.That(playback.TrySample(0, 100, out var oldest), Is.True);
            Assert.That(oldest.Position.x, Is.EqualTo(2));
        }

        [Test]
        public void IsNewerSequence_IsHalfTheRangeAhead()
        {
            Assert.That(PosePlaybackBuffer.IsNewerSequence(1, 0), Is.True);
            Assert.That(PosePlaybackBuffer.IsNewerSequence(0x7FFF, 0), Is.True);
            Assert.That(PosePlaybackBuffer.IsNewerSequence(0x8000, 0), Is.False);
            Assert.That(PosePlaybackBuffer.IsNewerSequence(0, 0), Is.False);
            Assert.That(PosePlaybackBuffer.IsNewerSequence(3, 65533), Is.True);
        }

        [Test]
        public void SampleIsValid_AsProductionsDecoder()
        {
            Assert.That(PosePlaybackBuffer.SampleIsValid(Full(Vector3.one, Quaternion.identity, Vector3.zero)), Is.True);
            Assert.That(PosePlaybackBuffer.SampleIsValid(Full(new Vector3(float.NaN, 0, 0), Quaternion.identity, Vector3.zero)), Is.False);
            Assert.That(PosePlaybackBuffer.SampleIsValid(Full(Vector3.zero, Quaternion.identity, new Vector3(0, float.PositiveInfinity, 0))), Is.False);
            Assert.That(PosePlaybackBuffer.SampleIsValid(Full(Vector3.zero, new Quaternion(0, 0, 0, 0), Vector3.zero)), Is.False);
            Assert.That(PosePlaybackBuffer.SampleIsValid(Full(Vector3.zero, new Quaternion(0, 0, 0, 0.6f), Vector3.zero)), Is.True);
            Assert.That(PosePlaybackBuffer.SampleIsValid(Full(Vector3.zero, new Quaternion(0, 0, 0, 1.6f), Vector3.zero)), Is.False);
        }

        static PoseFrame Frame(ushort sequence, double timestamp, float x, bool discontinuity, uint generation = 1)
        {
            return new PoseFrame
            {
                Generation = generation,
                Sequence = sequence,
                CaptureTimestampMs = timestamp,
                ValidForMs = 33,
                Sample = new PoseSample { Position = new Vector3(x, 0, 0), Rotation = Quaternion.identity },
                Discontinuity = discontinuity
            };
        }

        static PoseSample Full(Vector3 position, Quaternion rotation, Vector3 velocity) => new PoseSample
        {
            Position = position,
            Rotation = rotation,
            Velocity = velocity,
            HasRotation = true,
            HasVelocity = true
        };
    }
}
