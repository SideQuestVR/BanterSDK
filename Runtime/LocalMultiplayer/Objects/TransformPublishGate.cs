// <mirror source="Packages/com.sidequest.packetparty/Runtime/Transforms/PacketPartyTransformRuntime.cs" sha256="11d524870f00cdd38eecad4647167f8d0cd40f0f4cc688894dba057a069fafe9" mode="port" />
// The publisher half of PacketPartyTransformRuntime: the Publisher record (:309-328), its construction from
// TransformPublisherOptions (:425-437), the decisions of Tick (:692-749) and SamplesEqual (:984-988). Encoding and
// sending stay with the caller, which queues a tf.objects frame instead of a transform-v6 datagram. The server's
// demand ceiling (Rate LOD) and the accepted-publish-rate clamp don't exist locally: the rate is the source rate.
using System;
using UnityEngine;

namespace BS.LocalMultiplayer.Objects
{
    /// <summary>
    /// When a synced object's owner sends its pose: at most <see cref="MaximumPublishHz"/> (30 Hz), only when the
    /// pose moved past the thresholds (1 mm, 0.1 degree, 0.01 m/s) or the stationary heartbeat (1 s) is due, and
    /// always first after the publisher starts.
    /// </summary>
    public sealed class TransformPublishGate
    {
        public readonly float MaximumPublishHz;
        public readonly double HeartbeatMs;
        public readonly float PositionThresholdSquared;
        public readonly float RotationThresholdDegrees;
        public readonly float VelocityThresholdSquared;

        public ushort Sequence { get; private set; }
        public double LastSentAt { get; private set; }
        public bool HasSent { get; private set; }
        public bool ForceKeyframe { get; private set; } = true;
        public SynchronizedTransformSample LastSample { get; private set; }
        public bool HasLastSample { get; private set; }

        public TransformPublishGate(
            float maximumPublishHz = 30,
            float stationaryHeartbeatSeconds = 1,
            float positionThresholdMeters = 0.001f,
            float rotationThresholdDegrees = 0.1f,
            float velocityThresholdMetersPerSecond = 0.01f)
        {
            if (maximumPublishHz <= 0 || float.IsNaN(maximumPublishHz) || float.IsInfinity(maximumPublishHz))
                throw new ArgumentOutOfRangeException(nameof(maximumPublishHz));
            MaximumPublishHz = maximumPublishHz;
            HeartbeatMs = Math.Max(1, stationaryHeartbeatSeconds * 1000.0);
            PositionThresholdSquared = Math.Max(0, positionThresholdMeters * positionThresholdMeters);
            RotationThresholdDegrees = Math.Max(0, rotationThresholdDegrees);
            VelocityThresholdSquared = Math.Max(0, velocityThresholdMetersPerSecond * velocityThresholdMetersPerSecond);
        }

        /// <summary>The minimum time between two sends: 1000 / Hz (33.3 ms at 30 Hz).</summary>
        public double MinimumIntervalMs => 1000.0 / MaximumPublishHz;

        /// <summary>The validForMs every frame advertises: the rounded minimum interval (33 at 30 Hz).</summary>
        public ushort ValidForMs => (ushort)Math.Min(ushort.MaxValue, Math.Max(1, (int)Math.Round(MinimumIntervalMs)));

        /// <summary>The rate gate, checked before sampling: false while the last send is younger than the minimum interval.</summary>
        public bool IsDue(double captureTimestampMs)
            => !(HasSent && captureTimestampMs - LastSentAt < MinimumIntervalMs);

        /// <summary>
        /// After sampling: send when the pose changed past a threshold, the heartbeat is due, or the publisher just
        /// started. <paramref name="keyframe"/> is what production stamps as the frame's keyframe flag.
        /// </summary>
        public bool ShouldSend(double captureTimestampMs, SynchronizedTransformSample sample, out bool keyframe)
        {
            bool changed = !HasLastSample || !SamplesEqual(sample, LastSample, this);
            bool heartbeat = !HasSent || captureTimestampMs - LastSentAt >= HeartbeatMs;
            keyframe = ForceKeyframe || heartbeat;
            return changed || heartbeat || ForceKeyframe;
        }

        /// <summary>Records a send and returns the sequence the frame carries; the next one is one higher (u16 wrap).</summary>
        public ushort MarkSent(double captureTimestampMs, SynchronizedTransformSample sample)
        {
            ushort sequence = Sequence;
            Sequence = unchecked((ushort)(Sequence + 1));
            LastSentAt = captureTimestampMs;
            HasSent = true;
            LastSample = sample;
            HasLastSample = true;
            ForceKeyframe = false;
            return sequence;
        }

        /// <summary>A new stream (RebindPublisherDescriptor): sequence 0, send immediately.</summary>
        public void Reset()
        {
            Sequence = 0;
            LastSentAt = 0;
            HasSent = false;
            HasLastSample = false;
            ForceKeyframe = true;
        }

        /// <summary>
        /// Equal within the thresholds (inclusive). Note Quaternion.Angle reports 0 below ~0.16 degrees
        /// (its dot-product epsilon), so in practice a rotation must exceed that to count, as in production.
        /// </summary>
        public static bool SamplesEqual(SynchronizedTransformSample left, SynchronizedTransformSample right, TransformPublishGate publisher)
            => left.Fields == right.Fields
                && (left.Position - right.Position).sqrMagnitude <= publisher.PositionThresholdSquared
                && (!left.HasRotation || Quaternion.Angle(left.Rotation, right.Rotation) <= publisher.RotationThresholdDegrees)
                && (!left.HasVelocity || (left.Velocity - right.Velocity).sqrMagnitude <= publisher.VelocityThresholdSquared);
    }
}
