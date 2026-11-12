// <mirror source="Packages/com.sidequest.packetparty/Runtime/Transforms/TransformPlaybackBuffer.cs" sha256="e855add2e2ecad31969f9e36cffb6f5b3b7245ffd4e98cbe2fae559cb7f3135e" mode="port" />
// <mirror source="Packages/com.sidequest.packetparty/Runtime/Transforms/TransformFrameCodec.cs" sha256="b62e3cff986cb8dd5d66a549e1c511ea30e602ad18d77348fd44d0c026d6faa0" mode="port" />
// TransformPlaybackBuffer (:15-216) without the transform-job path, with SynchronizedTransformFrame/Sample replaced by
// PoseFrame/PoseSample. Timestamps are already on this machine's QPC timeline (MachineClock), so the uint unwrap is
// gone. IsNewerSequence (:210-214) and SampleIsValid (:216-223) come from TransformFrameCodec.
using System;
using UnityEngine;

namespace BS.LocalMultiplayer.Avatars
{
    /// <summary>A transform pose, as PacketParty's SynchronizedTransformSample.</summary>
    public struct PoseSample
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Velocity;
        public bool HasRotation;
        public bool HasVelocity;
    }

    /// <summary>
    /// A received pose frame, as PacketParty's SynchronizedTransformFrame. <see cref="Generation"/> stands in for the
    /// transform handle's generation: the receiver bumps it whenever the sender starts a new registration (a frame
    /// flagged as a discontinuity), as a new descriptor does in production.
    /// </summary>
    public struct PoseFrame
    {
        public uint Generation;
        public ushort Sequence;
        /// <summary>Capture time on this machine's <see cref="MachineClock"/> timeline, in milliseconds.</summary>
        public double CaptureTimestampMs;
        public ushort ValidForMs;
        public PoseSample Sample;
        public bool Discontinuity;
    }

    /// <summary>
    /// The receive-side buffer a remote pose plays back from: interpolates between the frames that bracket the
    /// presentation time, and past the newest frame extrapolates along its velocity for a capped time. Out-of-order
    /// frames are rejected by sequence; a discontinuity or a new generation starts the buffer over.
    /// </summary>
    public sealed class PosePlaybackBuffer
    {
        struct Entry
        {
            public PoseFrame Frame;
            public double CaptureTimestampMs;
        }

        readonly Entry[] _entries;
        int _start;
        int _count;
        uint _generation;
        bool _hasGeneration;
        bool _hasLiveSequence;

        public PosePlaybackBuffer(int capacity = 32)
        {
            if (capacity < 2) throw new ArgumentOutOfRangeException(nameof(capacity));
            _entries = new Entry[capacity];
        }

        public int Count => _count;

        public void Clear()
        {
            _start = 0;
            _count = 0;
            _hasGeneration = false;
            _hasLiveSequence = false;
        }

        /// <summary>A structural baseline (production seeds one at descriptor install); the first live frame replaces it.</summary>
        public void Seed(PoseFrame frame)
        {
            _start = 0;
            _count = 1;
            _generation = frame.Generation;
            _hasGeneration = true;
            _hasLiveSequence = false;
            _entries[0] = new Entry { Frame = frame, CaptureTimestampMs = frame.CaptureTimestampMs };
        }

        public bool Push(PoseFrame frame)
            => Push(frame, double.NaN);

        /// <summary>
        /// Adds a live frame, timed at <paramref name="localCaptureTimestampMs"/> (NaN: the frame's own timestamp).
        /// False when it is not newer than the newest frame of the same generation.
        /// </summary>
        public bool Push(PoseFrame frame, double localCaptureTimestampMs)
        {
            if (!_hasGeneration || _generation != frame.Generation)
            {
                _start = 0;
                _count = 0;
                _generation = frame.Generation;
                _hasGeneration = true;
                _hasLiveSequence = false;
            }
            if (_hasLiveSequence && _count > 0 && !IsNewerSequence(frame.Sequence, At(_count - 1).Frame.Sequence)) return false;
            if (!_hasLiveSequence)
            {
                _start = 0;
                _count = 0;
            }
            if (frame.Discontinuity)
            {
                _start = 0;
                _count = 0;
            }
            double reference = !_hasLiveSequence || _count == 0 ? frame.CaptureTimestampMs : At(_count - 1).CaptureTimestampMs;
            double mappedTimestamp = double.IsNaN(localCaptureTimestampMs)
                ? frame.CaptureTimestampMs
                : localCaptureTimestampMs;
            // Clock-offset estimates can tighten as lower-latency packets arrive.
            // Preserve each stream's monotonic playback timeline when that happens.
            if (_hasLiveSequence && _count > 0) mappedTimestamp = Math.Max(reference, mappedTimestamp);
            var entry = new Entry { Frame = frame, CaptureTimestampMs = mappedTimestamp };
            if (_count < _entries.Length)
            {
                _entries[(_start + _count) % _entries.Length] = entry;
                _count++;
            }
            else
            {
                _entries[_start] = entry;
                _start = (_start + 1) % _entries.Length;
            }
            _hasLiveSequence = true;
            return true;
        }

        /// <summary>
        /// The pose at <paramref name="captureTimestampMs"/>: the first frame at or after it when that is the oldest,
        /// else a blend of the two frames around it; past the newest frame, the newest moved along its velocity for at
        /// most max(<paramref name="maximumExtrapolationMs"/>, 1.25 x the frame interval). False while empty.
        /// </summary>
        public bool TrySample(double captureTimestampMs, float maximumExtrapolationMs, out PoseSample sample)
        {
            sample = default;
            if (_count == 0) return false;
            for (int i = 0; i < _count; i++)
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
            Entry latest = At(_count - 1);
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

        float EstimatedIntervalMs()
        {
            if (_count < 2) return 0;
            int intervals = Math.Min(_count - 1, 6);
            double total = 0;
            for (int i = _count - intervals; i < _count; i++)
                total += Math.Max(0, At(i).CaptureTimestampMs - At(i - 1).CaptureTimestampMs);
            return (float)(total / intervals);
        }

        Entry At(int index) => _entries[(_start + index) % _entries.Length];

        static PoseSample Interpolate(PoseSample left, PoseSample right, float amount)
        {
            return new PoseSample
            {
                HasRotation = left.HasRotation,
                HasVelocity = left.HasVelocity,
                Position = Vector3.LerpUnclamped(left.Position, right.Position, amount),
                Rotation = left.HasRotation
                    ? Quaternion.SlerpUnclamped(left.Rotation, right.Rotation, amount)
                    : Quaternion.identity,
                Velocity = left.HasVelocity
                    ? Vector3.LerpUnclamped(left.Velocity, right.Velocity, amount)
                    : Vector3.zero
            };
        }

        /// <summary>16-bit wrap-around sequence order (TransformFrameCodec.IsNewerSequence).</summary>
        public static bool IsNewerSequence(ushort candidate, ushort previous)
        {
            ushort difference = unchecked((ushort)(candidate - previous));
            return difference != 0 && difference < 0x8000;
        }

        /// <summary>
        /// What production's frame decoder accepts: finite position and velocity, and a finite rotation whose squared
        /// length is within [0.25, 2.25]. A frame that fails is dropped whole.
        /// </summary>
        public static bool SampleIsValid(in PoseSample sample)
        {
            if (!Finite(sample.Position) || (sample.HasVelocity && !Finite(sample.Velocity))) return false;
            if (!sample.HasRotation) return true;
            return RotationIsValid(sample.Rotation);
        }

        /// <summary>A finite rotation whose squared length is within [0.25, 2.25] (TransformFrameCodec.SampleIsValid).</summary>
        public static bool RotationIsValid(Quaternion rotation)
        {
            float lengthSquared = rotation.x * rotation.x + rotation.y * rotation.y
                + rotation.z * rotation.z + rotation.w * rotation.w;
            return Finite(rotation) && lengthSquared >= 0.25f && lengthSquared <= 2.25f;
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        internal static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        static bool Finite(Quaternion value) => Finite(value.x) && Finite(value.y)
            && Finite(value.z) && Finite(value.w);
    }
}
