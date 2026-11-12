// <mirror source="Packages/com.sidequest.packetparty/Runtime/Transforms/TransformPlaybackBuffer.cs" sha256="1470050328d1466ba57828a1f940c377c8a8f912db38a084134622d21ecd6779" mode="port" />
// TransformPlaybackBuffer and TransformPresentationTimeline, line for line. Type substitutions: the descriptor
// handle's generation becomes the (ownership generation, publisher epoch) pair, and capture timestamps are the shared
// MachineClock's double milliseconds, so the 32-bit unwrap of the NaN path is the identity. The job-input variant
// (TryBuildJobInput) and the profiler marker are not ported: remote poses are applied directly, and the buffer stays
// free of native calls so its edit-mode tests also run outside Unity.
using System;
using UnityEngine;

namespace BS.LocalMultiplayer.Objects
{
    /// <summary>
    /// The receive buffer of one remote transform stream: in-order frames, sampled at a presentation time by
    /// interpolating between the bracketing frames (lerp, slerp) or, past the newest frame, extrapolating with its
    /// velocity for at most max(base limit, 1.25 x the delivery interval).
    /// </summary>
    public sealed class TransformPlaybackBuffer
    {
        private struct Entry
        {
            public SynchronizedTransformFrame Frame;
            public double CaptureTimestampMs;
        }

        private readonly Entry[] entries;
        private int start;
        private int count;
        private uint generation;
        private int epoch;
        private bool hasGeneration;
        private bool hasLiveSequence;

        public TransformPlaybackBuffer(int capacity = 32)
        {
            if (capacity < 2) throw new ArgumentOutOfRangeException(nameof(capacity));
            entries = new Entry[capacity];
        }

        public int Count => count;

        public void Clear()
        {
            start = 0;
            count = 0;
            hasGeneration = false;
            hasLiveSequence = false;
        }

        public void Seed(SynchronizedTransformFrame frame)
        {
            start = 0;
            count = 1;
            generation = frame.Generation;
            epoch = frame.Epoch;
            hasGeneration = true;
            hasLiveSequence = false;
            entries[0] = new Entry { Frame = frame, CaptureTimestampMs = frame.CaptureTimestampMs };
        }

        public bool Push(SynchronizedTransformFrame frame)
            => Push(frame, double.NaN);

        public bool Push(SynchronizedTransformFrame frame, double localCaptureTimestampMs)
        {
            // A new owner (generation) or a restarted publisher (epoch) is a new stream: production's new descriptor
            // handle. Start over, and accept its first frame whatever its sequence.
            if (!hasGeneration || generation != frame.Generation || epoch != frame.Epoch)
            {
                start = 0;
                count = 0;
                generation = frame.Generation;
                epoch = frame.Epoch;
                hasGeneration = true;
                hasLiveSequence = false;
            }
            if (hasLiveSequence && count > 0 && !ObjectTransportRules.IsNewerSequence(frame.Sequence, At(count - 1).Frame.Sequence)) return false;
            if (!hasLiveSequence)
            {
                start = 0;
                count = 0;
            }
            if (frame.Discontinuity)
            {
                start = 0;
                count = 0;
            }
            double reference = !hasLiveSequence || count == 0 ? frame.CaptureTimestampMs : At(count - 1).CaptureTimestampMs;
            // Production unwraps a 32-bit wire timestamp against the reference here; MachineClock timestamps are full
            // doubles, so the unwrapped value is the timestamp itself.
            double mappedTimestamp = double.IsNaN(localCaptureTimestampMs)
                ? frame.CaptureTimestampMs
                : localCaptureTimestampMs;
            // Clock-offset estimates can tighten as lower-latency packets arrive.
            // Preserve each stream's monotonic playback timeline when that happens.
            if (hasLiveSequence && count > 0) mappedTimestamp = Math.Max(reference, mappedTimestamp);
            var entry = new Entry { Frame = frame, CaptureTimestampMs = mappedTimestamp };
            if (count < entries.Length)
            {
                entries[(start + count) % entries.Length] = entry;
                count++;
            }
            else
            {
                entries[start] = entry;
                start = (start + 1) % entries.Length;
            }
            hasLiveSequence = true;
            return true;
        }

        public bool TrySample(double captureTimestampMs, float maximumExtrapolationMs, out SynchronizedTransformSample sample)
        {
            sample = default;
            if (count == 0) return false;
            for (int i = 0; i < count; i++)
            {
                Entry right = At(i);
                if (right.CaptureTimestampMs < captureTimestampMs) continue;
                if (i == 0) { sample = right.Frame.Sample; return true; }
                Entry left = At(i - 1);
                double duration = right.CaptureTimestampMs - left.CaptureTimestampMs;
                float t = duration <= 0 ? 1f : (float)((captureTimestampMs - left.CaptureTimestampMs) / duration);
                sample = Interpolate(left.Frame.Sample, right.Frame.Sample, Mathf.Clamp01(t));
                return true;
            }
            Entry latest = At(count - 1);
            sample = latest.Frame.Sample;
            if (sample.HasVelocity)
            {
                float advertisedIntervalMs = latest.Frame.ValidForMs;
                float adaptiveMaximumMs = Mathf.Max(maximumExtrapolationMs,
                    Mathf.Max(advertisedIntervalMs, EstimatedIntervalMs()) * 1.25f);
                adaptiveMaximumMs = Mathf.Min(adaptiveMaximumMs, ushort.MaxValue);
                float seconds = Mathf.Min(adaptiveMaximumMs, Mathf.Max(0, (float)(captureTimestampMs - latest.CaptureTimestampMs))) / 1000f;
                sample.Position += sample.Velocity * seconds;
            }
            return true;
        }

        /// <summary>Mean of the last (up to) six frame intervals, 0 with fewer than two frames.</summary>
        internal float EstimatedIntervalMs()
        {
            if (count < 2) return 0;
            int intervals = Math.Min(count - 1, 6);
            double total = 0;
            for (int i = count - intervals; i < count; i++)
                total += Math.Max(0, At(i).CaptureTimestampMs - At(i - 1).CaptureTimestampMs);
            return (float)(total / intervals);
        }

        private Entry At(int index) => entries[(start + index) % entries.Length];

        private static SynchronizedTransformSample Interpolate(
            SynchronizedTransformSample left,
            SynchronizedTransformSample right,
            float amount)
        {
            var result = new SynchronizedTransformSample
            {
                Fields = left.Fields,
                Position = Vector3.LerpUnclamped(left.Position, right.Position, amount),
                Rotation = left.HasRotation
                    ? Quaternion.SlerpUnclamped(left.Rotation, right.Rotation, amount)
                    : Quaternion.identity,
                Velocity = left.HasVelocity
                    ? Vector3.LerpUnclamped(left.Velocity, right.Velocity, amount)
                    : Vector3.zero
            };
            return result;
        }
    }

    /// <summary>
    /// Maintains a monotonic presentation clock whose interpolation buffer follows the
    /// per-recipient cadence advertised by transform-v6 frames. Delay grows more slowly
    /// than wall time so a tier change cannot make presentation time run backwards.
    /// </summary>
    public sealed class TransformPresentationTimeline
    {
        private const float IntervalSafetyMultiplier = 1.1f;
        private const float MaximumDelayIncreasePerElapsedMs = .9f;
        private const float DelayDecreaseMsPerSecond = 500f;

        private bool initialized;
        private double previousNowMs;

        public float CurrentDelayMs { get; private set; }

        public void Reset()
        {
            initialized = false;
            previousNowMs = 0;
            CurrentDelayMs = 0;
        }

        public static float TargetDelayMs(
            float minimumDelayMs,
            float advertisedIntervalMs,
            bool rateAware,
            float maximumRateAwareDelayMs)
        {
            float minimum = Mathf.Max(0, minimumDelayMs);
            if (!rateAware || advertisedIntervalMs <= 0) return minimum;
            float maximum = Mathf.Max(minimum, maximumRateAwareDelayMs);
            return Mathf.Clamp(advertisedIntervalMs * IntervalSafetyMultiplier, minimum, maximum);
        }

        public double Advance(
            double nowMs,
            float minimumDelayMs,
            float advertisedIntervalMs,
            bool rateAware,
            float maximumRateAwareDelayMs)
        {
            float target = TargetDelayMs(minimumDelayMs, advertisedIntervalMs, rateAware, maximumRateAwareDelayMs);
            if (!initialized || nowMs < previousNowMs)
            {
                initialized = true;
                previousNowMs = nowMs;
                CurrentDelayMs = target;
                return nowMs - CurrentDelayMs;
            }

            float elapsedMs = (float)Math.Max(0, nowMs - previousNowMs);
            previousNowMs = nowMs;
            if (target > CurrentDelayMs)
            {
                // Keeping this below elapsed wall time guarantees a non-decreasing
                // presentation timestamp while the interpolation buffer grows.
                CurrentDelayMs = Mathf.MoveTowards(
                    CurrentDelayMs,
                    target,
                    elapsedMs * MaximumDelayIncreasePerElapsedMs);
            }
            else
            {
                CurrentDelayMs = Mathf.MoveTowards(
                    CurrentDelayMs,
                    target,
                    elapsedMs * DelayDecreaseMsPerSecond / 1000f);
            }
            return nowMs - CurrentDelayMs;
        }
    }
}
