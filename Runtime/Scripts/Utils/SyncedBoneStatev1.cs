
using Quat = SQ.Tracking.HalfQuat;
using Vec3 = SQ.Tracking.Vector3;
using BaseFloat = SQ.Tracking.Half;
using System.Runtime.InteropServices;
using System;



[StructLayout(LayoutKind.Sequential)]
    public struct SyncedBoneStatev1
    {
        public Quat HEAD_TRACKING;
        public Vec3 HEAD_TRACKING_position;
        public Quat HEAD;
        public Quat NECK;
        public Quat HIPS;
        public Vec3 HIPS_position;
        public Quat SPINE;
        public Quat CHEST;

        public Quat LEFTARM_SHOULDER;
        public Quat LEFTARM_UPPER;
        public Quat LEFTARM_LOWER;
        public Quat LEFTARM_HAND;

        public Quat RIGHTARM_SHOULDER;
        public Quat RIGHTARM_UPPER;
        public Quat RIGHTARM_LOWER;
        public Quat RIGHTARM_HAND;


        public Quat LEFTLEG_UPPER;
        public Quat LEFTLEG_LOWER;
        public Quat LEFTLEG_FOOT;
        public Quat LEFTLEG_TOES;

        public Quat RIGHTLEG_UPPER;
        public Quat RIGHTLEG_LOWER;
        public Quat RIGHTLEG_FOOT;
        public Quat RIGHTLEG_TOES;


        public Quat LEFTARM_HAND_PINKY1;
        public Quat LEFTARM_HAND_PINKY2;
        public Quat LEFTARM_HAND_PINKY3;
        public Quat LEFTARM_HAND_RING1;
        public Quat LEFTARM_HAND_RING2;
        public Quat LEFTARM_HAND_RING3;
        public Quat LEFTARM_HAND_MIDDLE1;
        public Quat LEFTARM_HAND_MIDDLE2;
        public Quat LEFTARM_HAND_MIDDLE3;
        public Quat LEFTARM_HAND_INDEX1;
        public Quat LEFTARM_HAND_INDEX2;
        public Quat LEFTARM_HAND_INDEX3;
        public Quat LEFTARM_HAND_THUMB1;
        public Quat LEFTARM_HAND_THUMB2;
        public Quat LEFTARM_HAND_THUMB3;

        public Quat RIGHTARM_HAND_PINKY1;
        public Quat RIGHTARM_HAND_PINKY2;
        public Quat RIGHTARM_HAND_PINKY3;
        public Quat RIGHTARM_HAND_RING1;
        public Quat RIGHTARM_HAND_RING2;
        public Quat RIGHTARM_HAND_RING3;
        public Quat RIGHTARM_HAND_MIDDLE1;
        public Quat RIGHTARM_HAND_MIDDLE2;
        public Quat RIGHTARM_HAND_MIDDLE3;
        public Quat RIGHTARM_HAND_INDEX1;
        public Quat RIGHTARM_HAND_INDEX2;
        public Quat RIGHTARM_HAND_INDEX3;
        public Quat RIGHTARM_HAND_THUMB1;
        public Quat RIGHTARM_HAND_THUMB2;
        public Quat RIGHTARM_HAND_THUMB3;
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


        public static T FromBytes<T>(byte[] data) where T : struct
        {
            //byte[] bytes = reader.ReadBytes(Marshal.SizeOf(typeof(T)));

            GCHandle pinned = GCHandle.Alloc(data, GCHandleType.Pinned);
            T st = (T)Marshal.PtrToStructure(pinned.AddrOfPinnedObject(), typeof(T));
            pinned.Free();
            return st;
        }

        public int BytesSize()
        {
            return Marshal.SizeOf(this);
        }

        public void FromBytes(byte[] data, int offset) 
        {
            int size = Marshal.SizeOf(this);
            if (data.Length < size + offset)
            {
                throw new ArgumentException("Invalid byte array length for SyncedBoneStatev1.");
            }

            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.Copy(data, offset, ptr, size);
                 this = (SyncedBoneStatev1)Marshal.PtrToStructure(ptr, typeof(SyncedBoneStatev1));
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        public byte[] ToBytes<T>() where T: struct
        {
            int sz = Marshal.SizeOf<T>();// (this);
            byte[] data = new byte[sz];
            this.ToBytes<T>(data, 0);
            return data;
        }

        public int ToBytes<T>(byte[] data, int startIndex) where T : struct
        {
            int sz = Marshal.SizeOf<T>();// (this);
            if (data.Length < sz)
            {
                throw new ArgumentException("SyncedBoneStatev1 buffer is too small");
            }
            IntPtr ptr = IntPtr.Zero;
            try
            {
                ptr = Marshal.AllocHGlobal(sz);
                Marshal.StructureToPtr(this, ptr, true);
                Marshal.Copy(ptr, data, startIndex, sz);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
            return sz;
        }

    }
