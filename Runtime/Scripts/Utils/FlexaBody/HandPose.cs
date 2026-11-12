#if !BANTER_FLEX
using UnityEngine;

namespace SideQuest.FlexaBody
{
    /// <summary> Lerpable HandPose, Set of Vector4 FingerPoses </summary>
    [System.Serializable]
    public class HandPose
    {
        public Vector4 thumb;
        public Vector4 index;
        public Vector4 mid;
        public Vector4 ring;
        public Vector4 pinky;

        public HandPose()
        {
            thumb = new Vector4();
            index = new Vector4();
            mid = new Vector4();
            ring = new Vector4();
            pinky = new Vector4();
        }

        public HandPose(HandPose copyPose)
        {
            thumb = copyPose.thumb;
            index = copyPose.index;
            mid = copyPose.mid;
            ring = copyPose.ring;
            pinky = copyPose.pinky;
        }

        public static HandPose Lerp(HandPose fromPose, HandPose toPose, float time)
        {
            HandPose pose = new HandPose
            {
                thumb = Vector4.Lerp(fromPose.thumb, toPose.thumb, time),
                index = Vector4.Lerp(fromPose.index, toPose.index, time),
                mid = Vector4.Lerp(fromPose.mid, toPose.mid, time),
                ring = Vector4.Lerp(fromPose.ring, toPose.ring, time),
                pinky = Vector4.Lerp(fromPose.pinky, toPose.pinky, time)
            };

            return pose;
        }

        public void LerpTo(HandPose toPose, float time)
        {
            thumb = Vector4.Lerp(thumb, toPose.thumb, time);
            index = Vector4.Lerp(index, toPose.index, time);
            mid = Vector4.Lerp(mid, toPose.mid, time);
            ring = Vector4.Lerp(ring, toPose.ring, time);
            pinky = Vector4.Lerp(pinky, toPose.pinky, time);
        }
    }
}

#endif
