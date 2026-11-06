using System;
using System.Collections.Generic;
using BS.Bridge;
using UnityEngine;

namespace BS.BridgeApi
{
    /// <summary>Unity value types in and out of a call: what BS.Bridge.Core, which knows no engine types, can't do itself.</summary>
    internal static class BridgeApiArgs
    {
        /// <summary>Most objects in one result.</summary>
        public const int MaxRefs = 256;
        /// <summary>A Transform was written since the last physics query: queries sync first, so they see the write.</summary>
        public static bool TransformsMoved;

        public static Vector2 V2(BridgeCallContext c, int i) { ref var v = ref c.Vector(i, ValueTag.Vec2); return new Vector2(v.X, v.Y); }
        public static Vector3 V3(BridgeCallContext c, int i) { ref var v = ref c.Vector(i, ValueTag.Vec3); return new Vector3(v.X, v.Y, v.Z); }
        public static Vector4 V4(BridgeCallContext c, int i) { ref var v = ref c.Vector(i, ValueTag.Vec4); return new Vector4(v.X, v.Y, v.Z, v.W); }

        /// <summary>Normalised: a quaternion is a rotation only at unit length, and one of no length is no rotation at all.</summary>
        public static Quaternion Q(BridgeCallContext c, int i)
        {
            ref var v = ref c.Vector(i, ValueTag.Quat);
            double square = (double)v.X * v.X + (double)v.Y * v.Y + (double)v.Z * v.Z + (double)v.W * v.W;
            if (square < 1e-12)
            {
                c.Fail(ReplyStatus.Type, "argument " + i + ": a quaternion can't be zero");
                return Quaternion.identity;
            }
            float scale = (float)(1 / Math.Sqrt(square));
            return new Quaternion(v.X * scale, v.Y * scale, v.Z * scale, v.W * scale);
        }

        public static Color Col(BridgeCallContext c, int i) { ref var v = ref c.Vector(i, ValueTag.Color); return new Color(v.X, v.Y, v.Z, v.W); }

        public static float[] Floats(BridgeCallContext c, int i) => c.Array_(i, ValueTag.Float)?.Floats;
        public static int[] Ints(BridgeCallContext c, int i) => c.Array_(i, ValueTag.Int)?.Ints;
        public static string[] Strings(BridgeCallContext c, int i) => c.Array_(i, ValueTag.String)?.Strings;

        public static Vector2[] V2s(BridgeCallContext c, int i)
        {
            var a = c.Array_(i, ValueTag.Vec2);
            if (a == null) return null;
            var o = new Vector2[a.Count];
            for (int n = 0; n < o.Length; n++) o[n] = new Vector2(a.Floats[n * 2], a.Floats[n * 2 + 1]);
            return o;
        }

        public static Vector3[] V3s(BridgeCallContext c, int i)
        {
            var a = c.Array_(i, ValueTag.Vec3);
            if (a == null) return null;
            var o = new Vector3[a.Count];
            for (int n = 0; n < o.Length; n++) o[n] = new Vector3(a.Floats[n * 3], a.Floats[n * 3 + 1], a.Floats[n * 3 + 2]);
            return o;
        }

        /// <summary>An array of objects: resolved and scope-checked already; this checks their class.</summary>
        public static T[] Refs<T>(BridgeCallContext c, int i) where T : class
        {
            var a = c.Array_(i, ValueTag.Ref);
            if (a == null) return null;
            var o = new T[a.Count];
            for (int n = 0; n < o.Length; n++)
            {
                o[n] = a.Objects[n] as T;
                if (o[n] == null && a.Objects[n] != null) { c.Fail(ReplyStatus.Type, "argument " + i + "[" + n + "]: expected " + typeof(T).Name); return null; }
            }
            return o;
        }

        public static void Ret(BridgeCallContext c, Vector2 v) => BridgeValues.Vector(c.Out, ValueTag.Vec2, v.x, v.y);
        public static void Ret(BridgeCallContext c, Vector3 v) => BridgeValues.Vector(c.Out, ValueTag.Vec3, v.x, v.y, v.z);
        public static void Ret(BridgeCallContext c, Vector4 v) => BridgeValues.Vector(c.Out, ValueTag.Vec4, v.x, v.y, v.z, v.w);
        public static void Ret(BridgeCallContext c, Quaternion v) => BridgeValues.Vector(c.Out, ValueTag.Quat, v.x, v.y, v.z, v.w);
        public static void Ret(BridgeCallContext c, Color v) => BridgeValues.Vector(c.Out, ValueTag.Color, v.r, v.g, v.b, v.a);

        public static void Ret(BridgeCallContext c, Bounds b)
        {
            BridgeValues.RecordHeader(c.Out, RecordType.Bounds, 2, false);
            Ret(c, b.center);
            Ret(c, b.size);
        }

        public static void Ret(BridgeCallContext c, AnimatorStateInfo s)
        {
            BridgeValues.RecordHeader(c.Out, RecordType.AnimatorState, 6, false);
            BridgeValues.Int(c.Out, s.fullPathHash);
            BridgeValues.Int(c.Out, s.shortNameHash);
            BridgeValues.Float(c.Out, s.normalizedTime);
            BridgeValues.Float(c.Out, s.length);
            BridgeValues.Float(c.Out, s.speed);
            BridgeValues.Bool(c.Out, s.loop);
        }

        public static void Ret(BridgeCallContext c, float[] v)
        {
            BridgeValues.ArrayHeader(c.Out, ValueTag.Float, v.Length);
            foreach (float f in v) c.Out.F32(f);
        }

        public static void Ret(BridgeCallContext c, int[] v)
        {
            BridgeValues.ArrayHeader(c.Out, ValueTag.Int, v.Length);
            foreach (int n in v) c.Out.Varint(BridgeValues.Zigzag(n));
        }

        public static void Ret(BridgeCallContext c, string[] v)
        {
            BridgeValues.ArrayHeader(c.Out, ValueTag.String, v.Length);
            foreach (string s in v) c.Out.Str(s ?? string.Empty);
        }

        public static void Ret(BridgeCallContext c, Vector2[] v)
        {
            BridgeValues.ArrayHeader(c.Out, ValueTag.Vec2, v.Length);
            foreach (var p in v) BridgeValues.ElementVector(c.Out, ValueTag.Vec2, p.x, p.y);
        }

        public static void Ret(BridgeCallContext c, Vector3[] v)
        {
            BridgeValues.ArrayHeader(c.Out, ValueTag.Vec3, v.Length);
            foreach (var p in v) BridgeValues.ElementVector(c.Out, ValueTag.Vec3, p.x, p.y, p.z);
        }
    }
}
