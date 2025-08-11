using System.Collections.Generic;

namespace Banter.SDK
{
    public enum AvatarBoneName
    {
        HEAD = 0,
        NECK = 1,
        HIPS = 2,
        SPINE = 3,
        CHEST = 4,

        LEFTARM_SHOULDER = 5,
        LEFTARM_UPPER = 6,
        LEFTARM_LOWER = 7,
        LEFTARM_HAND = 8,

        RIGHTARM_SHOULDER = 9,
        RIGHTARM_UPPER = 10,
        RIGHTARM_LOWER = 11,
        RIGHTARM_HAND = 12,

        LEFTLEG_UPPER = 13,
        LEFTLEG_LOWER = 14,
        LEFTLEG_FOOT = 15,
        LEFTLEG_TOES = 16,

        RIGHTLEG_UPPER = 17,
        RIGHTLEG_LOWER = 18,
        RIGHTLEG_FOOT = 19,
        RIGHTLEG_TOES = 20,

        LEFTARM_HAND_PINKY1 = 21,
        LEFTARM_HAND_PINKY2 = 22,
        LEFTARM_HAND_PINKY3 = 23,
        LEFTARM_HAND_RING1 = 24,
        LEFTARM_HAND_RING2 = 25,
        LEFTARM_HAND_RING3 = 26,
        LEFTARM_HAND_MIDDLE1 = 27,
        LEFTARM_HAND_MIDDLE2 = 28,
        LEFTARM_HAND_MIDDLE3 = 29,
        LEFTARM_HAND_INDEX1 = 30,
        LEFTARM_HAND_INDEX2 = 31,
        LEFTARM_HAND_INDEX3 = 32,
        LEFTARM_HAND_THUMB1 = 33,
        LEFTARM_HAND_THUMB2 = 34,
        LEFTARM_HAND_THUMB3 = 35,

        RIGHTARM_HAND_PINKY1 = 36,
        RIGHTARM_HAND_PINKY2 = 37,
        RIGHTARM_HAND_PINKY3 = 38,
        RIGHTARM_HAND_RING1 = 39,
        RIGHTARM_HAND_RING2 = 40,
        RIGHTARM_HAND_RING3 = 41,
        RIGHTARM_HAND_MIDDLE1 = 42,
        RIGHTARM_HAND_MIDDLE2 = 43,
        RIGHTARM_HAND_MIDDLE3 = 44,
        RIGHTARM_HAND_INDEX1 = 45,
        RIGHTARM_HAND_INDEX2 = 46,
        RIGHTARM_HAND_INDEX3 = 47,
        RIGHTARM_HAND_THUMB1 = 48,
        RIGHTARM_HAND_THUMB2 = 49,
        RIGHTARM_HAND_THUMB3 = 50,
    }

    /*
    {"Neck", AvatarBoneName.NECK },
            {"Hips", AvatarBoneName.HIPS },
            {"Spine1", AvatarBoneName.SPINE },
            {"Spine2", AvatarBoneName.CHEST },

            {"LeftShoulder", AvatarBoneName.LEFTARM_SHOULDER },
            {"LeftArm", AvatarBoneName.LEFTARM_UPPER },
            {"LeftForeArm", AvatarBoneName.LEFTARM_LOWER },
            {"LeftHand", AvatarBoneName.LEFTARM_HAND },
            {"LeftUpLeg", AvatarBoneName.LEFTLEG_UPPER },
            {"LeftLeg", AvatarBoneName.LEFTLEG_LOWER },
            {"LeftFoot", AvatarBoneName.LEFTLEG_FOOT },
            {"LeftToeBase", AvatarBoneName.LEFTLEG_TOES },

            {"RightShoulder", AvatarBoneName.RIGHTARM_SHOULDER },
            {"RightArm", AvatarBoneName.RIGHTARM_UPPER },
            {"RightForeArm", AvatarBoneName.RIGHTARM_LOWER },
            {"RightHand", AvatarBoneName.RIGHTARM_HAND },
            {"RightUpLeg", AvatarBoneName.RIGHTLEG_UPPER },
            {"RightLeg", AvatarBoneName.RIGHTLEG_LOWER },
            {"RightFoot", AvatarBoneName.RIGHTLEG_FOOT },
            {"RightToeBase", AvatarBoneName.RIGHTLEG_TOES },
            
            //{"LeftEye", UnityAvatarBoneNames.LEFT_EYE},
            //{"RightEye", UnityAvatarBoneNames.RIGHT_EYE},

            {"LeftHandPinky1", AvatarBoneName.LEFTARM_HAND_PINKY1 },
            {"LeftHandPinky2", AvatarBoneName.LEFTARM_HAND_PINKY2 },
            {"LeftHandPinky3", AvatarBoneName.LEFTARM_HAND_PINKY3 },
            {"LeftHandRing1", AvatarBoneName.LEFTARM_HAND_RING1 },
            {"LeftHandRing2", AvatarBoneName.LEFTARM_HAND_RING2 },
            {"LeftHandRing3", AvatarBoneName.LEFTARM_HAND_RING3 },
            {"LeftHandMiddle1", AvatarBoneName.LEFTARM_HAND_MIDDLE1 },
            {"LeftHandMiddle2", AvatarBoneName.LEFTARM_HAND_MIDDLE2 },
            {"LeftHandMiddle3", AvatarBoneName.LEFTARM_HAND_MIDDLE3 },
            {"LeftHandIndex1", AvatarBoneName.LEFTARM_HAND_INDEX1},
            {"LeftHandIndex2", AvatarBoneName.LEFTARM_HAND_INDEX2 },
            {"LeftHandIndex3", AvatarBoneName.LEFTARM_HAND_INDEX3 },
            {"LeftHandThumb1", AvatarBoneName.LEFTARM_HAND_THUMB1 },
            {"LeftHandThumb2", AvatarBoneName.LEFTARM_HAND_THUMB2 },
            {"LeftHandThumb3", AvatarBoneName.LEFTARM_HAND_THUMB3 },
            {"RightHandPinky1", AvatarBoneName.RIGHTARM_HAND_PINKY1 },
            {"RightHandPinky2", AvatarBoneName.RIGHTARM_HAND_PINKY2 },
            {"RightHandPinky3", AvatarBoneName.RIGHTARM_HAND_PINKY3 },
            {"RightHandRing1", AvatarBoneName.RIGHTARM_HAND_RING1 },
            {"RightHandRing2", AvatarBoneName.RIGHTARM_HAND_RING2 },
            {"RightHandRing3", AvatarBoneName.RIGHTARM_HAND_RING3 },
            {"RightHandMiddle1", AvatarBoneName.RIGHTARM_HAND_MIDDLE1 },
            {"RightHandMiddle2", AvatarBoneName.RIGHTARM_HAND_MIDDLE2 },
            {"RightHandMiddle3", AvatarBoneName.RIGHTARM_HAND_MIDDLE3 },
            {"RightHandIndex1", AvatarBoneName.RIGHTARM_HAND_INDEX1 },
            {"RightHandIndex2", AvatarBoneName.RIGHTARM_HAND_INDEX2 },
            {"RightHandIndex3", AvatarBoneName.RIGHTARM_HAND_INDEX3 },
            {"RightHandThumb1", AvatarBoneName.RIGHTARM_HAND_THUMB1 },
            {"RightHandThumb2", AvatarBoneName.RIGHTARM_HAND_THUMB2 },
            {"RightHandThumb3", AvatarBoneName.RIGHTARM_HAND_THUMB3 },
            */

    public class AvatarBoneStringName
    {
        public const string HEAD = "Head";
        public const string NECK = "Neck";
        public const string HIPS = "Hips";
        public const string SPINE = "Spine1";
        public const string CHEST = "Spine2";
        public const string LEFTARM_SHOULDER = "LeftShoulder";
        public const string LEFTARM_UPPER = "LeftArm";
        public const string LEFTARM_LOWER = "LeftForeArm";
        public const string LEFTARM_HAND = "LeftHand";
        public const string RIGHTARM_SHOULDER = "RightShoulder";
        public const string RIGHTARM_UPPER = "RightArm";
        public const string RIGHTARM_LOWER = "RightForeArm";
        public const string RIGHTARM_HAND = "RightHand";
        public const string LEFTLEG_UPPER = "LeftUpLeg";
        public const string LEFTLEG_LOWER = "LeftLeg";
        public const string LEFTLEG_FOOT = "LeftFoot";
        public const string LEFTLEG_TOES = "LeftToeBase";
        public const string RIGHTLEG_UPPER = "RightUpLeg";
        public const string RIGHTLEG_LOWER = "RightLeg";
        public const string RIGHTLEG_FOOT = "RightFoot";
        public const string RIGHTLEG_TOES = "RightToeBase";
        public const string LEFTARM_HAND_PINKY1 = "LeftHandPinky1";
        public const string LEFTARM_HAND_PINKY2 = "LeftHandPinky2";
        public const string LEFTARM_HAND_PINKY3 = "LeftHandPinky3";
        public const string LEFTARM_HAND_RING1 = "LeftHandRing1";
        public const string LEFTARM_HAND_RING2 = "LeftHandRing2";
        public const string LEFTARM_HAND_RING3 = "LeftHandRing3";
        public const string LEFTARM_HAND_MIDDLE1 = "LeftHandMiddle1";
        public const string LEFTARM_HAND_MIDDLE2 = "LeftHandMiddle2";
        public const string LEFTARM_HAND_MIDDLE3 = "LeftHandMiddle3";
        public const string LEFTARM_HAND_INDEX1 = "LeftHandIndex1";
        public const string LEFTARM_HAND_INDEX2 = "LeftHandIndex2";
        public const string LEFTARM_HAND_INDEX3 = "LeftHandIndex3";
        public const string LEFTARM_HAND_THUMB1 = "LeftHandThumb1";
        public const string LEFTARM_HAND_THUMB2 = "LeftHandThumb2";
        public const string LEFTARM_HAND_THUMB3 = "LeftHandThumb3";
        public const string RIGHTARM_HAND_PINKY1 = "RightHandPinky1";
        public const string RIGHTARM_HAND_PINKY2 = "RightHandPinky2";
        public const string RIGHTARM_HAND_PINKY3 = "RightHandPinky3";
        public const string RIGHTARM_HAND_RING1 = "RightHandRing1";
        public const string RIGHTARM_HAND_RING2 = "RightHandRing2";
        public const string RIGHTARM_HAND_RING3 = "RightHandRing3";
        public const string RIGHTARM_HAND_MIDDLE1 = "RightHandMiddle1";
        public const string RIGHTARM_HAND_MIDDLE2 = "RightHandMiddle2";
        public const string RIGHTARM_HAND_MIDDLE3 = "RightHandMiddle3";
        public const string RIGHTARM_HAND_INDEX1 = "RightHandIndex1";
        public const string RIGHTARM_HAND_INDEX2 = "RightHandIndex2";
        public const string RIGHTARM_HAND_INDEX3 = "RightHandIndex3";
        public const string RIGHTARM_HAND_THUMB1 = "RightHandThumb1";
        public const string RIGHTARM_HAND_THUMB2 = "RightHandThumb2";
        public const string RIGHTARM_HAND_THUMB3 = "RightHandThumb3";

    }

    public class AvatarBoneNames
    {
        public static Dictionary<AvatarBoneName, string> AvatarBoneNamesReverseMapping = new Dictionary<AvatarBoneName, string>()
        {
            {AvatarBoneName.HEAD, AvatarBoneStringName.HEAD },
            {AvatarBoneName.NECK, AvatarBoneStringName.NECK },
            {AvatarBoneName.HIPS, AvatarBoneStringName.HIPS },
            {AvatarBoneName.SPINE, AvatarBoneStringName.SPINE },
            {AvatarBoneName.CHEST, AvatarBoneStringName.CHEST },
            {AvatarBoneName.LEFTARM_SHOULDER, AvatarBoneStringName.LEFTARM_SHOULDER },
            {AvatarBoneName.LEFTARM_UPPER, AvatarBoneStringName.LEFTARM_UPPER },
            {AvatarBoneName.LEFTARM_LOWER, AvatarBoneStringName.LEFTARM_LOWER },
            {AvatarBoneName.LEFTARM_HAND, AvatarBoneStringName.LEFTARM_HAND },
            {AvatarBoneName.LEFTLEG_UPPER, AvatarBoneStringName.LEFTLEG_UPPER },
            {AvatarBoneName.LEFTLEG_LOWER, AvatarBoneStringName.LEFTLEG_LOWER },
            {AvatarBoneName.LEFTLEG_FOOT, AvatarBoneStringName.LEFTLEG_FOOT },
            {AvatarBoneName.LEFTLEG_TOES, AvatarBoneStringName.LEFTLEG_TOES },
            {AvatarBoneName.RIGHTARM_SHOULDER, AvatarBoneStringName.RIGHTARM_SHOULDER },
            {AvatarBoneName.RIGHTARM_UPPER, AvatarBoneStringName.RIGHTARM_UPPER },
            {AvatarBoneName.RIGHTARM_LOWER, AvatarBoneStringName.RIGHTARM_LOWER },
            {AvatarBoneName.RIGHTARM_HAND, AvatarBoneStringName.RIGHTARM_HAND },
            {AvatarBoneName.RIGHTLEG_UPPER, AvatarBoneStringName.RIGHTLEG_UPPER },
            {AvatarBoneName.RIGHTLEG_LOWER, AvatarBoneStringName.RIGHTLEG_LOWER },
            {AvatarBoneName.RIGHTLEG_FOOT, AvatarBoneStringName.RIGHTLEG_FOOT },
            {AvatarBoneName.RIGHTLEG_TOES, AvatarBoneStringName.RIGHTLEG_TOES },
            {AvatarBoneName.LEFTARM_HAND_PINKY1, AvatarBoneStringName.LEFTARM_HAND_PINKY1 },
            {AvatarBoneName.LEFTARM_HAND_PINKY2, AvatarBoneStringName.LEFTARM_HAND_PINKY2 },
            {AvatarBoneName.LEFTARM_HAND_PINKY3, AvatarBoneStringName.LEFTARM_HAND_PINKY3 },
            {AvatarBoneName.LEFTARM_HAND_RING1, AvatarBoneStringName.LEFTARM_HAND_RING1 },
            {AvatarBoneName.LEFTARM_HAND_RING2, AvatarBoneStringName.LEFTARM_HAND_RING2 },
            {AvatarBoneName.LEFTARM_HAND_RING3, AvatarBoneStringName.LEFTARM_HAND_RING3 },
            {AvatarBoneName.LEFTARM_HAND_MIDDLE1, AvatarBoneStringName.LEFTARM_HAND_MIDDLE1 },
            {AvatarBoneName.LEFTARM_HAND_MIDDLE2, AvatarBoneStringName.LEFTARM_HAND_MIDDLE2 },
            {AvatarBoneName.LEFTARM_HAND_MIDDLE3, AvatarBoneStringName.LEFTARM_HAND_MIDDLE3 },
            {AvatarBoneName.LEFTARM_HAND_INDEX1, AvatarBoneStringName.LEFTARM_HAND_INDEX1 },
            {AvatarBoneName.LEFTARM_HAND_INDEX2, AvatarBoneStringName.LEFTARM_HAND_INDEX2 },
            {AvatarBoneName.LEFTARM_HAND_INDEX3, AvatarBoneStringName.LEFTARM_HAND_INDEX3 },
            {AvatarBoneName.LEFTARM_HAND_THUMB1, AvatarBoneStringName.LEFTARM_HAND_THUMB1 },
            {AvatarBoneName.LEFTARM_HAND_THUMB2, AvatarBoneStringName.LEFTARM_HAND_THUMB2 },
            {AvatarBoneName.LEFTARM_HAND_THUMB3, AvatarBoneStringName.LEFTARM_HAND_THUMB3 },
            {AvatarBoneName.RIGHTARM_HAND_PINKY1, AvatarBoneStringName.RIGHTARM_HAND_PINKY1 },
            {AvatarBoneName.RIGHTARM_HAND_PINKY2, AvatarBoneStringName.RIGHTARM_HAND_PINKY2 },
            {AvatarBoneName.RIGHTARM_HAND_PINKY3, AvatarBoneStringName.RIGHTARM_HAND_PINKY3 },
            {AvatarBoneName.RIGHTARM_HAND_RING1, AvatarBoneStringName.RIGHTARM_HAND_RING1 },

            {AvatarBoneName.RIGHTARM_HAND_RING2, AvatarBoneStringName.RIGHTARM_HAND_RING2 },
            {AvatarBoneName.RIGHTARM_HAND_RING3, AvatarBoneStringName.RIGHTARM_HAND_RING3 },
            {AvatarBoneName.RIGHTARM_HAND_MIDDLE1, AvatarBoneStringName.RIGHTARM_HAND_MIDDLE1 },
            {AvatarBoneName.RIGHTARM_HAND_MIDDLE2, AvatarBoneStringName.RIGHTARM_HAND_MIDDLE2 },
            {AvatarBoneName.RIGHTARM_HAND_MIDDLE3, AvatarBoneStringName.RIGHTARM_HAND_MIDDLE3 },
            {AvatarBoneName.RIGHTARM_HAND_INDEX1, AvatarBoneStringName.RIGHTARM_HAND_INDEX1 },

            {AvatarBoneName.RIGHTARM_HAND_INDEX2, AvatarBoneStringName.RIGHTARM_HAND_INDEX2 },
            {AvatarBoneName.RIGHTARM_HAND_INDEX3, AvatarBoneStringName.RIGHTARM_HAND_INDEX3 },
            {AvatarBoneName.RIGHTARM_HAND_THUMB1, AvatarBoneStringName.RIGHTARM_HAND_THUMB1 },
            {AvatarBoneName.RIGHTARM_HAND_THUMB2, AvatarBoneStringName.RIGHTARM_HAND_THUMB2 },
            {AvatarBoneName.RIGHTARM_HAND_THUMB3, AvatarBoneStringName.RIGHTARM_HAND_THUMB3 },
        };
        public static Dictionary<string, AvatarBoneName> AvatarBoneNamesMapping = new Dictionary<string, AvatarBoneName>()
        {
            // Unity
            // {"Hips", AvatarBoneName.HIPS },
            {"Spine", AvatarBoneName.SPINE },
            {"Chest", AvatarBoneName.CHEST },
            // {"UpperChest", AvatarBoneName.S},

            // {"LeftShoulder", AvatarBoneName.LEFTARM_SHOULDER },
            {"LeftUpperArm", AvatarBoneName.LEFTARM_UPPER },
            {"LeftLowerArm", AvatarBoneName.LEFTARM_LOWER },
            // {"LeftHand", AvatarBoneName.LEFTARM_HAND },

            // {"RightShoulder", AvatarBoneName.RIGHTARM_SHOULDER },
            {"RightUpperArm", AvatarBoneName.RIGHTARM_UPPER },
            {"RightLowerArm", AvatarBoneName.RIGHTARM_LOWER },
            // {"RightHand", AvatarBoneName.RIGHTARM_HAND },

            {"LeftUpperLeg", AvatarBoneName.LEFTLEG_UPPER },
            {"LeftLowerLeg", AvatarBoneName.LEFTLEG_LOWER },
            // {"LeftFoot", AvatarBoneName.LEFTLEG_FOOT },
            {"LeftToes", AvatarBoneName.LEFTLEG_TOES },

            {"RightUpperLeg", AvatarBoneName.RIGHTLEG_UPPER },
            {"RightLowerLeg", AvatarBoneName.RIGHTLEG_LOWER },
            // {"RightFoot", AvatarBoneName.RIGHTLEG_FOOT },
            {"RightToes", AvatarBoneName.RIGHTLEG_TOES },

            // {"Neck", AvatarBoneName.NECK },
            {AvatarBoneStringName.HEAD, AvatarBoneName.HEAD },
            //{"LeftEye", AvatarBoneName.HEAD_LEFTEYE },
            //{"RightEye", AvatarBoneName.HEAD_RIGHTEYE },
            //{"Jaw", AvatarBoneName.JAW },

            {"Left Little Proximal", AvatarBoneName.LEFTARM_HAND_PINKY1 },
            {"Left Little Intermediate", AvatarBoneName.LEFTARM_HAND_PINKY2 },
            {"Left Little Distal", AvatarBoneName.LEFTARM_HAND_PINKY3 },
            {"Left Ring Proximal", AvatarBoneName.LEFTARM_HAND_RING1 },
            {"Left Ring Intermediate", AvatarBoneName.LEFTARM_HAND_RING2 },
            {"Left Ring Distal", AvatarBoneName.LEFTARM_HAND_RING3 },
            {"Left Middle Proximal", AvatarBoneName.LEFTARM_HAND_MIDDLE1 },
            {"Left Middle Intermediate", AvatarBoneName.LEFTARM_HAND_MIDDLE2 },
            {"Left Middle Distal", AvatarBoneName.LEFTARM_HAND_MIDDLE3 },
            {"Left Index Proximal", AvatarBoneName.LEFTARM_HAND_INDEX1},
            {"Left Index Intermediate", AvatarBoneName.LEFTARM_HAND_INDEX2 },
            {"Left Index Distal", AvatarBoneName.LEFTARM_HAND_INDEX3 },
            {"Left Thumb Proximal", AvatarBoneName.LEFTARM_HAND_THUMB1 },
            {"Left Thumb Intermediate", AvatarBoneName.LEFTARM_HAND_THUMB2 },
            {"Left Thumb Distal", AvatarBoneName.LEFTARM_HAND_THUMB3 },

            {"Right Little Proximal", AvatarBoneName.RIGHTARM_HAND_PINKY1 },
            {"Right Little Intermediate", AvatarBoneName.RIGHTARM_HAND_PINKY2 },
            {"Right Little Distal", AvatarBoneName.RIGHTARM_HAND_PINKY3 },
            {"Right Ring Proximal", AvatarBoneName.RIGHTARM_HAND_RING1 },
            {"Right Ring Intermediate", AvatarBoneName.RIGHTARM_HAND_RING2 },
            {"Right Ring Distal", AvatarBoneName.RIGHTARM_HAND_RING3 },
            {"Right Middle Proximal", AvatarBoneName.RIGHTARM_HAND_MIDDLE1 },
            {"Right Middle Intermediate", AvatarBoneName.RIGHTARM_HAND_MIDDLE2 },
            {"Right Middle Distal", AvatarBoneName.RIGHTARM_HAND_MIDDLE3 },
            {"Right Index Proximal", AvatarBoneName.RIGHTARM_HAND_INDEX1},
            {"Right Index Intermediate", AvatarBoneName.RIGHTARM_HAND_INDEX2 },
            {"Right Index Distal", AvatarBoneName.RIGHTARM_HAND_INDEX3 },
            {"Right Thumb Proximal", AvatarBoneName.RIGHTARM_HAND_THUMB1 },
            {"Right Thumb Intermediate", AvatarBoneName.RIGHTARM_HAND_THUMB2 },
            {"Right Thumb Distal", AvatarBoneName.RIGHTARM_HAND_THUMB3 },

            //Mixamo
            {"mixamorig:Head", AvatarBoneName.HEAD },
            {"mixamorig:Neck", AvatarBoneName.NECK },
            {"mixamorig:Hips", AvatarBoneName.HIPS },
            {"mixamorig:Spine1", AvatarBoneName.SPINE },
            {"mixamorig:Spine2", AvatarBoneName.CHEST},

            {"mixamorig:LeftShoulder", AvatarBoneName.LEFTARM_SHOULDER },
            {"mixamorig:LeftArm", AvatarBoneName.LEFTARM_UPPER },
            {"mixamorig:LeftForeArm", AvatarBoneName.LEFTARM_LOWER },
            {"mixamorig:LeftHand", AvatarBoneName.LEFTARM_HAND },
            {"mixamorig:LeftUpLeg", AvatarBoneName.LEFTLEG_UPPER },
            {"mixamorig:LeftLeg", AvatarBoneName.LEFTLEG_LOWER },
            {"mixamorig:LeftFoot", AvatarBoneName.LEFTLEG_FOOT },
            {"mixamorig:LeftToeBase", AvatarBoneName.LEFTLEG_TOES },

            {"mixamorig:RightShoulder", AvatarBoneName.RIGHTARM_SHOULDER },
            {"mixamorig:RightArm", AvatarBoneName.RIGHTARM_UPPER },
            {"mixamorig:RightForeArm", AvatarBoneName.RIGHTARM_LOWER },
            {"mixamorig:RightHand", AvatarBoneName.RIGHTARM_HAND },
            {"mixamorig:RightUpLeg", AvatarBoneName.RIGHTLEG_UPPER },
            {"mixamorig:RightLeg", AvatarBoneName.RIGHTLEG_LOWER },
            {"mixamorig:RightFoot", AvatarBoneName.RIGHTLEG_FOOT },
            {"mixamorig:RightToeBase", AvatarBoneName.RIGHTLEG_TOES },

            {"mixamorig:LeftHandPinky1", AvatarBoneName.LEFTARM_HAND_PINKY1 },
            {"mixamorig:LeftHandPinky2", AvatarBoneName.LEFTARM_HAND_PINKY2 },
            {"mixamorig:LeftHandPinky3", AvatarBoneName.LEFTARM_HAND_PINKY3 },
            {"mixamorig:LeftHandRing1", AvatarBoneName.LEFTARM_HAND_RING1 },
            {"mixamorig:LeftHandRing2", AvatarBoneName.LEFTARM_HAND_RING2 },
            {"mixamorig:LeftHandRing3", AvatarBoneName.LEFTARM_HAND_RING3 },
            {"mixamorig:LeftHandMiddle1", AvatarBoneName.LEFTARM_HAND_MIDDLE1 },
            {"mixamorig:LeftHandMiddle2", AvatarBoneName.LEFTARM_HAND_MIDDLE2 },
            {"mixamorig:LeftHandMiddle3", AvatarBoneName.LEFTARM_HAND_MIDDLE3 },
            {"mixamorig:LeftHandIndex1", AvatarBoneName.LEFTARM_HAND_INDEX1},
            {"mixamorig:LeftHandIndex2", AvatarBoneName.LEFTARM_HAND_INDEX2 },
            {"mixamorig:LeftHandIndex3", AvatarBoneName.LEFTARM_HAND_INDEX3 },
            {"mixamorig:LeftHandThumb1", AvatarBoneName.LEFTARM_HAND_THUMB1 },
            {"mixamorig:LeftHandThumb2", AvatarBoneName.LEFTARM_HAND_THUMB2 },
            {"mixamorig:LeftHandThumb3", AvatarBoneName.LEFTARM_HAND_THUMB3 },
            {"mixamorig:RightHandPinky1", AvatarBoneName.RIGHTARM_HAND_PINKY1 },
            {"mixamorig:RightHandPinky2", AvatarBoneName.RIGHTARM_HAND_PINKY2 },
            {"mixamorig:RightHandPinky3", AvatarBoneName.RIGHTARM_HAND_PINKY3 },
            {"mixamorig:RightHandRing1", AvatarBoneName.RIGHTARM_HAND_RING1 },
            {"mixamorig:RightHandRing2", AvatarBoneName.RIGHTARM_HAND_RING2 },
            {"mixamorig:RightHandRing3", AvatarBoneName.RIGHTARM_HAND_RING3 },
            {"mixamorig:RightHandMiddle1", AvatarBoneName.RIGHTARM_HAND_MIDDLE1 },
            {"mixamorig:RightHandMiddle2", AvatarBoneName.RIGHTARM_HAND_MIDDLE2 },
            {"mixamorig:RightHandMiddle3", AvatarBoneName.RIGHTARM_HAND_MIDDLE3 },
            {"mixamorig:RightHandIndex1", AvatarBoneName.RIGHTARM_HAND_INDEX1 },
            {"mixamorig:RightHandIndex2", AvatarBoneName.RIGHTARM_HAND_INDEX2 },
            {"mixamorig:RightHandIndex3", AvatarBoneName.RIGHTARM_HAND_INDEX3 },
            {"mixamorig:RightHandThumb1", AvatarBoneName.RIGHTARM_HAND_THUMB1 },
            {"mixamorig:RightHandThumb2", AvatarBoneName.RIGHTARM_HAND_THUMB2 },
            {"mixamorig:RightHandThumb3", AvatarBoneName.RIGHTARM_HAND_THUMB3 },


            //RPM
            {AvatarBoneStringName.NECK, AvatarBoneName.NECK },
            {AvatarBoneStringName.HIPS, AvatarBoneName.HIPS },
            {AvatarBoneStringName.SPINE, AvatarBoneName.SPINE },
            {AvatarBoneStringName.CHEST, AvatarBoneName.CHEST },

            {AvatarBoneStringName.LEFTARM_SHOULDER, AvatarBoneName.LEFTARM_SHOULDER },
            {AvatarBoneStringName.LEFTARM_UPPER, AvatarBoneName.LEFTARM_UPPER },
            {AvatarBoneStringName.LEFTARM_LOWER, AvatarBoneName.LEFTARM_LOWER },
            {AvatarBoneStringName.LEFTARM_HAND, AvatarBoneName.LEFTARM_HAND },
            {AvatarBoneStringName.LEFTLEG_UPPER, AvatarBoneName.LEFTLEG_UPPER },
            {AvatarBoneStringName.LEFTLEG_LOWER, AvatarBoneName.LEFTLEG_LOWER },
            {AvatarBoneStringName.LEFTLEG_FOOT, AvatarBoneName.LEFTLEG_FOOT },
            {AvatarBoneStringName.LEFTLEG_TOES, AvatarBoneName.LEFTLEG_TOES },

            {AvatarBoneStringName.RIGHTARM_SHOULDER, AvatarBoneName.RIGHTARM_SHOULDER },
            {AvatarBoneStringName.RIGHTARM_UPPER, AvatarBoneName.RIGHTARM_UPPER },
            {AvatarBoneStringName.RIGHTARM_LOWER, AvatarBoneName.RIGHTARM_LOWER },
            {AvatarBoneStringName.RIGHTARM_HAND, AvatarBoneName.RIGHTARM_HAND },
            {AvatarBoneStringName.RIGHTLEG_UPPER, AvatarBoneName.RIGHTLEG_UPPER },
            {AvatarBoneStringName.RIGHTLEG_LOWER, AvatarBoneName.RIGHTLEG_LOWER },
            {AvatarBoneStringName.RIGHTLEG_FOOT, AvatarBoneName.RIGHTLEG_FOOT },
            {AvatarBoneStringName.RIGHTLEG_TOES, AvatarBoneName.RIGHTLEG_TOES },

            //{"LeftEye", UnityAvatarBoneNames.LEFT_EYE},
            //{"RightEye", UnityAvatarBoneNames.RIGHT_EYE},

            {AvatarBoneStringName.LEFTARM_HAND_PINKY1, AvatarBoneName.LEFTARM_HAND_PINKY1 },
            {AvatarBoneStringName.LEFTARM_HAND_PINKY2, AvatarBoneName.LEFTARM_HAND_PINKY2 },
            {AvatarBoneStringName.LEFTARM_HAND_PINKY3, AvatarBoneName.LEFTARM_HAND_PINKY3 },
            {AvatarBoneStringName.LEFTARM_HAND_RING1, AvatarBoneName.LEFTARM_HAND_RING1 },
            {AvatarBoneStringName.LEFTARM_HAND_RING2, AvatarBoneName.LEFTARM_HAND_RING2 },
            {AvatarBoneStringName.LEFTARM_HAND_RING3, AvatarBoneName.LEFTARM_HAND_RING3 },
            {AvatarBoneStringName.LEFTARM_HAND_MIDDLE1, AvatarBoneName.LEFTARM_HAND_MIDDLE1 },
            {AvatarBoneStringName.LEFTARM_HAND_MIDDLE2, AvatarBoneName.LEFTARM_HAND_MIDDLE2 },
            {AvatarBoneStringName.LEFTARM_HAND_MIDDLE3, AvatarBoneName.LEFTARM_HAND_MIDDLE3 },
            {AvatarBoneStringName.LEFTARM_HAND_INDEX1, AvatarBoneName.LEFTARM_HAND_INDEX1 },
            {AvatarBoneStringName.LEFTARM_HAND_INDEX2, AvatarBoneName.LEFTARM_HAND_INDEX2 },
            {AvatarBoneStringName.LEFTARM_HAND_INDEX3, AvatarBoneName.LEFTARM_HAND_INDEX3 },
            {AvatarBoneStringName.LEFTARM_HAND_THUMB1, AvatarBoneName.LEFTARM_HAND_THUMB1 },
            {AvatarBoneStringName.LEFTARM_HAND_THUMB2, AvatarBoneName.LEFTARM_HAND_THUMB2 },
            {AvatarBoneStringName.LEFTARM_HAND_THUMB3, AvatarBoneName.LEFTARM_HAND_THUMB3 },
            {AvatarBoneStringName.RIGHTARM_HAND_PINKY1, AvatarBoneName.RIGHTARM_HAND_PINKY1 },
            {AvatarBoneStringName.RIGHTARM_HAND_PINKY2, AvatarBoneName.RIGHTARM_HAND_PINKY2 },
            {AvatarBoneStringName.RIGHTARM_HAND_PINKY3, AvatarBoneName.RIGHTARM_HAND_PINKY3 },
            {AvatarBoneStringName.RIGHTARM_HAND_RING1, AvatarBoneName.RIGHTARM_HAND_RING1 },
            {AvatarBoneStringName.RIGHTARM_HAND_RING2, AvatarBoneName.RIGHTARM_HAND_RING2 },
            {AvatarBoneStringName.RIGHTARM_HAND_RING3, AvatarBoneName.RIGHTARM_HAND_RING3 },
            {AvatarBoneStringName.RIGHTARM_HAND_MIDDLE1, AvatarBoneName.RIGHTARM_HAND_MIDDLE1 },
            {AvatarBoneStringName.RIGHTARM_HAND_MIDDLE2, AvatarBoneName.RIGHTARM_HAND_MIDDLE2 },
            {AvatarBoneStringName.RIGHTARM_HAND_MIDDLE3, AvatarBoneName.RIGHTARM_HAND_MIDDLE3 },
            {AvatarBoneStringName.RIGHTARM_HAND_INDEX1, AvatarBoneName.RIGHTARM_HAND_INDEX1 },
            {AvatarBoneStringName.RIGHTARM_HAND_INDEX2, AvatarBoneName.RIGHTARM_HAND_INDEX2 },
            {AvatarBoneStringName.RIGHTARM_HAND_INDEX3, AvatarBoneName.RIGHTARM_HAND_INDEX3 },
            {AvatarBoneStringName.RIGHTARM_HAND_THUMB1, AvatarBoneName.RIGHTARM_HAND_THUMB1 },
            {AvatarBoneStringName.RIGHTARM_HAND_THUMB2, AvatarBoneName.RIGHTARM_HAND_THUMB2 },
            {AvatarBoneStringName.RIGHTARM_HAND_THUMB3, AvatarBoneName.RIGHTARM_HAND_THUMB3 },
        };
    }
}