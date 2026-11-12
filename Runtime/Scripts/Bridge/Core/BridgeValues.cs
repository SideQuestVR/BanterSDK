using System;

namespace BS.Bridge
{
    /// <summary>The tag byte in front of every typed value. Mirror of Injection~/src/bridge/values.ts.</summary>
    internal static class ValueTag
    {
        public const byte Null = 0;
        public const byte Bool = 1;
        public const byte Int = 2;
        public const byte Float = 3;
        public const byte Double = 4;
        public const byte String = 5;
        public const byte Vec2 = 6;
        public const byte Vec3 = 7;
        public const byte Vec4 = 8;
        public const byte Quat = 9;
        public const byte Color = 10;
        public const byte Ref = 11;
        public const byte Array = 12;
        /// <summary>Unity to page only.</summary>
        public const byte Record = 13;

        /// <summary>Floats in a vector-like value, or 0.</summary>
        public static int Width(byte tag)
        {
            switch (tag)
            {
                case Vec2: return 2;
                case Vec3: return 3;
                case Vec4: case Quat: case Color: return 4;
                default: return 0;
            }
        }

        public static bool IsElement(byte tag) => tag >= Bool && tag <= Ref || tag == Record;

        /// <summary>The fewest bytes one array element takes, so a count can be refused before anything is allocated.</summary>
        public static int MinElementBytes(byte tag)
        {
            switch (tag)
            {
                case Float: return 4;
                case Double: return 8;
                case Ref: case Record: return 2;
                default: return Math.Max(1, Width(tag) * 4);
            }
        }
    }

    internal static class RecordType
    {
        /// <summary>collider (ref) · point · normal · distance.</summary>
        public const int RaycastHit = 1;
        /// <summary>center · size.</summary>
        public const int Bounds = 2;
        /// <summary>fullPathHash · shortNameHash · normalizedTime · length · speed · loop.</summary>
        public const int AnimatorState = 3;
    }

    /// <summary>A decoded ARRAY. One backing array by element type; vector-like elements are flattened into Floats.</summary>
    internal sealed class BridgeArray
    {
        public byte ElemTag;
        public int Count;
        public int[] Ints;
        public int[] TypeIds;
        public float[] Floats;
        public double[] Doubles;
        public string[] Strings;
        public BridgeValue[] Records;
        /// <summary>REF elements once resolved (null for a null element).</summary>
        public object[] Objects;
    }

    /// <summary>One decoded value. Which fields mean anything depends on Tag.</summary>
    internal struct BridgeValue
    {
        public byte Tag;
        /// <summary>BOOL, INT, a REF's handle, a RECORD's type.</summary>
        public int I;
        /// <summary>A REF's type id.</summary>
        public int TypeId;
        public float X, Y, Z, W;
        /// <summary>FLOAT (also in X, exactly as read) and DOUBLE.</summary>
        public double D;
        public string S;
        public BridgeArray A;
        public BridgeValue[] Fields;
        /// <summary>A REF once resolved.</summary>
        public object Obj;
    }

    /// <summary>Codec of the typed values CALL and REPLY carry. Reading never throws on input.</summary>
    internal static class BridgeValues
    {
        /// <summary>Longest string argument, in UTF-8 bytes.</summary>
        public const int MaxArgString = 64 * 1024;
        public const int MaxArgArray = 16 * 1024;
        public const int MaxRecordFields = 16;
        /// <summary>Largest number in an argument, alone or in a vector: past this nothing in a world means anything.</summary>
        public const float MaxNumber = 1e9f;

        public static uint Zigzag(int v) => (uint)((v << 1) ^ (v >> 31));
        public static int Unzigzag(uint z) => (int)(z >> 1) ^ -(int)(z & 1);

        /// <summary>
        /// Reads one value. args: what a page may send (finite numbers only, argument size limits, no RECORD). Otherwise
        /// everything Unity may send. Returns a ReplyStatus: Ok, Malformed, Type or Limit.
        /// </summary>
        public static byte Read(ref PayloadCursor c, ref BridgeValue v, bool args)
        {
            byte tag = c.U8();
            if (c.Failed) return ReplyStatus.Malformed;
            v = default;
            v.Tag = tag;
            switch (tag)
            {
                case ValueTag.Null:
                    return ReplyStatus.Ok;
                case ValueTag.Bool:
                case ValueTag.Int:
                case ValueTag.Float:
                case ValueTag.Double:
                case ValueTag.String:
                case ValueTag.Vec2:
                case ValueTag.Vec3:
                case ValueTag.Vec4:
                case ValueTag.Quat:
                case ValueTag.Color:
                case ValueTag.Ref:
                    return ReadScalar(ref c, ref v, tag, args, false);
                case ValueTag.Array:
                    return ReadArray(ref c, ref v, args);
                case ValueTag.Record:
                    if (args) return ReplyStatus.Type;
                    return ReadRecord(ref c, ref v);
                default:
                    return ReplyStatus.Malformed;
            }
        }

        static byte ReadScalar(ref PayloadCursor c, ref BridgeValue v, byte tag, bool args, bool element)
        {
            switch (tag)
            {
                case ValueTag.Bool:
                {
                    byte b = c.U8();
                    if (c.Failed || b > 1) return ReplyStatus.Malformed;
                    v.I = b;
                    return ReplyStatus.Ok;
                }
                case ValueTag.Int:
                {
                    uint z = c.Varint();
                    if (c.Failed) return ReplyStatus.Malformed;
                    v.I = Unzigzag(z);
                    return ReplyStatus.Ok;
                }
                case ValueTag.Float:
                {
                    float f = c.F32();
                    if (c.Failed) return ReplyStatus.Malformed;
                    if (args && !Usable(f)) return ReplyStatus.Type;
                    v.X = f;
                    v.D = f;
                    return ReplyStatus.Ok;
                }
                case ValueTag.Double:
                {
                    double d = c.F64();
                    if (c.Failed) return ReplyStatus.Malformed;
                    if (args && !(Math.Abs(d) <= MaxNumber)) return ReplyStatus.Type;
                    v.D = d;
                    return ReplyStatus.Ok;
                }
                case ValueTag.String:
                {
                    uint n = StringLength(c);
                    if (args && n > MaxArgString) return ReplyStatus.Limit;
                    v.S = c.Str(int.MaxValue);
                    return c.Failed ? ReplyStatus.Malformed : ReplyStatus.Ok;
                }
                case ValueTag.Ref:
                {
                    uint h = c.Varint(), t = c.Varint();
                    if (c.Failed || h > int.MaxValue || t > int.MaxValue || (h == 0 && (!element || t != 0))) return ReplyStatus.Malformed;
                    v.I = (int)h;
                    v.TypeId = (int)t;
                    return ReplyStatus.Ok;
                }
                default:
                {
                    int width = ValueTag.Width(tag);
                    v.X = c.F32();
                    v.Y = c.F32();
                    if (width > 2) v.Z = c.F32();
                    if (width > 3) v.W = c.F32();
                    if (c.Failed) return ReplyStatus.Malformed;
                    if (args && !(Usable(v.X) && Usable(v.Y) && Usable(v.Z) && Usable(v.W))) return ReplyStatus.Type;
                    return ReplyStatus.Ok;
                }
            }
        }

        /// <summary>A string's declared length, without moving the cursor (the cursor is a copy).</summary>
        static uint StringLength(PayloadCursor c) => c.Varint();

        static byte ReadArray(ref PayloadCursor c, ref BridgeValue v, bool args)
        {
            byte elem = c.U8();
            uint count = c.Varint();
            if (c.Failed || !ValueTag.IsElement(elem) || (args && elem == ValueTag.Record)) return args && elem == ValueTag.Record ? ReplyStatus.Type : ReplyStatus.Malformed;
            if (args && count > MaxArgArray) return ReplyStatus.Limit;
            if ((long)count * ValueTag.MinElementBytes(elem) > c.Remaining) return ReplyStatus.Malformed;
            int n = (int)count;
            var a = new BridgeArray { ElemTag = elem, Count = n };
            v.A = a;
            var e = default(BridgeValue);
            int width = ValueTag.Width(elem);
            switch (elem)
            {
                case ValueTag.Bool:
                case ValueTag.Int:
                    a.Ints = new int[n];
                    for (int i = 0; i < n; i++)
                    {
                        byte s = ReadScalar(ref c, ref e, elem, args, true);
                        if (s != ReplyStatus.Ok) return s;
                        a.Ints[i] = e.I;
                    }
                    return ReplyStatus.Ok;
                case ValueTag.Float:
                    a.Floats = new float[n];
                    for (int i = 0; i < n; i++)
                    {
                        byte s = ReadScalar(ref c, ref e, elem, args, true);
                        if (s != ReplyStatus.Ok) return s;
                        a.Floats[i] = e.X;
                    }
                    return ReplyStatus.Ok;
                case ValueTag.Double:
                    a.Doubles = new double[n];
                    for (int i = 0; i < n; i++)
                    {
                        byte s = ReadScalar(ref c, ref e, elem, args, true);
                        if (s != ReplyStatus.Ok) return s;
                        a.Doubles[i] = e.D;
                    }
                    return ReplyStatus.Ok;
                case ValueTag.String:
                    a.Strings = new string[n];
                    for (int i = 0; i < n; i++)
                    {
                        byte s = ReadScalar(ref c, ref e, elem, args, true);
                        if (s != ReplyStatus.Ok) return s;
                        a.Strings[i] = e.S;
                    }
                    return ReplyStatus.Ok;
                case ValueTag.Ref:
                    a.Ints = new int[n];
                    a.TypeIds = new int[n];
                    for (int i = 0; i < n; i++)
                    {
                        byte s = ReadScalar(ref c, ref e, elem, args, true);
                        if (s != ReplyStatus.Ok) return s;
                        a.Ints[i] = e.I;
                        a.TypeIds[i] = e.TypeId;
                    }
                    return ReplyStatus.Ok;
                case ValueTag.Record:
                    a.Records = new BridgeValue[n];
                    for (int i = 0; i < n; i++)
                    {
                        a.Records[i].Tag = ValueTag.Record;
                        byte s = ReadRecord(ref c, ref a.Records[i]);
                        if (s != ReplyStatus.Ok) return s;
                    }
                    return ReplyStatus.Ok;
                default:
                    a.Floats = new float[n * width];
                    for (int i = 0; i < n; i++)
                    {
                        byte s = ReadScalar(ref c, ref e, elem, args, true);
                        if (s != ReplyStatus.Ok) return s;
                        int at = i * width;
                        a.Floats[at] = e.X;
                        a.Floats[at + 1] = e.Y;
                        if (width > 2) a.Floats[at + 2] = e.Z;
                        if (width > 3) a.Floats[at + 3] = e.W;
                    }
                    return ReplyStatus.Ok;
            }
        }

        static byte ReadRecord(ref PayloadCursor c, ref BridgeValue v)
        {
            uint type = c.Varint(), n = c.Varint();
            if (c.Failed || type > int.MaxValue || n > MaxRecordFields) return ReplyStatus.Malformed;
            v.I = (int)type;
            v.Fields = new BridgeValue[n];
            for (int i = 0; i < n; i++)
            {
                byte s = Read(ref c, ref v.Fields[i], false);
                if (s != ReplyStatus.Ok) return s;
                if (v.Fields[i].Tag == ValueTag.Array || v.Fields[i].Tag == ValueTag.Record) return ReplyStatus.Malformed;
            }
            return ReplyStatus.Ok;
        }

        static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
        static bool Usable(float f) => Finite(f) && Math.Abs(f) <= MaxNumber;

        // ------------------------------------------------------------------------------------------- writing

        public static void Null(ByteWriter w) => w.U8(ValueTag.Null);
        public static void Bool(ByteWriter w, bool v) { w.U8(ValueTag.Bool); w.U8(v ? 1 : 0); }
        public static void Int(ByteWriter w, int v) { w.U8(ValueTag.Int); w.Varint(Zigzag(v)); }
        public static void Float(ByteWriter w, float v) { w.U8(ValueTag.Float); w.F32(v); }
        public static void Double(ByteWriter w, double v) { w.U8(ValueTag.Double); w.F64(v); }
        public static void String(ByteWriter w, string v) { w.U8(ValueTag.String); w.Str(v ?? string.Empty); }

        /// <summary>VEC2, VEC3, VEC4, QUAT or COLOR.</summary>
        public static void Vector(ByteWriter w, byte tag, float x, float y, float z = 0, float a = 0)
        {
            w.U8(tag);
            VectorBody(w, tag, x, y, z, a);
        }

        static void VectorBody(ByteWriter w, byte tag, float x, float y, float z, float a)
        {
            int width = ValueTag.Width(tag);
            w.F32(x);
            w.F32(y);
            if (width > 2) w.F32(z);
            if (width > 3) w.F32(a);
        }

        public static void Ref(ByteWriter w, int handle, int typeId)
        {
            w.U8(ValueTag.Ref);
            w.Varint((uint)handle);
            w.Varint((uint)typeId);
        }

        /// <summary>Starts an ARRAY: what follows is count elements without tags, written with the Element* methods.</summary>
        public static void ArrayHeader(ByteWriter w, byte elemTag, int count)
        {
            w.U8(ValueTag.Array);
            w.U8(elemTag);
            w.Varint((uint)count);
        }

        public static void ElementRef(ByteWriter w, int handle, int typeId) { w.Varint((uint)handle); w.Varint((uint)typeId); }
        public static void ElementVector(ByteWriter w, byte tag, float x, float y, float z = 0, float a = 0) => VectorBody(w, tag, x, y, z, a);

        /// <summary>A RECORD's start, without its tag when it is an array element: then n values follow.</summary>
        public static void RecordHeader(ByteWriter w, int type, int fields, bool element)
        {
            if (!element) w.U8(ValueTag.Record);
            w.Varint((uint)type);
            w.Varint((uint)fields);
        }

        /// <summary>Any decoded value, byte for byte as it was read.</summary>
        public static void Write(ByteWriter w, in BridgeValue v)
        {
            if (v.Tag == ValueTag.Array) WriteArray(w, v.A);
            else
            {
                w.U8(v.Tag);
                WriteBody(w, v);
            }
        }

        static void WriteBody(ByteWriter w, in BridgeValue v)
        {
            switch (v.Tag)
            {
                case ValueTag.Null: break;
                case ValueTag.Bool: w.U8(v.I); break;
                case ValueTag.Int: w.Varint(Zigzag(v.I)); break;
                case ValueTag.Float: w.F32(v.X); break;
                case ValueTag.Double: w.F64(v.D); break;
                case ValueTag.String: w.Str(v.S); break;
                case ValueTag.Ref: w.Varint((uint)v.I); w.Varint((uint)v.TypeId); break;
                case ValueTag.Record:
                    w.Varint((uint)v.I);
                    w.Varint((uint)v.Fields.Length);
                    foreach (var f in v.Fields) Write(w, f);
                    break;
                default: VectorBody(w, v.Tag, v.X, v.Y, v.Z, v.W); break;
            }
        }

        static void WriteArray(ByteWriter w, BridgeArray a)
        {
            ArrayHeader(w, a.ElemTag, a.Count);
            int width = ValueTag.Width(a.ElemTag);
            for (int i = 0; i < a.Count; i++)
            {
                switch (a.ElemTag)
                {
                    case ValueTag.Bool: w.U8(a.Ints[i]); break;
                    case ValueTag.Int: w.Varint(Zigzag(a.Ints[i])); break;
                    case ValueTag.Float: w.F32(a.Floats[i]); break;
                    case ValueTag.Double: w.F64(a.Doubles[i]); break;
                    case ValueTag.String: w.Str(a.Strings[i]); break;
                    case ValueTag.Ref: ElementRef(w, a.Ints[i], a.TypeIds[i]); break;
                    case ValueTag.Record: WriteBody(w, a.Records[i]); break;
                    default:
                        for (int k = 0; k < width; k++) w.F32(a.Floats[i * width + k]);
                        break;
                }
            }
        }
    }
}
