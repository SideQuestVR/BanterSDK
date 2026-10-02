// <mirror source="Packages/com.sidequest.packetparty/Tests/Editor/TransformRuntimeTests.cs" sha256="0a6b920a556881c8020b20650526f4dd2a004849175908977e0ca464e4672083" mode="port" />
// The playback tests of TransformRuntimeTests.cs (:879-888, :910-945) with the descriptor handle replaced by the
// (generation, epoch) stream identity, plus the rules the local port must keep.
using NUnit.Framework;
using UnityEngine;

namespace BS.LocalMultiplayer.Tests
{
    using BS.LocalMultiplayer.Objects;

    /// <summary>Remote synced-object playback: TransformPlaybackBuffer and TransformPresentationTimeline.</summary>
    public class TransformPlaybackBufferTests
    {
        const float Tolerance = 1e-4f;

        static SynchronizedTransformFrame Frame(
            ushort sequence, double timestamp, float x, bool discontinuity = false,
            uint generation = 1, int epoch = 1, ushort validForMs = 33)
        {
            return new SynchronizedTransformFrame
            {
                Generation = generation,
                Epoch = epoch,
                Sequence = sequence,
                CaptureTimestampMs = timestamp,
                ValidForMs = validForMs,
                Sample = SynchronizedTransformSample.PositionOnly(new Vector3(x, 0, 0)),
                Discontinuity = discontinuity,
                Keyframe = true
            };
        }

        static SynchronizedTransformFrame Moving(ushort sequence, double timestamp, float x, float velocityX, ushort validForMs = 33)
        {
            var frame = Frame(sequence, timestamp, x, validForMs: validForMs);
            frame.Sample = SynchronizedTransformSample.Full(new Vector3(x, 0, 0), Quaternion.identity, new Vector3(velocityX, 0, 0));
            return frame;
        }

        // ─── Copied from production ─────────────────────────────────────────────

        [Test]
        public void StructuralBaselineDoesNotConsumeLiveSequenceZero()
        {
            var playback = new TransformPlaybackBuffer(4);
            playback.Seed(Frame(0, 0, 1, true));
            Assert.That(playback.Push(Frame(0, 500, 2, true)), Is.True);
            Assert.That(playback.TrySample(500, 100, out var sample), Is.True);
            Assert.That(sample.Position.x, Is.EqualTo(2));
        }

        [Test]
        public void PlaybackExtrapolationAdaptsToAThinnedDeliveryInterval()
        {
            var playback = new TransformPlaybackBuffer(4);
            var first = Frame(0, 0, 0, false);
            first.ValidForMs = 1000;
            first.Sample = SynchronizedTransformSample.Full(Vector3.zero, Quaternion.identity, new Vector3(2, 0, 0));
            var second = Frame(1, 1000, 2, false);
            second.ValidForMs = 1000;
            second.Sample = SynchronizedTransformSample.Full(new Vector3(2, 0, 0), Quaternion.identity, new Vector3(2, 0, 0));
            Assert.That(playback.Push(first, 0), Is.True);
            Assert.That(playback.Push(second, 1000), Is.True);
            Assert.That(playback.TrySample(1750, 150, out var sample), Is.True);
            Assert.That(sample.Position.x, Is.EqualTo(3.5f).Within(.001f));
        }

        [Test]
        public void PresentationTimelineUsesAdvertisedRateWithoutRunningBackward()
        {
            var timeline = new TransformPresentationTimeline();
            Assert.That(TransformPresentationTimeline.TargetDelayMs(100, 33, true, 1500), Is.EqualTo(100));
            Assert.That(TransformPresentationTimeline.TargetDelayMs(100, 1000, true, 1500), Is.EqualTo(1100).Within(.001f));
            Assert.That(TransformPresentationTimeline.TargetDelayMs(100, 1000, false, 1500), Is.EqualTo(100));

            double first = timeline.Advance(0, 100, 0, true, 1500);
            double growing = timeline.Advance(1000, 100, 1000, true, 1500);
            double grown = timeline.Advance(1200, 100, 1000, true, 1500);
            Assert.That(first, Is.EqualTo(-100));
            Assert.That(growing, Is.GreaterThanOrEqualTo(first));
            Assert.That(grown, Is.GreaterThanOrEqualTo(growing));
            Assert.That(timeline.CurrentDelayMs, Is.EqualTo(1100).Within(.001f));

            double shrinking = timeline.Advance(2200, 100, 33, true, 1500);
            Assert.That(shrinking, Is.GreaterThan(grown));
            Assert.That(timeline.CurrentDelayMs, Is.EqualTo(600).Within(.001f));
        }

        // ─── Interpolation ──────────────────────────────────────────────────────

        [Test]
        public void Sample_LerpsBetweenTheBracketingFrames()
        {
            var playback = new TransformPlaybackBuffer();
            Assert.IsTrue(playback.Push(Frame(0, 1000, 0)));
            Assert.IsTrue(playback.Push(Frame(1, 1100, 10)));
            Assert.IsTrue(playback.Push(Frame(2, 1200, 30)));
            Assert.IsTrue(playback.TrySample(1025, 100, out var a));
            Assert.AreEqual(2.5f, a.Position.x, Tolerance);
            Assert.IsTrue(playback.TrySample(1150, 100, out var b));
            Assert.AreEqual(20f, b.Position.x, Tolerance);
        }

        [Test]
        public void Sample_SlerpsTheRotation()
        {
            var playback = new TransformPlaybackBuffer();
            var from = Frame(0, 0, 0);
            from.Sample = SynchronizedTransformSample.PositionRotation(Vector3.zero, Quaternion.identity);
            var to = Frame(1, 100, 0);
            to.Sample = SynchronizedTransformSample.PositionRotation(Vector3.zero, Quaternion.Euler(0, 90, 0));
            playback.Push(from);
            playback.Push(to);
            Assert.IsTrue(playback.TrySample(50, 100, out var sample));
            Assert.AreEqual(45f, Quaternion.Angle(Quaternion.identity, sample.Rotation), 0.01f);
            Assert.AreEqual(45f, sample.Rotation.eulerAngles.y, 0.01f);
        }

        [Test]
        public void Sample_BeforeTheFirstFrame_HoldsTheFirstFrame()
        {
            var playback = new TransformPlaybackBuffer();
            playback.Push(Frame(0, 1000, 4));
            playback.Push(Frame(1, 1100, 8));
            Assert.IsTrue(playback.TrySample(10, 100, out var sample));
            Assert.AreEqual(4f, sample.Position.x);
        }

        [Test]
        public void Sample_EmptyBuffer_HasNothing()
        {
            Assert.IsFalse(new TransformPlaybackBuffer().TrySample(0, 100, out _));
        }

        // ─── Extrapolation: at most max(base, 1.25 x max(validFor, mean of the last six intervals)) ──

        [Test]
        public void Extrapolation_WithoutVelocity_HoldsTheNewestFrame()
        {
            var playback = new TransformPlaybackBuffer();
            playback.Push(Frame(0, 1000, 5));
            Assert.IsTrue(playback.TrySample(5000, 100, out var sample));
            Assert.AreEqual(5f, sample.Position.x);
        }

        [Test]
        public void Extrapolation_At30Hz_IsCappedAtTheBase100Ms()
        {
            var playback = new TransformPlaybackBuffer();
            playback.Push(Moving(0, 1000, 0, 2f));
            // 1.25 x 33 = 41.25 < 100, so 100 ms at 2 m/s.
            Assert.IsTrue(playback.TrySample(1500, 100, out var far));
            Assert.AreEqual(0.2f, far.Position.x, Tolerance);
            // Inside the cap the prediction is linear.
            Assert.IsTrue(playback.TrySample(1050, 100, out var near));
            Assert.AreEqual(0.1f, near.Position.x, Tolerance);
        }

        [Test]
        public void Extrapolation_FollowsTheMeasuredInterval()
        {
            var playback = new TransformPlaybackBuffer();
            playback.Push(Moving(0, 0, 0, 1f));
            playback.Push(Moving(1, 200, 0.2f, 1f));
            playback.Push(Moving(2, 400, 0.4f, 1f));
            // Mean interval 200 ms: the cap is 1.25 x 200 = 250 ms.
            Assert.IsTrue(playback.TrySample(1400, 100, out var sample));
            Assert.AreEqual(0.65f, sample.Position.x, Tolerance);
        }

        [Test]
        public void Extrapolation_FollowsTheAdvertisedInterval()
        {
            var playback = new TransformPlaybackBuffer();
            playback.Push(Moving(0, 0, 0, 1f, validForMs: 400));
            // validFor 400 ms: the cap is 1.25 x 400 = 500 ms.
            Assert.IsTrue(playback.TrySample(2000, 100, out var sample));
            Assert.AreEqual(0.5f, sample.Position.x, Tolerance);
        }

        [Test]
        public void Extrapolation_AveragesOnlyTheLastSixIntervals()
        {
            var playback = new TransformPlaybackBuffer();
            // One slow gap, then six 10 ms intervals: the slow gap is out of the window, so the cap stays 100 ms.
            playback.Push(Moving(0, 0, 0, 1f));
            double t = 1000;
            for (ushort sequence = 1; sequence <= 7; sequence++, t += 10)
            {
                playback.Push(Moving(sequence, t, 0, 1f));
            }
            Assert.AreEqual(10f, playback.EstimatedIntervalMs(), Tolerance);
            Assert.IsTrue(playback.TrySample(t - 10 + 1000, 100, out var sample));
            Assert.AreEqual(0.1f, sample.Position.x, Tolerance);
        }

        // ─── Stream identity and sequence ───────────────────────────────────────

        [Test]
        public void Push_RejectsAnOlderOrRepeatedSequence()
        {
            var playback = new TransformPlaybackBuffer();
            Assert.IsTrue(playback.Push(Frame(10, 0, 0)));
            Assert.IsFalse(playback.Push(Frame(9, 10, 1)));
            Assert.IsFalse(playback.Push(Frame(10, 20, 2)));
            Assert.IsTrue(playback.Push(Frame(11, 30, 3)));
            Assert.AreEqual(2, playback.Count);
        }

        [Test]
        public void Push_AcceptsTheSequenceAcrossTheU16Wrap()
        {
            var playback = new TransformPlaybackBuffer();
            Assert.IsTrue(playback.Push(Frame(ushort.MaxValue - 1, 0, 0)));
            Assert.IsTrue(playback.Push(Frame(ushort.MaxValue, 10, 1)));
            Assert.IsTrue(playback.Push(Frame(0, 20, 2)));
            Assert.IsTrue(playback.Push(Frame(1, 30, 3)));
            Assert.IsFalse(playback.Push(Frame(ushort.MaxValue, 40, 4)));
            Assert.AreEqual(4, playback.Count);
        }

        [Test]
        public void Push_ANewGeneration_StartsANewStream()
        {
            var playback = new TransformPlaybackBuffer();
            playback.Push(Frame(500, 0, 0, generation: 1));
            playback.Push(Frame(501, 10, 1, generation: 1));
            // The new owner's publisher restarts at sequence 0.
            Assert.IsTrue(playback.Push(Frame(0, 20, 7, generation: 2)));
            Assert.AreEqual(1, playback.Count);
            Assert.IsTrue(playback.TrySample(0, 100, out var sample));
            Assert.AreEqual(7f, sample.Position.x);
        }

        [Test]
        public void Push_ANewEpoch_StartsANewStream()
        {
            var playback = new TransformPlaybackBuffer();
            playback.Push(Frame(500, 0, 0, epoch: 3));
            // The restarted publisher (same owner and generation) begins again at sequence 0.
            Assert.IsTrue(playback.Push(Frame(0, 20, 7, epoch: 4)));
            Assert.AreEqual(1, playback.Count);
            Assert.IsTrue(playback.Push(Frame(1, 30, 8, epoch: 4)));
            Assert.IsFalse(playback.Push(Frame(0, 40, 9, epoch: 4)));
            Assert.AreEqual(2, playback.Count);
        }

        [Test]
        public void Push_ADiscontinuity_DropsTheHistory()
        {
            var playback = new TransformPlaybackBuffer();
            playback.Push(Frame(0, 0, 0));
            playback.Push(Frame(1, 10, 1));
            Assert.IsTrue(playback.Push(Frame(2, 20, 50, discontinuity: true)));
            Assert.AreEqual(1, playback.Count);
            Assert.IsTrue(playback.TrySample(15, 100, out var sample));
            Assert.AreEqual(50f, sample.Position.x);
        }

        [Test]
        public void Push_KeepsTheTimelineMonotonic()
        {
            var playback = new TransformPlaybackBuffer();
            playback.Push(Frame(0, 1000, 0), 1000);
            // A later frame mapped to an earlier local time keeps the previous timestamp.
            playback.Push(Frame(1, 1010, 10), 990);
            Assert.IsTrue(playback.TrySample(1000, 100, out var sample));
            Assert.AreEqual(0f, sample.Position.x);
        }

        [Test]
        public void Push_BeyondCapacity_DropsTheOldestFrames()
        {
            var playback = new TransformPlaybackBuffer(4);
            for (ushort sequence = 0; sequence < 6; sequence++) playback.Push(Frame(sequence, sequence * 10, sequence));
            Assert.AreEqual(4, playback.Count);
            Assert.IsTrue(playback.TrySample(0, 100, out var oldest));
            Assert.AreEqual(2f, oldest.Position.x);
        }

        [Test]
        public void Clear_ForgetsTheStream()
        {
            var playback = new TransformPlaybackBuffer();
            playback.Push(Frame(100, 0, 0));
            playback.Clear();
            Assert.AreEqual(0, playback.Count);
            Assert.IsTrue(playback.Push(Frame(3, 10, 1)));
        }

        // ─── The presentation clock the synced transform runs ────────────────────

        [Test]
        public void Timeline_At30Hz_PlaysBack100MsBehind()
        {
            var timeline = new TransformPresentationTimeline();
            Assert.AreEqual(4900, timeline.Advance(5000, 100, 33, true, 1500), 1e-6);
            Assert.AreEqual(100f, timeline.CurrentDelayMs);
        }

        [Test]
        public void Timeline_RateAwareDelay_IsCappedAt1500Ms()
        {
            Assert.AreEqual(1500f, TransformPresentationTimeline.TargetDelayMs(100, 5000, true, 1500));
        }

        [Test]
        public void Timeline_GrowsByAtMostNineTenthsOfTheElapsedTime()
        {
            var timeline = new TransformPresentationTimeline();
            timeline.Advance(0, 100, 33, true, 1500);
            timeline.Advance(100, 100, 1000, true, 1500);
            Assert.AreEqual(190f, timeline.CurrentDelayMs, Tolerance);
        }

        [Test]
        public void Timeline_ShrinksAt500MsPerSecond()
        {
            var timeline = new TransformPresentationTimeline();
            timeline.Advance(0, 100, 1000, true, 1500);
            Assert.AreEqual(1100f, timeline.CurrentDelayMs, Tolerance);
            timeline.Advance(200, 100, 33, true, 1500);
            Assert.AreEqual(1000f, timeline.CurrentDelayMs, Tolerance);
        }
    }
}
