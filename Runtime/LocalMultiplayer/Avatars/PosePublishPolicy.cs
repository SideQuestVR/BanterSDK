// <mirror source="Packages/com.sidequest.packetparty/Runtime/Transforms/PacketPartyTransformRuntime.cs" sha256="6e1d7856bc396c441284c8f3e288f7ea4905fcfb5211ff81377cc14f25ff0b05" mode="port" />
// <mirror source="Packages/com.sidequest.packetparty/Runtime/Transforms/PacketPartySynchronizedTransform.cs" sha256="651f879f279d96c94c5c5d31f899f64e9220ad3f270bc7adb72c526d7814f5d8" mode="port" />
// <mirror source="Packages/com.sidequest.packetparty/Runtime/Transforms/UnityTransformPoseAdapter.cs" sha256="4aec2c3c8da174ef8eb1049e4208f0640e6f7909110402e17d96875b1f48bc72" mode="port" />
// One participant publisher out of PacketPartyTransformRuntime: its state (Publisher :309-328, filled in
// CreatePublisherAsync :425-437), the Tick gate (:692-749) and SamplesEqual (:984-988). The defaults are the inspector
// values of PacketPartySynchronizedTransform (:98-113), which NetworkService.PublishPart (:1133-1155) keeps for the
// local "root" (30 Hz, rotation and velocity, 1 s heartbeat). A jump between two samples is flagged as a
// discontinuity, as DetectDiscontinuity (:1043-1057) does. RootPoseSampler is UnityTransformPoseAdapter (:23-51)
// in world space.
using System;
using UnityEngine;

namespace BS.LocalMultiplayer.Avatars
{
    /// <summary>
    /// When the local player's pose goes out: at most 30 times a second, and only when it moved by more than 1 mm,
    /// 0.1 degrees or 0.01 m/s, or a second has passed (heartbeat). A frame that carries the pose extension always
    /// counts as moved, so with the desktop rig the pose streams at the full rate, as production's does with an
    /// avatar loaded.
    /// </summary>
    public sealed class PosePublishPolicy
    {
        public const float MaximumPublishHz = 30f;
        public const float StationaryHeartbeatSeconds = 1f;
        public const float PositionThresholdMeters = .001f;
        public const float RotationThresholdDegrees = .1f;
        public const float VelocityThresholdMetersPerSecond = .01f;

        /// <summary>
        /// A root that moves at least <see cref="DiscontinuityMinimumDistance"/> between two samples, at
        /// <see cref="DiscontinuityMinimumSpeed"/> (m/s) or faster, teleported: the frame is flagged as a
        /// discontinuity, so receivers jump to it instead of interpolating across the gap (:449-458).
        /// </summary>
        public const float DiscontinuityMinimumDistance = 2f;

        /// <inheritdoc cref="DiscontinuityMinimumDistance"/>
        public const float DiscontinuityMinimumSpeed = 100f;

        readonly double _minimumIntervalMs = 1000.0 / MaximumPublishHz;
        readonly double _heartbeatMs = Math.Max(1, StationaryHeartbeatSeconds * 1000.0);
        readonly float _positionThresholdSquared = Math.Max(0, PositionThresholdMeters * PositionThresholdMeters);
        readonly float _rotationThresholdDegrees = Math.Max(0, RotationThresholdDegrees);
        readonly float _velocityThresholdSquared = Math.Max(0, VelocityThresholdMetersPerSecond * VelocityThresholdMetersPerSecond);

        ushort _sequence;
        double _lastSentAt;
        bool _hasSent;
        bool _forceKeyframe = true;
        PoseSample _lastSample;
        bool _hasLastSample;
        bool _discontinuityNext = true;
        Vector3 _previousSampledPosition;
        double _previousSampledAt;
        bool _hasPreviousSampled;

        /// <summary>The frames' validForMs: the publish interval, rounded (33 at 30 Hz).</summary>
        public ushort ValidForMs => (ushort)Math.Min(ushort.MaxValue, Math.Max(1, (int)Math.Round(_minimumIntervalMs)));

        /// <summary>
        /// A new registration: production registers the publisher again on every join (a new descriptor, so receivers
        /// start a fresh playback buffer). Here the next frame is flagged as a discontinuity instead, which the
        /// receiver treats as that new generation. The sequence keeps counting, so a receiver that kept its buffer
        /// still takes the new frames as newer.
        /// </summary>
        public void Reset()
        {
            _hasSent = false;
            _hasLastSample = false;
            _hasPreviousSampled = false;
            _forceKeyframe = true;
            _discontinuityNext = true;
        }

        /// <summary>The rate gate, checked before the pose is sampled (Tick :699-701).</summary>
        public bool IsDue(double nowMs) => !(_hasSent && nowMs - _lastSentAt < _minimumIntervalMs);

        /// <summary>
        /// Decides whether the sampled pose goes out now (Tick :719-744). When it does, returns its sequence and
        /// whether it is a discontinuity (the first frame of a registration, or a jump), and records it as the last
        /// frame sent. Call it once per sample: the jump is measured from the previous sample, sent or not.
        /// </summary>
        public bool TryCommit(double nowMs, in PoseSample sample, bool hasExtension, out ushort sequence, out bool discontinuity)
        {
            DetectDiscontinuity(sample, nowMs);
            bool changed = hasExtension || !_hasLastSample || !SamplesEqual(sample, _lastSample);
            bool heartbeat = !_hasSent || nowMs - _lastSentAt >= _heartbeatMs;
            if (!changed && !heartbeat && !_forceKeyframe && !_discontinuityNext)
            {
                sequence = 0;
                discontinuity = false;
                return false;
            }
            sequence = _sequence;
            discontinuity = _discontinuityNext;
            _sequence++;
            _lastSentAt = nowMs;
            _hasSent = true;
            _lastSample = sample;
            _hasLastSample = true;
            _forceKeyframe = false;
            _discontinuityNext = false;
            return true;
        }

        // Between consecutive samples, not sent frames: a still player sends rarely, so a teleport after a long idle
        // would otherwise look slow. Under a low publish rate, real motion still spans the longer sample interval.
        void DetectDiscontinuity(in PoseSample sample, double captureTimestampMs)
        {
            if (_hasPreviousSampled)
            {
                float jump = (sample.Position - _previousSampledPosition).magnitude;
                double seconds = (captureTimestampMs - _previousSampledAt) / 1000.0;
                if (jump >= DiscontinuityMinimumDistance && (seconds <= 0 || jump / seconds >= DiscontinuityMinimumSpeed))
                    _discontinuityNext = true;
            }
            _previousSampledPosition = sample.Position;
            _previousSampledAt = captureTimestampMs;
            _hasPreviousSampled = true;
        }

        bool SamplesEqual(in PoseSample left, in PoseSample right)
            => left.HasRotation == right.HasRotation && left.HasVelocity == right.HasVelocity
                && (left.Position - right.Position).sqrMagnitude <= _positionThresholdSquared
                && (!left.HasRotation || Quaternion.Angle(left.Rotation, right.Rotation) <= _rotationThresholdDegrees)
                && (!left.HasVelocity || (left.Velocity - right.Velocity).sqrMagnitude <= _velocityThresholdSquared);
    }

    /// <summary>
    /// Samples a transform's world pose, with the velocity measured since the previous sample (production's
    /// UnityTransformPoseAdapter with rotation and velocity, in world space).
    /// </summary>
    public sealed class RootPoseSampler
    {
        Vector3 _previousPosition;
        double _previousTimestampMs;
        bool _hasPrevious;

        /// <summary>Forget the previous sample: production builds a new adapter for every registration.</summary>
        public void Reset() => _hasPrevious = false;

        public PoseSample Sample(Vector3 position, Quaternion rotation, double captureTimestampMs)
        {
            Vector3 velocity = Vector3.zero;
            if (_hasPrevious)
            {
                double elapsedSeconds = (captureTimestampMs - _previousTimestampMs) / 1000.0;
                if (elapsedSeconds > 0.000001) velocity = (position - _previousPosition) / (float)elapsedSeconds;
            }
            _previousPosition = position;
            _previousTimestampMs = captureTimestampMs;
            _hasPrevious = true;
            return new PoseSample
            {
                Position = position,
                Rotation = rotation,
                Velocity = velocity,
                HasRotation = true,
                HasVelocity = true
            };
        }
    }
}
