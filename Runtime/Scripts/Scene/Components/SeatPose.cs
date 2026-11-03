using System;
using UnityEngine;

namespace BS
{
    public enum SeatViewMode { Legacy = 0, UprightFollowYaw = 1, UprightIndependent = 2 }

    /// <summary>Seat-relative pelvis and sole targets in metres, independent of the seat's visual scale.</summary>
    [Serializable]
    public struct SeatPose : IEquatable<SeatPose>
    {
        public Vector3 pelvisPosition;
        public Quaternion pelvisRotation;
        public Vector3 leftFootPosition;
        public Quaternion leftFootRotation;
        public Vector3 rightFootPosition;
        public Quaternion rightFootRotation;
        public Vector3 leftKneeHint;
        public Vector3 rightKneeHint;

        public static SeatPose Default => new SeatPose
        {
            pelvisPosition = new Vector3(0f, .04f, 0f), pelvisRotation = Quaternion.identity,
            leftFootPosition = new Vector3(-.14f, -.6f, .475f), leftFootRotation = Quaternion.identity,
            rightFootPosition = new Vector3(.14f, -.6f, .475f), rightFootRotation = Quaternion.identity,
            leftKneeHint = new Vector3(-.14f, .15f, .45f), rightKneeHint = new Vector3(.14f, .15f, .45f)
        };

        public bool IsValid => Finite(pelvisPosition) && Finite(leftFootPosition) && Finite(rightFootPosition)
            && Finite(leftKneeHint) && Finite(rightKneeHint)
            && Valid(pelvisRotation) && Valid(leftFootRotation) && Valid(rightFootRotation);

        public bool Equals(SeatPose other) => pelvisPosition.Equals(other.pelvisPosition) && pelvisRotation.Equals(other.pelvisRotation)
            && leftFootPosition.Equals(other.leftFootPosition) && leftFootRotation.Equals(other.leftFootRotation)
            && rightFootPosition.Equals(other.rightFootPosition) && rightFootRotation.Equals(other.rightFootRotation)
            && leftKneeHint.Equals(other.leftKneeHint) && rightKneeHint.Equals(other.rightKneeHint);

        public SeatPose Validated()
        {
            if (!IsValid) return Default;
            var value = this;
            value.pelvisRotation = pelvisRotation.normalized;
            value.leftFootRotation = leftFootRotation.normalized;
            value.rightFootRotation = rightFootRotation.normalized;
            return value;
        }

        public static float YawDelta(Quaternion previous, Quaternion current)
        {
            if (!Valid(previous) || !Valid(current)) return 0f;
            var delta = (current.normalized * Quaternion.Inverse(previous.normalized)).normalized;
            if (delta.w < 0f) delta = new Quaternion(-delta.x, -delta.y, -delta.z, -delta.w);
            return delta.w * delta.w + delta.y * delta.y < 1e-8f ? 0f
                : Mathf.DeltaAngle(0f, 2f * Mathf.Atan2(delta.y, delta.w) * Mathf.Rad2Deg);
        }

        /// <summary>Resolves a bent leg without stretching either segment; unreachable feet stop at natural reach.</summary>
        public static void SolveLeg(Vector3 hip, Vector3 kneeHint, Vector3 ankleTarget, float upperLength,
            float lowerLength, out Vector3 knee, out Vector3 ankle)
        {
            knee = ankle = Finite(hip) ? hip : Vector3.zero;
            if (!Finite(hip) || !Finite(kneeHint) || !Finite(ankleTarget) || !Finite(upperLength)
                || !Finite(lowerLength) || upperLength < .0001f || lowerLength < .0001f) return;
            var direction = ankleTarget - hip;
            var distance = direction.magnitude;
            if (!Finite(distance) || !Finite(upperLength * upperLength) || !Finite(lowerLength * lowerLength)) return;
            direction = distance > .0001f ? direction / distance : Vector3.down;
            distance = Mathf.Clamp(distance, Mathf.Abs(upperLength - lowerLength) + .00001f,
                upperLength + lowerLength - .00001f);
            var bend = Vector3.ProjectOnPlane(kneeHint - hip, direction);
            if (!Finite(bend) || !Finite(bend.sqrMagnitude) || bend.sqrMagnitude < .000001f)
                bend = Vector3.ProjectOnPlane(Mathf.Abs(direction.z) < .9f ? Vector3.forward : Vector3.up, direction);
            var along = (upperLength * upperLength + distance * distance - lowerLength * lowerLength) / (2f * distance);
            var raised = Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - along * along));
            if (!Finite(along) || !Finite(raised)) return;
            knee = hip + direction * along + bend.normalized * raised;
            ankle = hip + direction * distance;
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        static bool Valid(Quaternion value)
        {
            var length = Quaternion.Dot(value, value);
            return Finite(value.x) && Finite(value.y) && Finite(value.z) && Finite(value.w)
                && Finite(length) && length > 1e-8f;
        }
    }
}
