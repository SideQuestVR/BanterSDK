using UnityEngine;

namespace BS.LocalMultiplayer.Avatars
{
    /// <summary>
    /// The orb's pose extension (tf.participant <c>ext</c>, RELAY.md 6.2), which stands in for production's
    /// BonePoseCodec stream: the hip in world space, the head and both hands relative to the hip, and the seated leg
    /// pose. Like the bone stream it is hip-anchored, so whatever hangs off a remote hip (the head, the hands, an
    /// attachment) follows a hip that is glued to a seat.
    /// </summary>
    public static class PoseExtCodec
    {
        /// <summary>The head at rest: 0.65 m above the hip (<see cref="DesktopHipModel.HeadToHip"/>).</summary>
        public static readonly Vector3 HeadRest = new Vector3(0f, DesktopHipModel.HeadToHip, 0f);
        /// <summary>Where the left hand rests: in front of the hip, the desktop rig's left hand anchor.</summary>
        public static readonly Vector3 LeftHandRest = new Vector3(-0.22f, 0.02f, 0.22f);
        /// <summary>Where the right hand rests while the mouse hand isn't grabbing.</summary>
        public static readonly Vector3 RightHandRest = new Vector3(0.22f, 0.02f, 0.22f);

        /// <summary>The rest pose on a hip: head above it, hands in front of it, standing legs.</summary>
        public static PoseExt RestPose(Vector3 hipPosition, Quaternion hipRotation) => new PoseExt
        {
            HipPosition = hipPosition,
            HipRotation = hipRotation,
            HeadPosition = HeadRest,
            HeadRotation = Quaternion.identity,
            LeftPosition = LeftHandRest,
            LeftRotation = Quaternion.identity,
            RightPosition = RightHandRest,
            RightRotation = Quaternion.identity,
            Seated = false
        };

        /// <summary>Builds the extension from the hip pose and the world poses of the head and hands.</summary>
        public static PoseExt Capture(Vector3 hipPosition, Quaternion hipRotation,
            Vector3 headPosition, Quaternion headRotation,
            Vector3 leftPosition, Quaternion leftRotation,
            Vector3 rightPosition, Quaternion rightRotation,
            bool seated)
        {
            var inverse = Quaternion.Inverse(hipRotation);
            return new PoseExt
            {
                HipPosition = hipPosition,
                HipRotation = hipRotation,
                HeadPosition = inverse * (headPosition - hipPosition),
                HeadRotation = inverse * headRotation,
                LeftPosition = inverse * (leftPosition - hipPosition),
                LeftRotation = inverse * leftRotation,
                RightPosition = inverse * (rightPosition - hipPosition),
                RightRotation = inverse * rightRotation,
                Seated = seated
            };
        }

        /// <summary>A hip-local pose back in world space.</summary>
        public static void ToWorld(Vector3 hipPosition, Quaternion hipRotation, Vector3 localPosition, Quaternion localRotation,
            out Vector3 worldPosition, out Quaternion worldRotation)
        {
            worldPosition = hipPosition + hipRotation * localPosition;
            worldRotation = hipRotation * localRotation;
        }

        /// <summary>
        /// A received extension, checked as production's frame decoder checks a sample (finite values, rotations of
        /// squared length 0.25 to 2.25) and with every rotation normalised. False drops the extension.
        /// </summary>
        public static bool TryNormalize(in PoseExt ext, out PoseExt normalized)
        {
            normalized = ext;
            if (!PosePlaybackBuffer.Finite(ext.HipPosition) || !PosePlaybackBuffer.Finite(ext.HeadPosition)
                || !PosePlaybackBuffer.Finite(ext.LeftPosition) || !PosePlaybackBuffer.Finite(ext.RightPosition))
            {
                return false;
            }
            if (!PosePlaybackBuffer.RotationIsValid(ext.HipRotation) || !PosePlaybackBuffer.RotationIsValid(ext.HeadRotation)
                || !PosePlaybackBuffer.RotationIsValid(ext.LeftRotation) || !PosePlaybackBuffer.RotationIsValid(ext.RightRotation))
            {
                return false;
            }
            normalized.HipRotation = Normalize(ext.HipRotation);
            normalized.HeadRotation = Normalize(ext.HeadRotation);
            normalized.LeftRotation = Normalize(ext.LeftRotation);
            normalized.RightRotation = Normalize(ext.RightRotation);
            return true;
        }

        static Quaternion Normalize(Quaternion q)
        {
            float length = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            return new Quaternion(q.x / length, q.y / length, q.z / length, q.w / length);
        }
    }
}
