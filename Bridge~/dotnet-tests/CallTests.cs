using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using BS.Bridge;

/// <summary>A stand-in for one object in the world.</summary>
sealed class FakeObject
{
    public float Value;
    public FakeObject(float value = 0) { Value = value; }
}

/// <summary>Handles with the same contract as UnityHandles: scope on every resolve and every mint, dead stays dead.</summary>
sealed class FakeHandles : IBridgeHandles
{
    public readonly Dictionary<int, object> Table = new Dictionary<int, object>();
    public readonly HashSet<int> Dead = new HashSet<int>();
    public readonly HashSet<object> OutOfWorld = new HashSet<object>();
    public int Cleared;
    public int Next = 1000;
    public bool Full;

    public int Add(object o) { int h = ++Next; Table[h] = o; return h; }

    public object Resolve(int handle, out byte status)
    {
        if (!Table.TryGetValue(handle, out var o)) { status = ReplyStatus.UnknownHandle; return null; }
        if (Dead.Contains(handle)) { status = ReplyStatus.Destroyed; return null; }
        if (OutOfWorld.Contains(o)) { status = ReplyStatus.Denied; return null; }
        status = ReplyStatus.Ok;
        return o;
    }

    public int Mint(object obj, out int typeId)
    {
        typeId = 0;
        if (obj == null || OutOfWorld.Contains(obj)) return 0;
        if (Full) return -1;
        foreach (var e in Table) if (ReferenceEquals(e.Value, obj)) { typeId = 7; return e.Key; }
        typeId = 7;
        return Add(obj);
    }

    public void Clear() { Table.Clear(); Cleared++; }
}

/// <summary>What the generated table would hold, over FakeObjects.</summary>
static class FakeApi
{
    public const int Get = 1, Set = 2, Global = 3, Denied = 4, Heavy = 5, Echo = 6, Throws = 7, ReturnsOutsider = 8, Big = 9, Sum = 10,
        Pick = 11, Ranged = 12, Refs = 13, Nullable = 14, Log = 15, Nothing = 16, Secret = 17;

    public static readonly List<string> Effects = new List<string>();
    public static float GlobalValue;
    public static readonly FakeObject Outsider = new FakeObject();
    static readonly int[] Modes = { 0, 1, 2, 5 };

    public static BridgeMember[] Table()
    {
        var t = new BridgeMember[18];
        t[Get] = new BridgeMember(Get, BridgeTier.Read, false, false, 0, "", 1, c => { var s = c.SelfAs<FakeObject>(); if (!c.Failed) c.RetFloat(s.Value); });
        t[Set] = new BridgeMember(Set, BridgeTier.Write, false, false, 1, "f", 1, c =>
        {
            var s = c.SelfAs<FakeObject>();
            float v = c.Float(0);
            if (c.Failed) return;
            s.Value = v;
            Effects.Add("set " + v);
        });
        t[Global] = new BridgeMember(Global, BridgeTier.Global, false, true, 1, "f", 0, c =>
        {
            float v = c.Float(0);
            c.RangeFloat(0, -10, 10);
            if (c.Failed) return;
            float prev = GlobalValue;
            c.SnapshotOnce(Global, () => { GlobalValue = prev; Effects.Add("restored " + prev); });
            GlobalValue = v;
        });
        t[Denied] = new BridgeMember(Denied, BridgeTier.Denied, false, true, 0, "", 0, null);
        t[Heavy] = new BridgeMember(Heavy, BridgeTier.Read, true, true, 1, "i", 0, c => { int i = c.Int(0); if (c.Failed) return; Effects.Add("heavy " + i); c.RetInt(i); });
        t[Echo] = new BridgeMember(Echo, BridgeTier.Read, false, true, 1, "r1", 0, c => { var o = c.Ref<FakeObject>(0); if (!c.Failed) c.RetRef(o); });
        t[Throws] = new BridgeMember(Throws, BridgeTier.Read, false, true, 0, "", 0, c => throw new ArgumentException("boom"));
        t[Secret] = new BridgeMember(Secret, BridgeTier.Read, false, true, 0, "", 0, c => throw new InvalidOperationException("private detail"));
        t[ReturnsOutsider] = new BridgeMember(ReturnsOutsider, BridgeTier.Read, false, true, 0, "", 0, c => c.RetRef(Outsider));
        t[Big] = new BridgeMember(Big, BridgeTier.Read, false, true, 1, "i", 0, c =>
        {
            int n = c.Int(0);
            if (c.Failed) return;
            BridgeValues.ArrayHeader(c.Out, ValueTag.Int, n);
            for (int i = 0; i < n; i++) c.Out.Varint(0);
        });
        t[Sum] = new BridgeMember(Sum, BridgeTier.Read, false, true, 1, "f[]", 0, c =>
        {
            var a = c.Array_(0, ValueTag.Float);
            if (c.Failed) return;
            float s = 0;
            for (int i = 0; i < a.Count; i++) s += a.Floats[i];
            c.RetFloat(s);
        });
        t[Pick] = new BridgeMember(Pick, BridgeTier.Read, false, true, 1, "e0", 0, c => { int m = c.Enum(0, Modes); if (!c.Failed) c.RetInt(m); });
        t[Ranged] = new BridgeMember(Ranged, BridgeTier.Write, false, true, 1, "i", 0, c => { c.Int(0); c.RangeInt(0, 0, 10); if (!c.Failed) Effects.Add("ranged"); });
        t[Refs] = new BridgeMember(Refs, BridgeTier.Read, false, true, 1, "r1[]", 0, c =>
        {
            var a = c.Array_(0, ValueTag.Ref);
            if (c.Failed) return;
            var list = new List<object>();
            for (int i = 0; i < a.Count; i++) list.Add(a.Objects[i]);
            list.Add(Outsider);
            c.RetRefs(list, 2);
        });
        t[Nullable] = new BridgeMember(Nullable, BridgeTier.Read, false, true, 1, "r1", 0, c => { var o = c.Ref<FakeObject>(0, true); if (!c.Failed) c.RetBool(o == null); });
        t[Log] = new BridgeMember(Log, BridgeTier.Write, false, true, 1, "s", 0, c => { var s = c.Str(0); if (!c.Failed) Effects.Add(s); });
        return t;
    }

    public static byte[] Call(uint reqId, int member, int self, params Action<ByteWriter>[] args)
    {
        var w = new ByteWriter();
        w.Varint(reqId); w.Varint((uint)member); w.Varint((uint)self); w.Varint((uint)args.Length);
        foreach (var a in args) a(w);
        return w.ToArray();
    }

    public static Action<ByteWriter> F(float v) => w => BridgeValues.Float(w, v);
    public static Action<ByteWriter> I(int v) => w => BridgeValues.Int(w, v);
    public static Action<ByteWriter> S(string v) => w => BridgeValues.String(w, v);
    public static Action<ByteWriter> R(int h) => w => BridgeValues.Ref(w, h, 0);
    public static readonly Action<ByteWriter> Nil = w => BridgeValues.Null(w);
}

static class CallTests
{
    sealed class Lab
    {
        public readonly FakeHandles Handles = new FakeHandles();
        public int Frame = 1;
        public readonly BridgeCallHost Host;
        public readonly Rig Rig;
        public readonly TestPage Page = new TestPage("doc");
        uint nextReq = 1;

        public Lab(BridgeCallLimits limits = null, int maxQueued = 256, bool hold = false)
        {
            Host = new BridgeCallHost(FakeApi.Table(), Handles, () => Frame, limits);
            Rig = new Rig(new BridgeViewOptions { CallHost = Host, MaxCallsQueued = maxQueued });
            Rig.View.NotifyLoadStarted("https://world.example/space");
            if (!hold) Rig.View.NotifyOnLoad();
            Page.Hello();
            Rig.Pump(Page);
            FakeApi.Effects.Clear();
            FakeApi.GlobalValue = 0;
        }

        public uint Send(int member, int self, params Action<ByteWriter>[] args)
        {
            uint id = nextReq++;
            Page.Link.EnqueueOp(BridgeOps.Encode(BridgeOp.Call, FakeApi.Call(id, member, self, args)));
            return id;
        }

        public uint SendRaw(byte[] payload) { Page.Link.EnqueueOp(BridgeOps.Encode(BridgeOp.Call, payload)); return 0; }

        /// <summary>Sends one call and returns the reply's status.</summary>
        public byte Do(int member, int self, params Action<ByteWriter>[] args)
        {
            uint id = Send(member, self, args);
            Rig.Pump(Page);
            return Status(id);
        }

        public byte Status(uint id)
        {
            var mine = Page.Replies.Where(r => r.reqId == id).ToList();
            T.Eq(1, mine.Count, "replies to request " + id);
            return mine[0].status;
        }

        public string Result(uint id)
        {
            var c = new PayloadCursor(Page.ReplyBodies[id]);
            var v = default(BridgeValue);
            T.Eq(ReplyStatus.Ok, BridgeValues.Read(ref c, ref v, false), "reply body");
            T.True(c.Done, "one value in a reply");
            return Describe(v);
        }

        public string Message(uint id)
        {
            var c = new PayloadCursor(Page.ReplyBodies[id]);
            var s = c.Str(int.MaxValue);
            T.True(c.Done, "one string in an error reply");
            return s;
        }
    }

    static byte[] Hex(JsonElement e) => BridgeHex.FromHex(e.GetString());

    static string Bits(float f) => BitConverter.SingleToInt32Bits(f).ToString("x8");

    /// <summary>One spelling of a value, the same as value-text.ts.</summary>
    public static string Describe(in BridgeValue v) => Body(v);

    static string Body(in BridgeValue v)
    {
        switch (v.Tag)
        {
            case ValueTag.Null: return "null";
            case ValueTag.Bool: return v.I != 0 ? "true" : "false";
            case ValueTag.Int: return "i:" + v.I;
            case ValueTag.Float: return "f:" + Bits(v.X);
            case ValueTag.Double: return "d:" + BitConverter.DoubleToInt64Bits(v.D).ToString("x16");
            case ValueTag.String: return "s:" + BridgeHex.ToHex(BridgeUtf8.Encode(v.S));
            case ValueTag.Ref: return "r:" + v.I + "/" + v.TypeId;
            case ValueTag.Array: return "a" + v.A.ElemTag + ":[" + string.Join(",", Elements(v.A)) + "]";
            case ValueTag.Record: return "rec" + v.I + ":[" + string.Join(",", v.Fields.Select(f => Body(f))) + "]";
            default: return VectorName(v.Tag) + ":[" + string.Join(",", Components(v).Select(Bits)) + "]";
        }
    }

    static IEnumerable<float> Components(BridgeValue v)
    {
        int width = ValueTag.Width(v.Tag);
        yield return v.X;
        yield return v.Y;
        if (width > 2) yield return v.Z;
        if (width > 3) yield return v.W;
    }

    static string VectorName(byte tag) => tag == ValueTag.Vec2 ? "v2" : tag == ValueTag.Vec3 ? "v3" : tag == ValueTag.Vec4 ? "v4" : tag == ValueTag.Quat ? "q" : "c";

    static IEnumerable<string> Elements(BridgeArray a)
    {
        int width = ValueTag.Width(a.ElemTag);
        for (int i = 0; i < a.Count; i++)
        {
            switch (a.ElemTag)
            {
                case ValueTag.Bool: yield return a.Ints[i] != 0 ? "true" : "false"; break;
                case ValueTag.Int: yield return "i:" + a.Ints[i]; break;
                case ValueTag.Float: yield return "f:" + Bits(a.Floats[i]); break;
                case ValueTag.Double: yield return "d:" + BitConverter.DoubleToInt64Bits(a.Doubles[i]).ToString("x16"); break;
                case ValueTag.String: yield return "s:" + BridgeHex.ToHex(BridgeUtf8.Encode(a.Strings[i])); break;
                case ValueTag.Ref: yield return "r:" + a.Ints[i] + "/" + a.TypeIds[i]; break;
                case ValueTag.Record: yield return Body(a.Records[i]); break;
                default:
                    yield return VectorName(a.ElemTag) + ":[" + string.Join(",", a.Floats.Skip(i * width).Take(width).Select(Bits)) + "]";
                    break;
            }
        }
    }

    public static JsonElement V;

    static byte StatusNamed(string name)
    {
        switch (name)
        {
            case "OK": return ReplyStatus.Ok;
            case "TYPE": return ReplyStatus.Type;
            case "LIMIT": return ReplyStatus.Limit;
            case "DENIED": return ReplyStatus.Denied;
            case "DESTROYED": return ReplyStatus.Destroyed;
            default: throw new Exception("status " + name);
        }
    }

    static void Vectors()
    {
        var path = Path.Combine(Path.GetDirectoryName(Source()), "..", "vectors", "unity_call_v1_vectors.json");
        V = JsonDocument.Parse(File.ReadAllText(path)).RootElement;
        Console.WriteLine("typed value vectors");

        T.Run("constants match", () =>
        {
            var c = V.GetProperty("constants");
            T.Eq(BridgeOp.Call, c.GetProperty("call").GetInt32());
            T.Eq(BridgeOp.Reply, c.GetProperty("reply").GetInt32());
            T.Eq(BridgeValues.MaxArgString, c.GetProperty("maxArgString").GetInt32());
            T.Eq(BridgeValues.MaxArgArray, c.GetProperty("maxArgArray").GetInt32());
            T.Eq(BridgeCallContext.MaxArgs, c.GetProperty("maxArgs").GetInt32());
            T.Eq(BridgeValues.MaxNumber, c.GetProperty("maxNumber").GetSingle());
        });

        T.Run("values decode, describe and re-encode, as arguments and as results", () =>
        {
            foreach (var x in V.GetProperty("values").EnumerateArray())
                foreach (bool args in new[] { true, false })
                {
                    var b = Hex(x.GetProperty("hex"));
                    var c = new PayloadCursor(b);
                    var v = default(BridgeValue);
                    string name = x.GetProperty("name").GetString();
                    T.Eq(ReplyStatus.Ok, BridgeValues.Read(ref c, ref v, args), name);
                    T.True(c.Done, name + " consumed");
                    T.Eq(x.GetProperty("text").GetString(), Describe(v), name);
                    var w = new ByteWriter();
                    BridgeValues.Write(w, v);
                    T.Eq(x.GetProperty("hex").GetString(), BridgeHex.ToHex(w.Span), name);
                }
        });

        T.Run("results decode and re-encode", () =>
        {
            foreach (var x in V.GetProperty("results").EnumerateArray())
            {
                var b = Hex(x.GetProperty("hex"));
                var c = new PayloadCursor(b);
                var v = default(BridgeValue);
                string name = x.GetProperty("name").GetString();
                T.Eq(ReplyStatus.Ok, BridgeValues.Read(ref c, ref v, false), name);
                T.True(c.Done, name);
                T.Eq(x.GetProperty("text").GetString(), Describe(v), name);
                var w = new ByteWriter();
                BridgeValues.Write(w, v);
                T.Eq(x.GetProperty("hex").GetString(), BridgeHex.ToHex(w.Span), name);
            }
        });

        T.Run("refused as arguments, with the vector's status", () =>
        {
            foreach (var x in V.GetProperty("badArgs").EnumerateArray())
            {
                var b = Hex(x.GetProperty("hex"));
                var c = new PayloadCursor(b);
                var v = default(BridgeValue);
                T.Eq(StatusNamed(x.GetProperty("status").GetString()), BridgeValues.Read(ref c, ref v, true), x.GetProperty("name").GetString());
            }
        });

        T.Run("refused both ways", () =>
        {
            foreach (var x in V.GetProperty("badValues").EnumerateArray())
                foreach (bool args in new[] { true, false })
                {
                    var b = Hex(x.GetProperty("hex"));
                    var c = new PayloadCursor(b);
                    var v = default(BridgeValue);
                    byte s = BridgeValues.Read(ref c, ref v, args);
                    T.True(s != ReplyStatus.Ok || !c.Done, "accepted: " + x.GetProperty("name").GetString());
                }
        });

        T.Run("calls and replies decode", () =>
        {
            foreach (var x in V.GetProperty("calls").EnumerateArray())
            {
                var b = Hex(x.GetProperty("hex"));
                var c = new PayloadCursor(b);
                T.Eq(x.GetProperty("reqId").GetUInt32(), c.Varint());
                T.Eq(x.GetProperty("member").GetUInt32(), c.Varint());
                T.Eq(x.GetProperty("self").GetUInt32(), c.Varint());
                uint n = c.Varint();
                var want = x.GetProperty("args").EnumerateArray().Select(a => a.GetString()).ToList();
                T.Eq((uint)want.Count, n);
                for (int i = 0; i < n; i++)
                {
                    var v = default(BridgeValue);
                    T.Eq(ReplyStatus.Ok, BridgeValues.Read(ref c, ref v, true));
                    T.Eq(want[i], Describe(v));
                }
                T.True(c.Done);
                T.Eq(x.GetProperty("op").GetString(), BridgeHex.ToHex(BridgeOps.Encode(BridgeOp.Call, b)));
            }
            foreach (var x in V.GetProperty("replies").EnumerateArray())
            {
                T.True(ReplyPayload.TryDecode(Hex(x.GetProperty("hex")), out uint id, out byte status, out _));
                T.Eq(x.GetProperty("reqId").GetUInt32(), id);
                T.Eq(StatusNamed(x.GetProperty("status").GetString()), status);
            }
        });
    }

    static string Source([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

    public static void Run()
    {
        Vectors();
        Console.WriteLine("typed calls");

        T.Run("no host: WELCOME grants nothing and a CALL is answered UNSUPPORTED, unrun", () =>
        {
            var rig = new Rig();
            rig.View.NotifyLoadStarted("https://world.example/space");
            rig.View.NotifyOnLoad();
            var page = new TestPage("doc");
            page.Hello();
            rig.Pump(page);
            T.Eq(0u, page.Families);
            T.Eq(0u, page.Caps);
            page.Link.EnqueueOp(BridgeOps.Encode(BridgeOp.Call, FakeApi.Call(5, FakeApi.Get, 1)));
            rig.Pump(page);
            T.Eq(ReplyStatus.Unsupported, page.Replies.Single(r => r.reqId == 5).status);
        });

        T.Run("a host: WELCOME grants the family and the tiers", () =>
        {
            var lab = new Lab();
            T.Eq(BridgeFamily.Call, lab.Page.Families);
            T.Eq(BridgeCap.Read | BridgeCap.Write | BridgeCap.Global, lab.Page.Caps);
        });

        T.Run("one reply, after the effect", () =>
        {
            var lab = new Lab();
            var o = new FakeObject();
            int h = lab.Handles.Add(o);
            uint set = lab.Send(FakeApi.Set, h, FakeApi.F(4.5f));
            uint get = lab.Send(FakeApi.Get, h);
            lab.Rig.Pump(lab.Page);
            T.Eq(ReplyStatus.Ok, lab.Status(set));
            T.Eq("null", lab.Result(set));
            T.Eq(ReplyStatus.Ok, lab.Status(get));
            T.Eq("f:" + Bits(4.5f), lab.Result(get));
            T.Eq(4.5f, o.Value);
        });

        T.Run("tiers: T3 and unknown members are refused, and a missing capability refuses its tier", () =>
        {
            var lab = new Lab();
            int h = lab.Handles.Add(new FakeObject());
            T.Eq(ReplyStatus.Denied, lab.Do(FakeApi.Denied, 0));
            T.Eq(ReplyStatus.UnknownMember, lab.Do(999, 0));
            T.Eq(ReplyStatus.UnknownMember, lab.Do(FakeApi.Nothing, 0));
            lab.Host.Caps = BridgeCap.Read;
            T.Eq(ReplyStatus.Ok, lab.Do(FakeApi.Get, h));
            T.Eq(ReplyStatus.Denied, lab.Do(FakeApi.Set, h, FakeApi.F(1)));
            T.Eq(ReplyStatus.Denied, lab.Do(FakeApi.Global, 0, FakeApi.F(1)));
            T.Eq(0, FakeApi.Effects.Count, "nothing ran");
            lab.Host.Caps = 0;
            T.Eq(ReplyStatus.Denied, lab.Do(FakeApi.Get, h));
        });

        T.Run("scope: a target or a ref argument outside the world is refused, on every call", () =>
        {
            var lab = new Lab();
            var inside = new FakeObject(1);
            var leaves = new FakeObject(2);
            int hi = lab.Handles.Add(inside), hl = lab.Handles.Add(leaves);
            T.Eq(ReplyStatus.Ok, lab.Do(FakeApi.Set, hl, FakeApi.F(3)));
            lab.Handles.OutOfWorld.Add(leaves);
            T.Eq(ReplyStatus.Denied, lab.Do(FakeApi.Set, hl, FakeApi.F(9)));
            T.Eq(3f, leaves.Value, "unchanged");
            T.Eq(ReplyStatus.Denied, lab.Do(FakeApi.Echo, 0, FakeApi.R(hl)));
            T.Eq(ReplyStatus.Ok, lab.Do(FakeApi.Echo, 0, FakeApi.R(hi)));
            uint id = lab.Send(FakeApi.Refs, 0, w => { BridgeValues.ArrayHeader(w, ValueTag.Ref, 2); BridgeValues.ElementRef(w, hi, 0); BridgeValues.ElementRef(w, hl, 0); });
            lab.Rig.Pump(lab.Page);
            T.Eq(ReplyStatus.Denied, lab.Status(id));
            StringAssert(lab.Message(id), "argument 0[1]");
        });

        T.Run("scope: what a thunk returns goes through the filter", () =>
        {
            var lab = new Lab();
            lab.Handles.OutOfWorld.Add(FakeApi.Outsider);
            uint one = lab.Send(FakeApi.ReturnsOutsider, 0);
            int hi = lab.Handles.Add(new FakeObject());
            uint many = lab.Send(FakeApi.Refs, 0, w => { BridgeValues.ArrayHeader(w, ValueTag.Ref, 2); BridgeValues.ElementRef(w, hi, 0); BridgeValues.ElementRef(w, 0, 0); });
            lab.Rig.Pump(lab.Page);
            T.Eq("null", lab.Result(one));
            T.Eq("a11:[r:" + hi + "/7]", lab.Result(many), "the outsider is left out, null elements too");
        });

        T.Run("a dead handle is DESTROYED, an unknown one UNKNOWN_HANDLE, each with a message", () =>
        {
            var lab = new Lab();
            int h = lab.Handles.Add(new FakeObject());
            lab.Handles.Dead.Add(h);
            uint dead = lab.Send(FakeApi.Get, h);
            uint unknown = lab.Send(FakeApi.Get, 424242);
            uint deadArg = lab.Send(FakeApi.Echo, 0, FakeApi.R(h));
            lab.Rig.Pump(lab.Page);
            T.Eq(ReplyStatus.Destroyed, lab.Status(dead));
            StringAssert(lab.Message(dead), "destroyed");
            T.Eq(ReplyStatus.UnknownHandle, lab.Status(unknown));
            T.Eq(ReplyStatus.Destroyed, lab.Status(deadArg));
        });

        T.Run("bad arguments: each refused before the thunk, and the stream carries on", () =>
        {
            var lab = new Lab();
            int h = lab.Handles.Add(new FakeObject());
            T.Eq(ReplyStatus.Type, lab.Do(FakeApi.Set, h), "too few");
            T.Eq(ReplyStatus.Type, lab.Do(FakeApi.Set, h, FakeApi.F(1), FakeApi.F(2)), "too many");
            T.Eq(ReplyStatus.Type, lab.Do(FakeApi.Set, h, FakeApi.S("x")), "wrong tag");
            T.Eq(ReplyStatus.Type, lab.Do(FakeApi.Set, h, FakeApi.F(float.NaN)), "NaN");
            T.Eq(ReplyStatus.Type, lab.Do(FakeApi.Set, h, FakeApi.F(float.PositiveInfinity)), "infinity");
            T.Eq(ReplyStatus.Type, lab.Do(FakeApi.Set, 0, FakeApi.F(1)), "no target");
            T.Eq(ReplyStatus.Type, lab.Do(FakeApi.Global, h, FakeApi.F(1)), "a static with a target");
            T.Eq(ReplyStatus.Type, lab.Do(FakeApi.Echo, 0, FakeApi.Nil), "null where a ref is needed");
            T.Eq(ReplyStatus.Ok, lab.Do(FakeApi.Nullable, 0, FakeApi.Nil), "null where it may be");
            T.Eq(ReplyStatus.Type, lab.Do(FakeApi.Pick, 0, FakeApi.I(3)), "not a value of the enum");
            T.Eq(ReplyStatus.Ok, lab.Do(FakeApi.Pick, 0, FakeApi.I(5)));
            T.Eq(ReplyStatus.Denied, lab.Do(FakeApi.Ranged, 0, FakeApi.I(11)), "out of range");
            T.Eq(ReplyStatus.Ok, lab.Do(FakeApi.Ranged, 0, FakeApi.I(10)));

            uint id = 500;
            var trailing = FakeApi.Call(id, FakeApi.Get, h).Concat(new byte[] { 0 }).ToArray();
            lab.SendRaw(trailing);
            lab.SendRaw(FakeApi.Call(501, FakeApi.Set, h, w => w.U8(99)));
            lab.Rig.Pump(lab.Page);
            T.Eq(ReplyStatus.Malformed, lab.Status(500), "bytes after the last argument");
            T.Eq(ReplyStatus.Malformed, lab.Status(501), "unknown tag");
            T.Eq(ReplyStatus.Ok, lab.Do(FakeApi.Get, h), "still working");
            T.Eq(new[] { "ranged" }.Length, FakeApi.Effects.Count, "only the valid write ran");
        });

        T.Run("more arguments than a member takes are refused before any is read", () =>
        {
            var lab = new Lab();
            var w = new ByteWriter();
            w.Varint(9); w.Varint(FakeApi.Get); w.Varint(1); w.Varint(uint.MaxValue);
            lab.SendRaw(w.ToArray());
            lab.Rig.Pump(lab.Page);
            T.Eq(ReplyStatus.Type, lab.Status(9));
        });

        T.Run("a call with no readable request id is dropped; nothing else stops", () =>
        {
            var lab = new Lab();
            int h = lab.Handles.Add(new FakeObject());
            lab.SendRaw(new byte[] { 0xff });
            lab.SendRaw(new byte[0]);
            lab.SendRaw(new byte[] { 0 });
            T.Eq(ReplyStatus.Ok, lab.Do(FakeApi.Get, h));
            T.Eq(1, lab.Page.Replies.Count);
        });

        T.Run("a thunk that throws answers INTERNAL with its message", () =>
        {
            var lab = new Lab();
            uint id = lab.Send(FakeApi.Throws, 0);
            uint next = lab.Send(FakeApi.Heavy, 0, FakeApi.I(1));
            lab.Rig.Pump(lab.Page);
            T.Eq(ReplyStatus.Internal, lab.Status(id));
            StringAssert(lab.Message(id), "boom");
            T.Eq(ReplyStatus.Ok, lab.Status(next));
            T.Eq(1L, lab.Host.Failures);
        });

        T.Run("a page that keeps a thunk throwing fills no log: a few per world, the rest only counted", () =>
        {
            var lab = new Lab();
            int logged = 0;
            lab.Host.Log = m => logged++;
            for (int i = 0; i < 20; i++) lab.Send(FakeApi.Throws, 0);
            lab.Rig.Pump(lab.Page);
            T.Eq(8, logged);
            T.Eq(20L, lab.Host.Failures);
            lab.Rig.View.NotifyLoadStarted("https://next.example/");
            lab.Host.WorldUnloaded();
            var again = new Lab();
            T.Eq(ReplyStatus.Internal, again.Do(FakeApi.Throws, 0), "each one still gets its answer");
        });

        T.Run("an exception's message reaches the page only when it is one of Unity's rule-breaking kinds", () =>
        {
            var lab = new Lab();
            uint id = lab.Send(FakeApi.Secret, 0);
            lab.Rig.Pump(lab.Page);
            T.Eq(ReplyStatus.Internal, lab.Status(id));
            T.Eq("InvalidOperationException", lab.Message(id));
        });

        T.Run("past the hard cap a typed reply shrinks to LIMIT, and the queue closes the session whatever the page acknowledges", () =>
        {
            var lab = new Lab();
            lab.Rig.View.Options.MaxOutboundHardBytes = 1000;
            lab.Rig.View.Options.MaxOutboundCloseBytes = 5000;
            lab.Rig.View.Options.FlushMaxFrames = 0;
            uint first = 0;
            for (int round = 0; round < 400 && lab.Rig.View.Current != null; round++)
            {
                uint id = lab.Send(FakeApi.Big, 0, FakeApi.I(2000));
                if (first == 0) first = id;
                lab.Page.Link.SendWindow = int.MaxValue;
                lab.Page.Flush();
                foreach (var s in lab.Page.TakeOut()) lab.Rig.View.Receive(s);
                lab.Rig.View.Drain();
                lab.Rig.View.Flush();
                T.True(lab.Rig.View.Current == null || lab.Rig.View.Current.Link.QueuedCost < 8000, "the queue stays near its limit");
            }
            T.True(lab.Rig.View.Current == null, "the session closed on queued bytes");
            T.True(lab.Rig.View.Stats.OutboundCloses > 0);
        });

        T.Run("a flood of calls past the limit closes the session inside one message", () =>
        {
            var lab = new Lab(maxQueued: 4);
            lab.Rig.View.Options.MaxCallsOverflowed = 50;
            for (int i = 0; i < 500; i++) lab.Send(FakeApi.Log, 0, FakeApi.S("x"));
            lab.Page.Link.SendWindow = int.MaxValue;
            lab.Page.Flush();
            foreach (var s in lab.Page.TakeOut()) lab.Rig.View.Receive(s);
            lab.Rig.View.Drain();
            T.True(lab.Rig.View.Current == null, "closed");
            T.True(lab.Host.Refused <= 60, "it stopped there: " + lab.Host.Refused);
        });

        T.Run("calls running in between keep a burst from counting as a flood", () =>
        {
            var lab = new Lab(maxQueued: 4);
            lab.Rig.View.Options.MaxCallsOverflowed = 2;
            for (int round = 0; round < 20; round++)
            {
                for (int i = 0; i < 6; i++) lab.Send(FakeApi.Log, 0, FakeApi.S("x"));
                lab.Rig.Pump(lab.Page);
            }
            T.True(lab.Rig.View.Current != null, "still open");
        });

        T.Run("a client can turn tiers and members off, read on every call, and WELCOME says so", () =>
        {
            var lab = new Lab();
            int h = lab.Handles.Add(new FakeObject());
            uint limit = BridgeCap.Read;
            bool off = false;
            lab.Host.CapsLimit = () => limit;
            lab.Host.MemberOff = id => off && id == FakeApi.Get;
            T.Eq(ReplyStatus.Ok, lab.Do(FakeApi.Get, h));
            T.Eq(ReplyStatus.Denied, lab.Do(FakeApi.Set, h, FakeApi.F(1)));
            off = true;
            T.Eq(ReplyStatus.Denied, lab.Do(FakeApi.Get, h));
            T.Eq(BridgeFamily.Call, lab.Host.Families);
            limit = 0;
            T.Eq(0u, lab.Host.Families);

            var rig = new Rig(new BridgeViewOptions { CallHost = lab.Host });
            rig.View.NotifyLoadStarted("https://next.example/");
            rig.View.NotifyOnLoad();
            var page = new TestPage("next");
            page.Hello();
            rig.Pump(page);
            T.Eq(0u, page.Families);
            T.Eq(0u, page.Caps);
        });

        T.Run("a client's reset wins: restoring first and resetting after leaves the reset's value", () =>
        {
            var lab = new Lab();
            FakeApi.GlobalValue = 1234;
            T.Eq(ReplyStatus.Ok, lab.Do(FakeApi.Global, 0, FakeApi.F(1)));
            lab.Host.RestoreGlobals();   // the top of ResetScene
            T.Eq(1234f, FakeApi.GlobalValue);
            FakeApi.GlobalValue = 0;     // OnSceneReset
            lab.Rig.View.NotifyLoadStarted("https://next.example/");
            T.Eq(0f, FakeApi.GlobalValue, "nothing is left to bring back");
        });

        T.Run("a new session starts from the client's globals, whoever wrote after the load started", () =>
        {
            var lab = new Lab();
            var v = lab.Rig.View;
            v.NotifyLoadStarted("https://b.example/");
            v.NotifyOnLoad();
            var late = new TestPage("a-fresh-token", "https://a.example/");
            late.Hello();
            lab.Rig.Pump(late);
            late.Link.EnqueueOp(BridgeOps.Encode(BridgeOp.Call, FakeApi.Call(1, FakeApi.Global, 0, FakeApi.F(7))));
            lab.Rig.Pump(late);
            T.Eq(7f, FakeApi.GlobalValue);
            var next = new TestPage("the-real-document", "https://b.example/");
            next.Hello();
            lab.Rig.Pump(next);
            T.Eq(0f, FakeApi.GlobalValue);
        });

        T.Run("an about: load unloads the world too", () =>
        {
            var lab = new Lab();
            T.Eq(ReplyStatus.Ok, lab.Do(FakeApi.Global, 0, FakeApi.F(3)));
            int before = lab.Handles.Cleared;
            lab.Rig.View.NotifyLoadStarted("about:blank");
            T.Eq(0f, FakeApi.GlobalValue);
            T.Eq(before + 1, lab.Handles.Cleared);
        });

        T.Run("a result past the cap is LIMIT, and a call past the cap never runs", () =>
        {
            var lab = new Lab(new BridgeCallLimits { MaxResultBytes = 1000, MaxCallBytes = 200 });
            uint ok = lab.Send(FakeApi.Big, 0, FakeApi.I(100));
            uint big = lab.Send(FakeApi.Big, 0, FakeApi.I(5000));
            uint huge = lab.Send(FakeApi.Log, 0, FakeApi.S(new string('x', 500)));
            lab.Rig.Pump(lab.Page);
            T.Eq(ReplyStatus.Ok, lab.Status(ok));
            T.Eq(ReplyStatus.Limit, lab.Status(big));
            T.Eq(ReplyStatus.Limit, lab.Status(huge));
            T.Eq(0, FakeApi.Effects.Count);
        });

        T.Run("a full handle table fails the call with LIMIT", () =>
        {
            var lab = new Lab();
            lab.Handles.Full = true;
            T.Eq(ReplyStatus.Limit, lab.Do(FakeApi.Echo, 0, FakeApi.R(lab.Handles.Add(new FakeObject()))));
        });

        T.Run("calls past the queue limit are answered LIMIT, unrun, and every call gets exactly one reply", () =>
        {
            var lab = new Lab(maxQueued: 8);
            var ids = new List<uint>();
            for (int i = 0; i < 20; i++) ids.Add(lab.Send(FakeApi.Log, 0, FakeApi.S("c" + i)));
            lab.Page.Flush();
            foreach (var s in lab.Page.TakeOut()) lab.Rig.View.Receive(s);
            lab.Rig.View.Drain(1); // one ingest: nothing has run yet
            lab.Rig.Pump(lab.Page);
            var statuses = ids.Select(lab.Status).ToList();
            T.Eq(8, statuses.Count(s => s == ReplyStatus.Ok));
            T.Eq(12, statuses.Count(s => s == ReplyStatus.Limit));
            T.Seq(Enumerable.Range(0, 8).Select(i => "c" + i), FakeApi.Effects, "the first ones, in order");
            T.Eq(0, lab.Rig.View.Current.CallsQueued);
        });

        T.Run("heavy calls: at most the allowance per frame; the rest wait in order with what follows them", () =>
        {
            var lab = new Lab(new BridgeCallLimits { MaxHeavyPerFrame = 2 });
            for (int i = 0; i < 5; i++) lab.Send(FakeApi.Heavy, 0, FakeApi.I(i));
            lab.Send(FakeApi.Log, 0, FakeApi.S("light"));
            lab.Page.Send("!after!");
            lab.Page.Flush();
            foreach (var s in lab.Page.TakeOut()) lab.Rig.View.Receive(s);
            lab.Rig.View.Drain();
            T.Seq(new[] { "heavy 0", "heavy 1" }, FakeApi.Effects, "frame 1");
            lab.Rig.View.Drain();
            T.Eq(2, FakeApi.Effects.Count, "the same frame: no more");
            lab.Frame++;
            lab.Rig.View.Drain();
            T.Seq(new[] { "heavy 0", "heavy 1", "heavy 2", "heavy 3" }, FakeApi.Effects, "frame 2");
            T.Eq(0, lab.Rig.Disp.Ran.Count, "text stays behind the calls ahead of it");
            lab.Frame++;
            lab.Rig.View.Drain();
            T.Seq(new[] { "heavy 0", "heavy 1", "heavy 2", "heavy 3", "heavy 4", "light" }, FakeApi.Effects, "frame 3");
            T.Eq(1, lab.Rig.Disp.Ran.Count);
        });

        T.Run("T2: restored at world unload, once per member, to the value before the first write", () =>
        {
            var lab = new Lab();
            FakeApi.GlobalValue = 1;
            T.Eq(ReplyStatus.Ok, lab.Do(FakeApi.Global, 0, FakeApi.F(5)));
            T.Eq(ReplyStatus.Ok, lab.Do(FakeApi.Global, 0, FakeApi.F(7)));
            T.Eq(7f, FakeApi.GlobalValue);
            T.Eq(ReplyStatus.Denied, lab.Do(FakeApi.Global, 0, FakeApi.F(11)), "out of its range");
            T.Eq(7f, FakeApi.GlobalValue);
            lab.Rig.View.NotifyLoadStarted("https://next.example/");
            T.Eq(1f, FakeApi.GlobalValue);
            T.Seq(new[] { "restored 1" }, FakeApi.Effects);
            lab.Rig.View.NotifyLoadStarted("https://later.example/");
            T.Eq(1, FakeApi.Effects.Count, "nothing to restore a second time");
        });

        T.Run("world unload forgets every handle; a same-document navigation does not", () =>
        {
            var lab = new Lab();
            int before = lab.Handles.Cleared;
            lab.Rig.View.NotifyLoadStarted("https://world.example/space#x", true);
            T.Eq(before, lab.Handles.Cleared);
            lab.Rig.View.NotifyLoadStarted("https://next.example/");
            T.Eq(before + 1, lab.Handles.Cleared);
            lab.Rig.View.Shutdown();
            T.Eq(before + 2, lab.Handles.Cleared);
        });

        T.Run("a held document's calls wait for its release; a closed document's never run", () =>
        {
            var lab = new Lab(hold: true);
            lab.Send(FakeApi.Log, 0, FakeApi.S("held"));
            lab.Page.Flush();
            foreach (var s in lab.Page.TakeOut()) lab.Rig.View.Receive(s);
            lab.Rig.View.Drain();
            T.Eq(0, FakeApi.Effects.Count, "held");
            T.Eq(1, lab.Rig.View.Current.CallsQueued);
            lab.Rig.View.NotifyOnLoad();
            lab.Rig.View.Drain();
            T.Seq(new[] { "held" }, FakeApi.Effects);

            var gone = new Lab();
            gone.Send(FakeApi.Log, 0, FakeApi.S("never"));
            gone.Page.Flush();
            foreach (var s in gone.Page.TakeOut()) gone.Rig.View.Receive(s);
            gone.Rig.View.NotifyLoadStarted("https://next.example/");
            gone.Rig.View.Drain();
            gone.Rig.View.Flush();
            T.Eq(0, FakeApi.Effects.Count(e => e == "never"), "the document had gone");
        });

        T.Run("a request id seen twice runs once", () =>
        {
            var lab = new Lab();
            var call = FakeApi.Call(77, FakeApi.Log, 0, FakeApi.S("once"));
            lab.SendRaw(call);
            lab.SendRaw(call);
            lab.Rig.Pump(lab.Page);
            T.Eq(1, FakeApi.Effects.Count);
            T.Eq(1, lab.Page.Replies.Count(r => r.reqId == 77));
        });

        T.Run("a thunk that pumps the bridge is told NOT_READY, and the outer call still answers", () =>
        {
            var handles = new FakeHandles();
            var t = FakeApi.Table();
            BridgeCallHost host = null;
            BridgeView view = null;
            BridgeSession session = null;
            byte[] inner = FakeApi.Call(2000, FakeApi.Get, 1);
            t[FakeApi.Nothing] = new BridgeMember(FakeApi.Nothing, BridgeTier.Read, false, true, 0, "", 0, c => host.Run(view, session, inner));
            host = new BridgeCallHost(t, handles, () => 1);
            var rig = new Rig(new BridgeViewOptions { CallHost = host });
            view = rig.View;
            rig.View.NotifyLoadStarted("https://world.example/");
            rig.View.NotifyOnLoad();
            var page = new TestPage("doc");
            page.Hello();
            rig.Pump(page);
            session = view.Current;
            page.Link.EnqueueOp(BridgeOps.Encode(BridgeOp.Call, FakeApi.Call(1000, FakeApi.Nothing, 0)));
            rig.Pump(page);
            T.Eq(ReplyStatus.Ok, page.Replies.Single(r => r.reqId == 1000).status);
            T.Eq(ReplyStatus.NotReady, page.Replies.Single(r => r.reqId == 2000).status);
        });

        T.Run("legacy text around calls keeps its order and its bytes", () =>
        {
            var lab = new Lab();
            lab.Rig.Disp.AutoReply = true;
            lab.Page.Send("!i!:1¶!gp!¶");
            lab.Send(FakeApi.Log, 0, FakeApi.S("between"));
            lab.Page.Send("!i!:2¶!gp!¶");
            lab.Rig.Pump(lab.Page);
            T.Seq(new[] { "!i!:1¶!gp!¶", "!i!:2¶!gp!¶" }, lab.Rig.Disp.Texts());
            T.Eq(2, lab.Page.Got.Count);
            T.Eq(1, lab.Page.Replies.Count);
        });
    }

    static void StringAssert(string s, string part) => T.True(s.Contains(part), "\"" + s + "\" should mention \"" + part + "\"");

    static byte[] Mutate(Rng r, byte[] b)
    {
        var x = b.ToList();
        int n = 1 + r.Int(4);
        for (int i = 0; i < n; i++)
        {
            int op = r.Int(5);
            if (op == 0 && x.Count > 0) x[r.Int(x.Count)] = (byte)r.Int(256);
            else if (op == 1 && x.Count > 0) x[r.Int(x.Count)] ^= (byte)(1 << r.Int(8));
            else if (op == 2) x.Insert(r.Int(x.Count + 1), (byte)r.Int(256));
            else if (op == 3 && x.Count > 0) x.RemoveAt(r.Int(x.Count));
            else if (x.Count > 0) x = x.Take(r.Int(x.Count)).ToList();
        }
        return x.ToArray();
    }

    public static void Fuzz(int n, uint seed)
    {
        Console.WriteLine($"fuzz, typed calls (seed {seed}, {n} inputs)");
        var pool = new[] { "values", "results", "badArgs", "badValues", "calls" }
            .SelectMany(k => V.GetProperty(k).EnumerateArray().Select(x => BridgeHex.FromHex(x.GetProperty("hex").GetString()))).ToList();

        T.Run("value reader: never throws; anything accepted re-encodes exactly", () =>
        {
            var r = new Rng(seed);
            int accepted = 0;
            for (int i = 0; i < n; i++)
            {
                var b = r.Int(3) == 0 ? r.Bytes(r.Int(48)) : Mutate(r, pool[r.Int(pool.Count)]);
                foreach (bool args in new[] { true, false })
                {
                    var c = new PayloadCursor(b);
                    var v = default(BridgeValue);
                    if (BridgeValues.Read(ref c, ref v, args) != ReplyStatus.Ok) continue;
                    accepted++;
                    int used = b.Length - c.Remaining;
                    var w = new ByteWriter();
                    BridgeValues.Write(w, v);
                    if (BridgeHex.ToHex(w.Span) != BridgeHex.ToHex(b.AsSpan(0, used))) throw new Exception("does not re-encode: " + BridgeHex.ToHex(b));
                }
            }
            T.True(accepted > 0, "something was accepted");
        });

        T.Run("the host fed damaged calls: never throws, and a readable request id gets exactly one reply", () =>
        {
            var lab = new Lab();
            int h = lab.Handles.Add(new FakeObject());
            var calls = new List<byte[]>
            {
                FakeApi.Call(1, FakeApi.Set, h, FakeApi.F(1)), FakeApi.Call(1, FakeApi.Get, h), FakeApi.Call(1, FakeApi.Echo, 0, FakeApi.R(h)),
                FakeApi.Call(1, FakeApi.Log, 0, FakeApi.S("x")), FakeApi.Call(1, FakeApi.Sum, 0, w => { BridgeValues.ArrayHeader(w, ValueTag.Float, 1); w.F32(1); }),
            };
            var r = new Rng(seed);
            var view = lab.Rig.View;
            var s = view.Current;
            for (int i = 0; i < n; i++)
            {
                var b = Mutate(r, calls[r.Int(calls.Count)]);
                var c = new PayloadCursor(b);
                uint id = c.Varint();
                bool readable = !c.Failed && id != 0;
                s.TypedLedger.Clear();
                view.Flush();
                lab.Rig.Sink.Take();
                lab.Host.Run(view, s, b);
                T.Eq(readable ? 1 : 0, s.TypedLedger.Count, "ledger after " + BridgeHex.ToHex(b));
            }
        });
    }
}
