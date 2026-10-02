// <mirror source="Packages/com.sidequest.packetparty/Runtime/Transforms/SynchronizedTransformValues.cs" sha256="e6369f80cc5db92e1e204120beed62035152f6c189300831c3798b0bca44b067" mode="port" />
// The value types of the synchronized-transform pipeline (SynchronizedTransformValues.cs:7-70, 163-174), cut down to
// what an automatic-identity synced object uses. The frame's TransformHandle (descriptor slot + generation) becomes the
// relay's stream identity: the object's ownership generation plus the publisher epoch (RELAY.md 6.2, tf.objects).
using System;
using UnityEngine;

namespace BS.LocalMultiplayer.Objects
{
    [Flags]
    public enum TransformBaseFields : byte
    {
        Position = 0,
        Rotation = 1 << 0,
        Velocity = 1 << 1
    }

    /// <summary>Read and apply a pose through the Transform, or through a Rigidbody (PacketPartySynchronizedTransform.cs:10).</summary>
    public enum TransformPoseBinding { Transform, Rigidbody }

    [Serializable]
    public struct SynchronizedTransformSample
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Velocity;
        public TransformBaseFields Fields;

        public bool HasRotation => (Fields & TransformBaseFields.Rotation) != 0;
        public bool HasVelocity => (Fields & TransformBaseFields.Velocity) != 0;

        public static SynchronizedTransformSample PositionOnly(Vector3 position) => new SynchronizedTransformSample
        {
            Position = position,
            Rotation = Quaternion.identity,
            Fields = TransformBaseFields.Position
        };

        public static SynchronizedTransformSample PositionRotation(Vector3 position, Quaternion rotation)
            => new SynchronizedTransformSample
            {
                Position = position,
                Rotation = rotation,
                Fields = TransformBaseFields.Rotation
            };

        public static SynchronizedTransformSample Full(Vector3 position, Quaternion rotation, Vector3 velocity)
            => new SynchronizedTransformSample
            {
                Position = position,
                Rotation = rotation,
                Velocity = velocity,
                Fields = TransformBaseFields.Rotation | TransformBaseFields.Velocity
            };
    }

    /// <summary>
    /// One received pose. <see cref="Generation"/> and <see cref="Epoch"/> together name the publisher stream that
    /// production identifies by its descriptor handle: a new owner (generation) or a restarted publisher (epoch)
    /// starts a new stream, and the playback buffer starts over.
    /// </summary>
    public struct SynchronizedTransformFrame
    {
        /// <summary>The object's ownership generation the frame was published under.</summary>
        public uint Generation;
        /// <summary>The publisher's epoch: bumped every time a publisher starts (production: a new registration).</summary>
        public int Epoch;
        public ushort Sequence;
        /// <summary><see cref="MachineClock.NowMs"/> at capture. Every MPPM player shares that clock.</summary>
        public double CaptureTimestampMs;
        /// <summary>Nominal milliseconds this pose remains valid.</summary>
        public ushort ValidForMs;
        public SynchronizedTransformSample Sample;
        public bool Discontinuity;
        public bool Keyframe;
    }
}
