using UnityEngine;

namespace Banter.SDK
{
    public static class BanterPoseExtensions
    {
        /// <summary> Transforms direction from local space to world space </summary>
        public static Vector3 TransformDirection(this Pose pose, Vector3 localDirection)
        {
            return pose.rotation * localDirection;
        }

        /// <summary> Transforms direction from world space to local space </summary>
        public static Vector3 InverseTransformDirection(this Pose pose, Vector3 worldDirection)
        {
            return Quaternion.Inverse(pose.rotation) * worldDirection;
        }

        /// <summary> Transforms position from local space to world space </summary>
        public static Vector3 TransformPoint(this Pose pose, Vector3 localPosition)
        {
            return pose.position + pose.TransformDirection(localPosition);
        }

        /// <summary> Transforms position from world space to local space </summary>
        public static Vector3 InverseTransformPoint(this Pose pose, Vector3 worldPosition)
        {
            Vector3 worldDirection = worldPosition - pose.position;
            return pose.InverseTransformDirection(worldDirection);
        }
    }
}
