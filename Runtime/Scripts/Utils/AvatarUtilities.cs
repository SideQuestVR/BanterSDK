using System;
using System.Collections.Generic;
using SQ.Tracking;
using UnityEngine;

public class AnimationCurveContainer
{
    public AnimationCurve curve = new AnimationCurve();
    public string path;
    public string propertyName;
}
public class AvatarUtilities
{
    static SyncedBoneStatev1 bones = new SyncedBoneStatev1();
    public static AnimationClip clip;
    static Dictionary<string, AnimationCurveContainer[]> curves = new Dictionary<string, AnimationCurveContainer[]>();
    private static string GetTransformPathName(Transform rootTransform, Transform targetTransform)
    {

        string returnName = targetTransform.name;
        Transform tempObj = targetTransform;

        if (tempObj == rootTransform)
            return "";

        while (tempObj.parent != rootTransform)
        {
            returnName = tempObj.parent.name + "/" + returnName;
            tempObj = tempObj.parent;
        }

        return returnName;
    }
    public static void SetAnimationCurves()
    {
        foreach (var curve in curves)
        {
            foreach (var container in curve.Value)
            {
                clip.SetCurve(container.path, typeof(Transform), container.propertyName, container.curve);
            }
        }
        clip.EnsureQuaternionContinuity();
    }

    public static void ParseAnimationCurves(byte[] bytes)
    {
        if (clip == null)
        {
            clip = new AnimationClip();
        }
        var head = 0;
        while (head < bytes.Length)
        {
            var time = BitConverter.ToSingle(bytes, head);
            head += 4;
            var length = BitConverter.ToInt32(bytes, head);
            head += 4;
            var data = new byte[length];
            Array.Copy(bytes, head, data, 0, length);
            bones.FromBytes(data, 0);
            ParseFrame(time);
            head += length;
        }
    }

    private static void ParseQuatFrame(float time, string key, HalfQuat quat, bool isWorld = false)
    {
        if (!curves.ContainsKey(key))
        {
            curves.Add(key, new AnimationCurveContainer[4]);
            curves[key][0] = new AnimationCurveContainer();
            curves[key][0].propertyName = isWorld ? "localRotation.x" : "localRotation.x";
            curves[key][1] = new AnimationCurveContainer();
            curves[key][1].propertyName = isWorld ? "localRotation.y" : "localRotation.y";
            curves[key][2] = new AnimationCurveContainer();
            curves[key][2].propertyName = isWorld ? "localRotation.z" : "localRotation.z";
            curves[key][3] = new AnimationCurveContainer();
            curves[key][3].propertyName = isWorld ? "localRotation.w" : "localRotation.w";
        }
        curves[key][0].curve.AddKey(time, quat.X);
        curves[key][1].curve.AddKey(time, quat.Y);
        curves[key][2].curve.AddKey(time, quat.Z);
        curves[key][3].curve.AddKey(time, quat.W);
    }
    private static void ParseVecFrame(float time, string key, SQ.Tracking.Vector3 vec)
    {
        if (!curves.ContainsKey(key))
        {
            curves.Add(key, new AnimationCurveContainer[3]);
            curves[key][0] = new AnimationCurveContainer();
            curves[key][0].propertyName = "localPosition.x";
            curves[key][1] = new AnimationCurveContainer();
            curves[key][1].propertyName = "localPosition.y";
            curves[key][2] = new AnimationCurveContainer();
            curves[key][2].propertyName = "localPosition.z";
        }
        curves[key][0].curve.AddKey(time, vec.X);
        curves[key][1].curve.AddKey(time, vec.Y);
        curves[key][2].curve.AddKey(time, vec.Z);
    }
    private static void ParseFrame(float time)
    {
        // ParseVecFrame(time, "HEAD_TRACKING_position", bones.HEAD_TRACKING_position);

        // ParseQuatFrame(time, "HEAD_TRACKING", bones.HEAD_TRACKING);
        ParseQuatFrame(time, "HEAD", bones.HEAD);
        ParseQuatFrame(time, "NECK", bones.NECK);
        ParseQuatFrame(time, "HIPS", bones.HIPS, true);

        ParseVecFrame(time, "HIPS_position", bones.HIPS_position);

        ParseQuatFrame(time, "SPINE", bones.SPINE);
        ParseQuatFrame(time, "CHEST", bones.CHEST);

        ParseQuatFrame(time, "LEFTARM_SHOULDER", bones.LEFTARM_SHOULDER);
        ParseQuatFrame(time, "LEFTARM_UPPER", bones.LEFTARM_UPPER);
        ParseQuatFrame(time, "LEFTARM_LOWER", bones.LEFTARM_LOWER);
        ParseQuatFrame(time, "LEFTARM_HAND", bones.LEFTARM_HAND);

        ParseQuatFrame(time, "RIGHTARM_SHOULDER", bones.RIGHTARM_SHOULDER);
        ParseQuatFrame(time, "RIGHTARM_UPPER", bones.RIGHTARM_UPPER);
        ParseQuatFrame(time, "RIGHTARM_LOWER", bones.RIGHTARM_LOWER);
        ParseQuatFrame(time, "RIGHTARM_HAND", bones.RIGHTARM_HAND);

        ParseQuatFrame(time, "LEFTLEG_UPPER", bones.LEFTLEG_UPPER);
        ParseQuatFrame(time, "LEFTLEG_LOWER", bones.LEFTLEG_LOWER);
        ParseQuatFrame(time, "LEFTLEG_FOOT", bones.LEFTLEG_FOOT);
        ParseQuatFrame(time, "LEFTLEG_TOES", bones.LEFTLEG_TOES);

        ParseQuatFrame(time, "RIGHTLEG_UPPER", bones.RIGHTLEG_UPPER);
        ParseQuatFrame(time, "RIGHTLEG_LOWER", bones.RIGHTLEG_LOWER);
        ParseQuatFrame(time, "RIGHTLEG_FOOT", bones.RIGHTLEG_FOOT);
        ParseQuatFrame(time, "RIGHTLEG_TOES", bones.RIGHTLEG_TOES);

        ParseQuatFrame(time, "LEFTARM_HAND_PINKY1", bones.LEFTARM_HAND_PINKY1);
        ParseQuatFrame(time, "LEFTARM_HAND_PINKY2", bones.LEFTARM_HAND_PINKY2);
        ParseQuatFrame(time, "LEFTARM_HAND_PINKY3", bones.LEFTARM_HAND_PINKY3);
        ParseQuatFrame(time, "LEFTARM_HAND_RING1", bones.LEFTARM_HAND_RING1);
        ParseQuatFrame(time, "LEFTARM_HAND_RING2", bones.LEFTARM_HAND_RING2);
        ParseQuatFrame(time, "LEFTARM_HAND_RING3", bones.LEFTARM_HAND_RING3);
        ParseQuatFrame(time, "LEFTARM_HAND_MIDDLE1", bones.LEFTARM_HAND_MIDDLE1);
        ParseQuatFrame(time, "LEFTARM_HAND_MIDDLE2", bones.LEFTARM_HAND_MIDDLE2);
        ParseQuatFrame(time, "LEFTARM_HAND_MIDDLE3", bones.LEFTARM_HAND_MIDDLE3);
        ParseQuatFrame(time, "LEFTARM_HAND_INDEX1", bones.LEFTARM_HAND_INDEX1);
        ParseQuatFrame(time, "LEFTARM_HAND_INDEX2", bones.LEFTARM_HAND_INDEX2);
        ParseQuatFrame(time, "LEFTARM_HAND_INDEX3", bones.LEFTARM_HAND_INDEX3);
        ParseQuatFrame(time, "LEFTARM_HAND_THUMB1", bones.LEFTARM_HAND_THUMB1);
        ParseQuatFrame(time, "LEFTARM_HAND_THUMB2", bones.LEFTARM_HAND_THUMB2);
        ParseQuatFrame(time, "LEFTARM_HAND_THUMB3", bones.LEFTARM_HAND_THUMB3);

        ParseQuatFrame(time, "RIGHTARM_HAND_PINKY1", bones.RIGHTARM_HAND_PINKY1);
        ParseQuatFrame(time, "RIGHTARM_HAND_PINKY2", bones.RIGHTARM_HAND_PINKY2);
        ParseQuatFrame(time, "RIGHTARM_HAND_PINKY3", bones.RIGHTARM_HAND_PINKY3);
        ParseQuatFrame(time, "RIGHTARM_HAND_RING1", bones.RIGHTARM_HAND_RING1);
        ParseQuatFrame(time, "RIGHTARM_HAND_RING2", bones.RIGHTARM_HAND_RING2);
        ParseQuatFrame(time, "RIGHTARM_HAND_RING3", bones.RIGHTARM_HAND_RING3);
        ParseQuatFrame(time, "RIGHTARM_HAND_MIDDLE1", bones.RIGHTARM_HAND_MIDDLE1);
        ParseQuatFrame(time, "RIGHTARM_HAND_MIDDLE2", bones.RIGHTARM_HAND_MIDDLE2);
        ParseQuatFrame(time, "RIGHTARM_HAND_MIDDLE3", bones.RIGHTARM_HAND_MIDDLE3);
        ParseQuatFrame(time, "RIGHTARM_HAND_INDEX1", bones.RIGHTARM_HAND_INDEX1);
        ParseQuatFrame(time, "RIGHTARM_HAND_INDEX2", bones.RIGHTARM_HAND_INDEX2);
        ParseQuatFrame(time, "RIGHTARM_HAND_INDEX3", bones.RIGHTARM_HAND_INDEX3);
        ParseQuatFrame(time, "RIGHTARM_HAND_THUMB1", bones.RIGHTARM_HAND_THUMB1);
        ParseQuatFrame(time, "RIGHTARM_HAND_THUMB2", bones.RIGHTARM_HAND_THUMB2);
        ParseQuatFrame(time, "RIGHTARM_HAND_THUMB3", bones.RIGHTARM_HAND_THUMB3);



        /*

        public BaseFloat SPIDERMAN_LEFT_X;
        public BaseFloat SPIDERMAN_RIGHT_X;
        public BaseFloat SPIDERMAN_LEFT_Y;
        public BaseFloat SPIDERMAN_RIGHT_Y;
        public BaseFloat SPIDERMAN_LEFT_Z;
        public BaseFloat SPIDERMAN_RIGHT_Z;
        public BaseFloat blow_Fish;
        public BaseFloat viseme_sil;
        public BaseFloat viseme_PP;
        public BaseFloat viseme_FF;
        public BaseFloat viseme_TH;
        public BaseFloat viseme_DD;
        public BaseFloat viseme_kk;
        public BaseFloat viseme_CH;
        public BaseFloat viseme_SS;
        public BaseFloat viseme_nn;
        public BaseFloat viseme_RR;
        public BaseFloat viseme_aa;
        public BaseFloat viseme_E;
        public BaseFloat viseme_I;
        public BaseFloat viseme_O;
        public BaseFloat viseme_U;
        public BaseFloat UserBlendShape1;
        public BaseFloat UserBlendShape2;
        public BaseFloat UserBlendShape3;
        public BaseFloat UserBlendShape4;
        public BaseFloat UserBlendShape5;
        public BaseFloat UserBlendShape6;
        */
    }

    public static void SetBonePaths(GameObject avatarObj, Action<Transform> headCallback)
    {
        foreach (var t in avatarObj.transform.GetComponentsInChildren<Transform>())
        {
            if (!AvatarBoneNames.AvatarBoneNamesMapping.ContainsKey(t.name))
                continue;

            var path = GetTransformPathName(avatarObj.transform, t);
            switch (AvatarBoneNames.AvatarBoneNamesMapping[t.name])
            {
                case AvatarBoneName.HEAD:
                    if (curves.ContainsKey("HEAD"))
                    {
                        foreach (var curve in curves["HEAD"])
                        {
                            curve.path = path;
                        }
                        headCallback(t);
                    }
                    break;
                case AvatarBoneName.NECK:
                    if (curves.ContainsKey("NECK"))
                    {
                        foreach (var curve in curves["NECK"])
                        {
                            curve.path = path;
                        }
                    }
                    break;
                case AvatarBoneName.HIPS:
                    if (curves.ContainsKey("HIPS"))
                    {
                        foreach (var curve in curves["HIPS"])
                        {
                            curve.path = path;
                        }
                    }
                    if (curves.ContainsKey("HIPS_position"))
                    {
                        foreach (var curve in curves["HIPS_position"])
                        {
                            curve.path = path;
                        }
                    }
                    break;
                case AvatarBoneName.SPINE:
                    if (curves.ContainsKey("SPINE"))
                    {
                        foreach (var curve in curves["SPINE"])
                        {
                            curve.path = path;
                        }
                    }
                    break;
                case AvatarBoneName.CHEST:
                    if (curves.ContainsKey("CHEST"))
                    {
                        foreach (var curve in curves["CHEST"])
                        {
                            curve.path = path;
                        }
                    }
                    break;

                case AvatarBoneName.LEFTARM_SHOULDER:
                    if (curves.ContainsKey("LEFTARM_SHOULDER"))
                    {
                        foreach (var curve in curves["LEFTARM_SHOULDER"])
                        {
                            curve.path = path;
                        }
                    }
                    break;
                case AvatarBoneName.LEFTARM_UPPER:
                    if (curves.ContainsKey("LEFTARM_UPPER"))
                    {
                        foreach (var curve in curves["LEFTARM_UPPER"])
                        {
                            curve.path = path;
                        }
                    }
                    break;
                case AvatarBoneName.LEFTARM_LOWER:
                    if (curves.ContainsKey("LEFTARM_LOWER"))
                    {
                        foreach (var curve in curves["LEFTARM_LOWER"])
                        {
                            curve.path = path;
                        }
                    }
                    break;
                case AvatarBoneName.LEFTARM_HAND:
                    if (curves.ContainsKey("LEFTARM_HAND"))
                    {
                        foreach (var curve in curves["LEFTARM_HAND"])
                        {
                            curve.path = path;
                        }
                    }
                    break;

                case AvatarBoneName.RIGHTARM_SHOULDER:
                    if (curves.ContainsKey("RIGHTARM_SHOULDER"))
                    {
                        foreach (var curve in curves["RIGHTARM_SHOULDER"])
                        {
                            curve.path = path;
                        }
                    }
                    break;
                case AvatarBoneName.RIGHTARM_UPPER:
                    if (curves.ContainsKey("RIGHTARM_UPPER"))
                    {
                        foreach (var curve in curves["RIGHTARM_UPPER"])
                        {
                            curve.path = path;
                        }
                    }
                    break;
                case AvatarBoneName.RIGHTARM_LOWER:
                    if (curves.ContainsKey("RIGHTARM_LOWER"))
                    {
                        foreach (var curve in curves["RIGHTARM_LOWER"])
                        {
                            curve.path = path;
                        }
                    }
                    break;
                case AvatarBoneName.RIGHTARM_HAND:
                    if (curves.ContainsKey("RIGHTARM_HAND"))
                    {
                        foreach (var curve in curves["RIGHTARM_HAND"])
                        {
                            curve.path = path;
                        }
                    }
                    break;

                case AvatarBoneName.LEFTLEG_UPPER:
                    if (curves.ContainsKey("LEFTLEG_UPPER"))
                    {
                        foreach (var curve in curves["LEFTLEG_UPPER"])
                        {
                            curve.path = path;
                        }
                    }
                    break;
                case AvatarBoneName.LEFTLEG_LOWER:
                    if (curves.ContainsKey("LEFTLEG_LOWER"))
                    {
                        foreach (var curve in curves["LEFTLEG_LOWER"])
                        {
                            curve.path = path;
                        }
                    }
                    break;
                case AvatarBoneName.LEFTLEG_FOOT:
                    if (curves.ContainsKey("LEFTLEG_FOOT"))
                    {
                        foreach (var curve in curves["LEFTLEG_FOOT"])
                        {
                            curve.path = path;
                        }
                    }
                    break;
                case AvatarBoneName.LEFTLEG_TOES:
                    if (curves.ContainsKey("LEFTLEG_TOES"))
                    {
                        foreach (var curve in curves["LEFTLEG_TOES"])
                        {
                            curve.path = path;
                        }
                    }
                    break;

                case AvatarBoneName.RIGHTLEG_UPPER:
                    if (curves.ContainsKey("RIGHTLEG_UPPER"))
                    {
                        foreach (var curve in curves["RIGHTLEG_UPPER"])
                        {
                            curve.path = path;
                        }
                    }
                    break;
                case AvatarBoneName.RIGHTLEG_LOWER:
                    if (curves.ContainsKey("RIGHTLEG_LOWER"))
                    {
                        foreach (var curve in curves["RIGHTLEG_LOWER"])
                        {
                            curve.path = path;
                        }
                    }
                    break;
                case AvatarBoneName.RIGHTLEG_FOOT:
                    if (curves.ContainsKey("RIGHTLEG_FOOT"))
                    {
                        foreach (var curve in curves["RIGHTLEG_FOOT"])
                        {
                            curve.path = path;
                        }
                    }
                    break;
                case AvatarBoneName.RIGHTLEG_TOES:
                    if (curves.ContainsKey("RIGHTLEG_TOES"))
                    {
                        foreach (var curve in curves["RIGHTLEG_TOES"])
                        {
                            curve.path = path;
                        }
                    }
                    break;

                case AvatarBoneName.LEFTARM_HAND_PINKY1:
                    if (curves.ContainsKey("LEFTARM_HAND_PINKY1"))
                    {
                        foreach (var curve in curves["LEFTARM_HAND_PINKY1"])
                        {
                            curve.path = path;
                        }
                    }
                    break;
                case AvatarBoneName.LEFTARM_HAND_PINKY2:
                    if (curves.ContainsKey("LEFTARM_HAND_PINKY2"))
                    {
                        foreach (var curve in curves["LEFTARM_HAND_PINKY2"])
                        {
                            curve.path = path;
                        }
                    }
                    break;
                case AvatarBoneName.LEFTARM_HAND_PINKY3:
                    if (curves.ContainsKey("LEFTARM_HAND_PINKY3"))
                    {
                        foreach (var curve in curves["LEFTARM_HAND_PINKY3"])
                        {
                            curve.path = path;
                        }
                    }
                    break;
                case AvatarBoneName.LEFTARM_HAND_RING1:
                    if (curves.ContainsKey("LEFTARM_HAND_RING1"))
                    {
                        foreach (var curve in curves["LEFTARM_HAND_RING1"])
                        {
                            curve.path = path;
                        }
                    }
                    break;
                case AvatarBoneName.LEFTARM_HAND_RING2:
                    if (curves.ContainsKey("LEFTARM_HAND_RING2"))
                    {
                        foreach (var curve in curves["LEFTARM_HAND_RING2"])
                        {
                            curve.path = path;
                        }
                    }
                    break;
                case AvatarBoneName.LEFTARM_HAND_RING3:
                    if (curves.ContainsKey("LEFTARM_HAND_RING3"))
                    {
                        foreach (var curve in curves["LEFTARM_HAND_RING3"])
                        {
                            curve.path = path;
                        }
                    }
                    break;
                case AvatarBoneName.LEFTARM_HAND_MIDDLE1:
                    if (curves.ContainsKey("LEFTARM_HAND_MIDDLE1"))
                    {
                        foreach (var curve in curves["LEFTARM_HAND_MIDDLE1"])
                        {
                            curve.path = path;
                        }
                    }
                    break;
                case AvatarBoneName.LEFTARM_HAND_MIDDLE2:
                    if (curves.ContainsKey("LEFTARM_HAND_MIDDLE2"))
                    {
                        foreach (var curve in curves["LEFTARM_HAND_MIDDLE2"])
                        {
                            curve.path = path;
                        }
                    }
                    break;
                case AvatarBoneName.LEFTARM_HAND_MIDDLE3:
                    if (curves.ContainsKey("LEFTARM_HAND_MIDDLE3"))
                    {
                        foreach (var curve in curves["LEFTARM_HAND_MIDDLE3"])
                        {
                            curve.path = path;
                        }
                    }
                    break;
                case AvatarBoneName.LEFTARM_HAND_INDEX1:
                    if (curves.ContainsKey("LEFTARM_HAND_INDEX1"))
                    {
                        foreach (var curve in curves["LEFTARM_HAND_INDEX1"])
                        {
                            curve.path = path;
                        }
                    }
                    break;
                case AvatarBoneName.LEFTARM_HAND_INDEX2:
                    if (curves.ContainsKey("LEFTARM_HAND_INDEX2"))
                    {
                        foreach (var curve in curves["LEFTARM_HAND_INDEX2"])
                        {
                            curve.path = path;
                        }
                    }
                    break;
                case AvatarBoneName.LEFTARM_HAND_INDEX3:
                    if (curves.ContainsKey("LEFTARM_HAND_INDEX3"))
                    {
                        foreach (var curve in curves["LEFTARM_HAND_INDEX3"])
                        {
                            curve.path = path;
                        }
                    }
                    break;
                case AvatarBoneName.LEFTARM_HAND_THUMB1:
                    if (curves.ContainsKey("LEFTARM_HAND_THUMB1"))
                    {
                        foreach (var curve in curves["LEFTARM_HAND_THUMB1"])
                        {
                            curve.path = path;
                        }
                    }
                    break;
                case AvatarBoneName.LEFTARM_HAND_THUMB2:
                    if (curves.ContainsKey("LEFTARM_HAND_THUMB2"))
                    {
                        foreach (var curve in curves["LEFTARM_HAND_THUMB2"])
                        {
                            curve.path = path;
                        }
                    }
                    break;
                case AvatarBoneName.LEFTARM_HAND_THUMB3:
                    if (curves.ContainsKey("LEFTARM_HAND_THUMB3"))
                    {
                        foreach (var curve in curves["LEFTARM_HAND_THUMB3"])
                        {
                            curve.path = path;
                        }
                    }
                    break;

                case AvatarBoneName.RIGHTARM_HAND_PINKY1:
                    if (curves.ContainsKey("RIGHTARM_HAND_PINKY1"))
                    {
                        foreach (var curve in curves["RIGHTARM_HAND_PINKY1"])
                        {
                            curve.path = path;
                        }
                    }
                    break;
                case AvatarBoneName.RIGHTARM_HAND_PINKY2:
                    if (curves.ContainsKey("RIGHTARM_HAND_PINKY2"))
                    {
                        foreach (var curve in curves["RIGHTARM_HAND_PINKY2"])
                        {
                            curve.path = path;
                        }
                    }
                    break;
                case AvatarBoneName.RIGHTARM_HAND_PINKY3:
                    if (curves.ContainsKey("RIGHTARM_HAND_PINKY3"))
                    {
                        foreach (var curve in curves["RIGHTARM_HAND_PINKY3"])
                        {
                            curve.path = path;
                        }
                    }
                    break;
                case AvatarBoneName.RIGHTARM_HAND_RING1:
                    if (curves.ContainsKey("RIGHTARM_HAND_RING1"))
                    {
                        foreach (var curve in curves["RIGHTARM_HAND_RING1"])
                        {
                            curve.path = path;
                        }
                    }
                    break;
                case AvatarBoneName.RIGHTARM_HAND_RING2:
                    if (curves.ContainsKey("RIGHTARM_HAND_RING2"))
                    {
                        foreach (var curve in curves["RIGHTARM_HAND_RING2"])
                        {
                            curve.path = path;
                        }
                    }
                    break;
                case AvatarBoneName.RIGHTARM_HAND_RING3:
                    if (curves.ContainsKey("RIGHTARM_HAND_RING3"))
                    {
                        foreach (var curve in curves["RIGHTARM_HAND_RING3"])
                        {
                            curve.path = path;
                        }
                    }
                    break;
                case AvatarBoneName.RIGHTARM_HAND_MIDDLE1:
                    if (curves.ContainsKey("RIGHTARM_HAND_MIDDLE1"))
                    {
                        foreach (var curve in curves["RIGHTARM_HAND_MIDDLE1"])
                        {
                            curve.path = path;
                        }
                    }
                    break;
                case AvatarBoneName.RIGHTARM_HAND_MIDDLE2:
                    if (curves.ContainsKey("RIGHTARM_HAND_MIDDLE2"))
                    {
                        foreach (var curve in curves["RIGHTARM_HAND_MIDDLE2"])
                        {
                            curve.path = path;
                        }
                    }
                    break;
                case AvatarBoneName.RIGHTARM_HAND_MIDDLE3:

                    if (curves.ContainsKey("RIGHTARM_HAND_MIDDLE3"))
                    {
                        foreach (var curve in curves["RIGHTARM_HAND_MIDDLE3"])
                        {
                            curve.path = path;
                        }
                    }
                    break;
                case AvatarBoneName.RIGHTARM_HAND_INDEX1:
                    if (curves.ContainsKey("RIGHTARM_HAND_INDEX1"))
                    {
                        foreach (var curve in curves["RIGHTARM_HAND_INDEX1"])
                        {
                            curve.path = path;
                        }
                    }
                    break;
                case AvatarBoneName.RIGHTARM_HAND_INDEX2:
                    if (curves.ContainsKey("RIGHTARM_HAND_INDEX2"))
                    {
                        foreach (var curve in curves["RIGHTARM_HAND_INDEX2"])
                        {
                            curve.path = path;
                        }
                    }
                    break;
                case AvatarBoneName.RIGHTARM_HAND_INDEX3:
                    if (curves.ContainsKey("RIGHTARM_HAND_INDEX3"))
                    {
                        foreach (var curve in curves["RIGHTARM_HAND_INDEX3"])
                        {
                            curve.path = path;
                        }
                    }
                    break;
                case AvatarBoneName.RIGHTARM_HAND_THUMB1:
                    if (curves.ContainsKey("RIGHTARM_HAND_THUMB1"))
                    {
                        foreach (var curve in curves["RIGHTARM_HAND_THUMB1"])
                        {
                            curve.path = path;
                        }
                    }
                    break;
                case AvatarBoneName.RIGHTARM_HAND_THUMB2:
                    if (curves.ContainsKey("RIGHTARM_HAND_THUMB2"))
                    {
                        foreach (var curve in curves["RIGHTARM_HAND_THUMB2"])
                        {
                            curve.path = path;
                        }
                    }
                    break;
                case AvatarBoneName.RIGHTARM_HAND_THUMB3:
                    if (curves.ContainsKey("RIGHTARM_HAND_THUMB3"))
                    {
                        foreach (var curve in curves["RIGHTARM_HAND_THUMB3"])
                        {
                            curve.path = path;
                        }
                    }
                    break;
            }
        }
    }
}