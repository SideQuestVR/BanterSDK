// <mirror source="Packages/com.sidequest.packetparty/Runtime/Transforms/UnityTransformPoseAdapter.cs" sha256="4aec2c3c8da174ef8eb1049e4208f0640e6f7909110402e17d96875b1f48bc72" mode="port" />
// UnityTransformPoseAdapter (world space only: synced objects are never network-parented) and
// UnityRigidbodyPoseAdapter, line for line. ITransformPoseSource is PacketPartyTransformRuntime.cs:22-26.
using System;
using UnityEngine;

namespace BS.LocalMultiplayer.Objects
{
    public interface ITransformPoseSource
    {
        bool TrySample(double captureTimestampMs, out SynchronizedTransformSample sample);
    }

    /// <summary>World-space Transform pose source and sink. Velocity, when synced, comes from position deltas between samples.</summary>
    public sealed class UnityTransformPoseAdapter : ITransformPoseSource
    {
        private readonly Transform transform;
        private readonly TransformBaseFields fields;
        private Vector3 previousPosition;
        private double previousTimestampMs;
        private bool hasPrevious;

        public UnityTransformPoseAdapter(Transform transform, TransformBaseFields fields)
        {
            this.transform = transform != null ? transform : throw new ArgumentNullException(nameof(transform));
            this.fields = fields;
        }

        public bool TrySample(double captureTimestampMs, out SynchronizedTransformSample sample)
        {
            if (transform == null)
            {
                sample = default;
                return false;
            }
            Vector3 position = transform.position;
            Vector3 velocity = Vector3.zero;
            if ((fields & TransformBaseFields.Velocity) != 0 && hasPrevious)
            {
                double elapsedSeconds = (captureTimestampMs - previousTimestampMs) / 1000.0;
                if (elapsedSeconds > 0.000001) velocity = (position - previousPosition) / (float)elapsedSeconds;
            }
            sample = new SynchronizedTransformSample
            {
                Fields = fields,
                Position = position,
                Rotation = (fields & TransformBaseFields.Rotation) != 0 ? transform.rotation : Quaternion.identity,
                Velocity = velocity
            };
            previousPosition = position;
            previousTimestampMs = captureTimestampMs;
            hasPrevious = true;
            return true;
        }

        public void Apply(SynchronizedTransformSample sample)
        {
            transform.position = sample.Position;
            if (!sample.HasRotation) return;
            transform.rotation = sample.Rotation;
        }
    }

    /// <summary>World-space Rigidbody source/application adapter for physics-bound synchronized roots.</summary>
    public sealed class UnityRigidbodyPoseAdapter : ITransformPoseSource
    {
        private readonly Rigidbody body;
        private readonly TransformBaseFields fields;

        public UnityRigidbodyPoseAdapter(Rigidbody body, TransformBaseFields fields)
        {
            this.body = body != null ? body : throw new ArgumentNullException(nameof(body));
            this.fields = fields;
        }

        public bool TrySample(double captureTimestampMs, out SynchronizedTransformSample sample)
        {
            if (body == null)
            {
                sample = default;
                return false;
            }
            sample = new SynchronizedTransformSample
            {
                Fields = fields,
                Position = body.position,
                Rotation = (fields & TransformBaseFields.Rotation) != 0 ? body.rotation : Quaternion.identity,
                Velocity = (fields & TransformBaseFields.Velocity) != 0 ? body.linearVelocity : Vector3.zero
            };
            return true;
        }

        /// <summary>
        /// A presentation step longer than this is placed, not moved: a teleport or reset crosses the gap in a few large
        /// steps (tf.objects frames carry no discontinuity, so playback interpolates across it), the first pose of a new
        /// stream in one, and MovePosition across such a step gives the kinematic body the implied velocity (hundreds of
        /// m/s) for that step, which it hands to anything it touches. No real interpolated step comes close.
        /// </summary>
        public const float SnapDistance = 5f;

        public void Apply(SynchronizedTransformSample sample)
        {
            if ((sample.Position - body.position).sqrMagnitude > SnapDistance * SnapDistance)
            {
                Quaternion rotation = sample.HasRotation ? sample.Rotation : body.rotation;
                body.position = sample.Position;
                body.rotation = rotation;
                body.transform.SetPositionAndRotation(sample.Position, rotation);
            }
            else
            {
                body.MovePosition(sample.Position);
                if (sample.HasRotation) body.MoveRotation(sample.Rotation);
            }
            // Remote physics views are deliberately kinematic. Their velocity is
            // interpolation/prediction input, not a value Unity allows us to set
            // on the presentation body.
            if (sample.HasVelocity && !body.isKinematic) body.linearVelocity = sample.Velocity;
        }
    }
}
