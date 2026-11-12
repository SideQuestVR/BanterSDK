using System;
using SideQuest.FlexaBody;
using UnityEngine;

namespace BS
{
    [Serializable]
    public sealed class BSPointHandPose
    {
        public Vector3 position;
        public Vector3 rotation;
        public Vector4 thumb = new Vector4(.15f, .2f, .2f, 0f);
        public Vector4 index = new Vector4(.3f, .4f, .3f, 0f);
        public Vector4 middle = new Vector4(.3f, .4f, .3f, 0f);
        public Vector4 ring = new Vector4(.3f, .4f, .3f, 0f);
        public Vector4 little = new Vector4(.3f, .4f, .3f, 0f);

        public Pose Offset(bool mirror = false)
        {
            var p = position;
            var q = Quaternion.Euler(rotation);
            if (mirror) { p.x = -p.x; q = new Quaternion(q.x, -q.y, -q.z, q.w); }
            return new Pose(p, q);
        }

        public BSPointHandPose Copy(bool mirror = false)
        {
            var copy = (BSPointHandPose)MemberwiseClone();
            var offset = Offset(mirror);
            copy.position = offset.position;
            copy.rotation = offset.rotation.eulerAngles;
            return copy;
        }

        public Vector4 GetFinger(int finger)
        {
            switch (finger)
            {
                case 0: return thumb;
                case 1: return index;
                case 2: return middle;
                case 3: return ring;
                default: return little;
            }
        }

        public void SetFinger(int finger, Vector4 value)
        {
            switch (finger)
            {
                case 0: thumb = value; break;
                case 1: index = value; break;
                case 2: middle = value; break;
                case 3: ring = value; break;
                default: little = value; break;
            }
        }

        internal HandPose Native() => new HandPose { thumb = thumb, index = index, mid = middle, ring = ring, pinky = little };

        internal int FingerHash()
        {
            unchecked
            {
                var hash = 17;
                for (var i = 0; i < 5; i++) hash = hash * 31 + GetFinger(i).GetHashCode();
                return hash;
            }
        }
    }

    // SDK-owned values survive scene bundles without requiring Flexabody in the creator project.
    [Serializable]
    public sealed class BSPointGrabPose
    {
        public bool enabled;
        public bool separateLeft;
        public BSPointHandPose right = new BSPointHandPose();
        public BSPointHandPose left = new BSPointHandPose();
#if UNITY_EDITOR
        public GameObject fitTarget;
        public bool fitToMesh = true;
#endif

        public BSPointHandPose GetHand(bool leftHand) => leftHand && separateLeft ? left : right;
        public Pose GetOffset(bool leftHand) => GetHand(leftHand)?.Offset(leftHand && !separateLeft) ?? Pose.identity;

        public void Apply(GrabHandle handle)
        {
            handle.UseCustomPointPose = enabled && right != null && (!separateLeft || left != null);
            handle.RightPointOffset = GetOffset(false);
            handle.LeftPointOffset = GetOffset(true);
            handle.RightPointHandPose = GetHand(false)?.Native();
            handle.LeftPointHandPose = GetHand(true)?.Native();
        }

        public static BSPointGrabPose For(Component source)
        {
            var handle = source.GetComponent<BSGrabHandle>();
            return handle != null ? handle.CustomPointPose : source.GetComponent<BSGrababble>()?.CustomPointPose;
        }
    }
}
