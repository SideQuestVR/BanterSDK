using System;
using System.Collections.Generic;

namespace BS.Bridge
{
    /// <summary>What a member needs. T3 has no thunk and is always refused.</summary>
    internal static class BridgeTier
    {
        public const byte Read = 0;
        public const byte Write = 1;
        public const byte Global = 2;
        public const byte Denied = 3;
    }

    /// <summary>One generated member: runs on the main thread, reads its arguments first and acts last.</summary>
    internal delegate void BridgeThunk(BridgeCallContext c);

    internal sealed class BridgeMember
    {
        public readonly int Id;
        public readonly byte Tier;
        public readonly bool Heavy;
        public readonly bool IsStatic;
        public readonly int ArgCount;
        /// <summary>Comma-separated parameter kinds (see the generator): what the self-test builds arguments from.</summary>
        public readonly string Params;
        /// <summary>The type id self must have, 0 for a static.</summary>
        public readonly int SelfType;
        public readonly BridgeThunk Thunk;

        public BridgeMember(int id, byte tier, bool heavy, bool isStatic, int argCount, string parameters, int selfType, BridgeThunk thunk)
        {
            Id = id;
            Tier = tier;
            Heavy = heavy;
            IsStatic = isStatic;
            ArgCount = argCount;
            Params = parameters;
            SelfType = selfType;
            Thunk = thunk;
        }
    }

    /// <summary>The world's objects as handles. The only way an object gets in or out of a call.</summary>
    internal interface IBridgeHandles
    {
        /// <summary>
        /// The object behind a handle, or null with status Destroyed, UnknownHandle or Denied. Called for self and for every
        /// ref argument, on every call: an object can leave the world after its handle was given out.
        /// </summary>
        object Resolve(int handle, out byte status);

        /// <summary>
        /// The handle for an object a thunk returns, and the type id of the facade that shows it. 0 when the world may not
        /// see it (or it is null); -1 when the table is full.
        /// </summary>
        int Mint(object obj, out int typeId);

        /// <summary>The world has gone: everything it was given is forgotten.</summary>
        void Clear();
    }

    internal sealed class BridgeCallLimits
    {
        public int MaxCallBytes = 256 * 1024;
        public int MaxResultBytes = 1024 * 1024;
        public int MaxHeavyPerFrame = 32;
    }

    /// <summary>
    /// One call in progress: the thunk's arguments and its result. Getters check the type; a wrong one fails the call, and
    /// the thunk stops at its Failed check before it does anything.
    /// </summary>
    internal sealed class BridgeCallContext
    {
        public const int MaxArgs = 16;

        public readonly BridgeValue[] Args = new BridgeValue[MaxArgs];
        public int ArgCount;
        public object Self;
        public readonly ByteWriter Out = new ByteWriter(256);
        public bool Failed;
        public byte FailStatus;
        public string FailMessage;
        internal IBridgeHandles Handles;
        internal BridgeCallHost Host;

        public void Fail(byte status, string message)
        {
            if (Failed) return;
            Failed = true;
            FailStatus = status;
            FailMessage = message;
        }

        void WrongType(int i, string wanted) => Fail(ReplyStatus.Type, "argument " + i + ": expected " + wanted);

        ref BridgeValue Arg(int i, byte tag, string wanted)
        {
            ref var v = ref Args[i];
            if (v.Tag != tag) WrongType(i, wanted);
            return ref v;
        }

        public bool Bool(int i) => Arg(i, ValueTag.Bool, "a boolean").I != 0;
        public int Int(int i) => Arg(i, ValueTag.Int, "an integer").I;
        public string Str(int i) => Arg(i, ValueTag.String, "a string").S;

        public float Float(int i)
        {
            ref var v = ref Args[i];
            if (v.Tag == ValueTag.Int) return v.I;
            if (v.Tag == ValueTag.Float || v.Tag == ValueTag.Double)
            {
                float f = (float)v.D;
                if (float.IsInfinity(f)) WrongType(i, "a number a float can hold");
                return f;
            }
            WrongType(i, "a number");
            return 0;
        }

        public double Double(int i)
        {
            ref var v = ref Args[i];
            if (v.Tag == ValueTag.Int) return v.I;
            if (v.Tag == ValueTag.Float || v.Tag == ValueTag.Double) return v.D;
            WrongType(i, "a number");
            return 0;
        }

        /// <summary>A VEC2, VEC3, VEC4, QUAT or COLOR argument; X, Y, Z and W hold it.</summary>
        public ref BridgeValue Vector(int i, byte tag) => ref Arg(i, tag, "a vector, quaternion or colour of the right size");

        /// <summary>An enum as its integer value, which must be one of those the member's type declares.</summary>
        public int Enum(int i, int[] allowed)
        {
            int v = Int(i);
            if (Failed) return 0;
            if (Array.IndexOf(allowed, v) < 0) Fail(ReplyStatus.Type, "argument " + i + ": " + v + " is not a value of this enum");
            return v;
        }

        /// <summary>An enum whose values combine: only bits of mask may be set.</summary>
        public int EnumFlags(int i, int mask)
        {
            int v = Int(i);
            if (Failed) return 0;
            if ((v & ~mask) != 0) Fail(ReplyStatus.Type, "argument " + i + ": " + v + " has bits this enum does not declare");
            return v;
        }

        /// <summary>The call's target as the class the member is declared on.</summary>
        public T SelfAs<T>() where T : class
        {
            var o = Self as T;
            if (o == null) Fail(ReplyStatus.Type, "the target is not a " + typeof(T).Name);
            return o;
        }

        /// <summary>A ref argument. The dispatcher has resolved it and checked its scope; this checks its class.</summary>
        public T Ref<T>(int i, bool nullable = false) where T : class
        {
            ref var v = ref Args[i];
            if (v.Tag == ValueTag.Null)
            {
                if (!nullable) WrongType(i, "an object");
                return null;
            }
            if (v.Tag != ValueTag.Ref) { WrongType(i, "an object"); return null; }
            var o = v.Obj as T;
            if (o == null) WrongType(i, typeof(T).Name);
            return o;
        }

        public BridgeArray Array_(int i, byte elemTag)
        {
            ref var v = ref Args[i];
            if (v.Tag != ValueTag.Array || v.A.ElemTag != elemTag) { WrongType(i, "an array of the right type"); return null; }
            return v.A;
        }

        public void RangeInt(int i, int min, int max)
        {
            if (Failed) return;
            int v = Args[i].I;
            if (v < min || v > max) Fail(ReplyStatus.Denied, "argument " + i + ": must be from " + min + " to " + max);
        }

        public void RangeFloat(int i, double min, double max)
        {
            if (Failed) return;
            double v = Double(i);
            if (!(v >= min && v <= max)) Fail(ReplyStatus.Denied, "argument " + i + ": must be from " + min + " to " + max);
        }

        /// <summary>Every component of a vector argument.</summary>
        public void RangeVector(int i, double min, double max)
        {
            if (Failed) return;
            ref var v = ref Args[i];
            int width = ValueTag.Width(v.Tag);
            if (!(In(v.X, min, max) && In(v.Y, min, max) && (width < 3 || In(v.Z, min, max)) && (width < 4 || In(v.W, min, max))))
                Fail(ReplyStatus.Denied, "argument " + i + ": every component must be from " + min + " to " + max);
        }

        static bool In(float v, double min, double max) => v >= min && v <= max;

        public void RangeLength(int i, int max)
        {
            if (Failed) return;
            ref var v = ref Args[i];
            int n = v.Tag == ValueTag.Array ? v.A.Count : v.Tag == ValueTag.String ? v.S.Length : 0;
            if (n > max) Fail(ReplyStatus.Limit, "argument " + i + ": at most " + max + " items");
        }

        // ----------------------------------------------------------------------------------------------- results

        public void RetBool(bool v) => BridgeValues.Bool(Out, v);
        public void RetInt(int v) => BridgeValues.Int(Out, v);
        public void RetFloat(float v) => BridgeValues.Float(Out, v);
        public void RetDouble(double v) => BridgeValues.Double(Out, v);
        public void RetString(string v) { if (v == null) BridgeValues.Null(Out); else BridgeValues.String(Out, v); }
        public void RetNull() => BridgeValues.Null(Out);

        /// <summary>The filter for everything a thunk returns: an object the world may not see reads as null.</summary>
        public void RetRef(object o)
        {
            int handle = Handles.Mint(o, out int typeId);
            if (handle < 0) Fail(ReplyStatus.Limit, "too many objects held by this world");
            else if (handle == 0) BridgeValues.Null(Out);
            else BridgeValues.Ref(Out, handle, typeId);
        }

        /// <summary>An array of objects, leaving out the ones the world may not see, at most max of them.</summary>
        public void RetRefs<T>(IList<T> objects, int max) where T : class
        {
            var handles = new List<(int handle, int type)>();
            for (int i = 0; i < objects.Count && handles.Count < max; i++)
            {
                int h = Handles.Mint(objects[i], out int typeId);
                if (h < 0) { Fail(ReplyStatus.Limit, "too many objects held by this world"); return; }
                if (h > 0) handles.Add((h, typeId));
            }
            BridgeValues.ArrayHeader(Out, ValueTag.Ref, handles.Count);
            foreach (var (h, t) in handles) BridgeValues.ElementRef(Out, h, t);
        }

        /// <summary>A global the call is about to change: restore puts it back at world unload. Only the first per member counts.</summary>
        public void SnapshotOnce(int member, Action restore) => Host.SnapshotOnce(member, restore);

        internal void Reset()
        {
            Array.Clear(Args, 0, ArgCount);
            ArgCount = 0;
            Self = null;
            Out.Reset();
            Failed = false;
            FailStatus = ReplyStatus.Ok;
            FailMessage = null;
        }
    }

    /// <summary>
    /// Runs CALL ops: decodes, checks tier, capabilities, arguments and scope, runs the thunk, and sends the one reply.
    /// Plain C#: Unity supplies the member table and the handles. Main thread only.
    /// </summary>
    internal sealed class BridgeCallHost
    {
        /// <summary>Set by the BS.BridgeApi assembly. Null: this client has no typed API, and WELCOME says so.</summary>
        public static BridgeCallHost Current;

        readonly BridgeMember[] members;
        readonly IBridgeHandles handles;
        readonly Func<int> frame;
        readonly BridgeCallContext ctx = new BridgeCallContext();
        readonly List<Action> restores = new List<Action>();
        readonly HashSet<int> snapshots = new HashSet<int>();
        int heavyFrame = -1;
        int heavyUsed;
        bool running;
        /// <summary>Exceptions logged for this world: the page has each one's message, and the rest are only counted.</summary>
        const int MaxLoggedFailures = 8;
        int loggedFailures;

        public readonly BridgeCallLimits Limits;
        public uint Caps = BridgeCap.Read | BridgeCap.Write | BridgeCap.Global;
        /// <summary>The client's limit on Caps, asked on every call. Null: no limit.</summary>
        public Func<uint> CapsLimit;
        /// <summary>The client's list of members that are off, asked on every call. Null: none.</summary>
        public Func<int, bool> MemberOff;
        public Action<string> Log;
        public long Calls, Refused, Failures;

        /// <summary>What a document may do right now: what WELCOME says, and what every call is checked against.</summary>
        public uint GrantedCaps => CapsLimit == null ? Caps : Caps & CapsLimit();

        /// <summary>No tiers, no family: the page says UNSUPPORTED and sends nothing.</summary>
        public uint Families => GrantedCaps == 0 ? 0 : BridgeFamily.Call;

        public BridgeCallHost(BridgeMember[] members, IBridgeHandles handles, Func<int> frame, BridgeCallLimits limits = null)
        {
            this.members = members;
            this.handles = handles;
            this.frame = frame;
            Limits = limits ?? new BridgeCallLimits();
            ctx.Handles = handles;
            ctx.Host = this;
        }

        /// <summary>False: a heavy call has no room this frame, and it (and what follows it) waits for the next one.</summary>
        public bool CanRun(ReadOnlySpan<byte> call)
        {
            var c = new PayloadCursor(call);
            c.Varint();
            uint id = c.Varint();
            if (c.Failed || id >= members.Length) return true;
            var m = members[id];
            if (m == null || !m.Heavy) return true;
            RollFrame();
            return heavyUsed < Limits.MaxHeavyPerFrame;
        }

        void RollFrame()
        {
            int f = frame();
            if (f == heavyFrame) return;
            heavyFrame = f;
            heavyUsed = 0;
        }

        /// <summary>Runs one CALL and sends its reply. A payload with no readable request id can't be answered: it is dropped.</summary>
        public void Run(BridgeView view, BridgeSession session, ReadOnlySpan<byte> call)
        {
            if (running)
            {
                // A thunk's Unity callback pumped the bridge. Nothing here is re-entrant, and it can't be right to run now.
                if (TryRequestId(call, out uint nested) && session.BeginRequest(nested))
                    SendError(view, session, nested, ReplyStatus.NotReady, "bridge: a call is already running");
                return;
            }
            var c = new PayloadCursor(call);
            uint reqId = c.Varint();
            if (c.Failed || reqId == 0) { view.Stats.DroppedOps++; return; }
            if (!session.BeginRequest(reqId)) { view.Stats.ReusedRequestIds++; return; }
            byte status = Invoke(ref c, call.Length);
            if (status == ReplyStatus.Ok) view.Reply(session.Epoch, reqId, ReplyStatus.Ok, ctx.Out.Span);
            else SendError(view, session, reqId, status, ctx.FailMessage);
            ctx.Reset();
        }

        /// <summary>What the last Invoke produced: one value, or its failure's message.</summary>
        public ReadOnlySpan<byte> Result => ctx.Out.Span;
        public string FailMessage => ctx.FailMessage;

        /// <summary>The call after its request id. Returns its status; it never throws.</summary>
        public byte Invoke(ref PayloadCursor c, int callBytes)
        {
            running = true;
            byte status;
            ctx.Reset();
            try
            {
                status = callBytes > Limits.MaxCallBytes ? Refuse(ReplyStatus.Limit, "call too large") : Execute(ref c);
            }
            catch (Exception e)
            {
                // A thunk, or a handle check: whichever it was, the page still gets its one reply.
                Failures++;
                status = ReplyStatus.Internal;
                string text = Explains(e) ? e.GetType().Name + ": " + e.Message : e.GetType().Name;
                ctx.Failed = false;
                ctx.Fail(status, text.Length > BridgeWire.MaxReasonText ? text.Substring(0, BridgeWire.MaxReasonText) : text);
                if (loggedFailures < MaxLoggedFailures)
                {
                    loggedFailures++;
                    Log?.Invoke("[Bridge] A call threw: " + e);
                }
            }
            finally
            {
                running = false;
            }
            return status;
        }

        /// <summary>Unity's rule-breaking exceptions speak to the caller; anything else is named, not quoted.</summary>
        static bool Explains(Exception e) => e is ArgumentException || e.GetType().Name == "UnityException";

        byte Refuse(byte status, string message)
        {
            Refused++;
            ctx.Fail(status, message);
            return status;
        }

        byte Execute(ref PayloadCursor c)
        {
            uint memberId = c.Varint(), self = c.Varint(), argc = c.Varint();
            if (c.Failed) return Refuse(ReplyStatus.Malformed, "bad call header");
            var m = memberId < members.Length ? members[memberId] : null;
            if (m == null) return Refuse(ReplyStatus.UnknownMember, "unknown member " + memberId);
            if (m.Tier == BridgeTier.Denied || m.Thunk == null) return Refuse(ReplyStatus.Denied, "member " + memberId + " is not available to worlds");
            if ((GrantedCaps & CapFor(m.Tier)) == 0) return Refuse(ReplyStatus.Denied, "this document may not use member " + memberId);
            if (MemberOff != null && MemberOff((int)memberId)) return Refuse(ReplyStatus.Denied, "member " + memberId + " is switched off");
            if (argc != m.ArgCount) return Refuse(ReplyStatus.Type, "member " + memberId + " takes " + m.ArgCount + " arguments, got " + argc);

            ctx.ArgCount = (int)argc;
            for (int i = 0; i < ctx.ArgCount; i++)
            {
                byte s = BridgeValues.Read(ref c, ref ctx.Args[i], true);
                if (s != ReplyStatus.Ok) return Refuse(s, "argument " + i + " can't be read");
            }
            if (!c.Done) return Refuse(ReplyStatus.Malformed, "bytes after the last argument");

            if (m.IsStatic)
            {
                if (self != 0) return Refuse(ReplyStatus.Type, "a static member has no target");
            }
            else
            {
                if (self == 0 || self > int.MaxValue) return Refuse(ReplyStatus.Type, "this member needs a target");
                ctx.Self = handles.Resolve((int)self, out byte s);
                if (ctx.Self == null) return Refuse(s, Explain(s, "the target"));
            }
            for (int i = 0; i < ctx.ArgCount; i++)
            {
                byte s = ResolveRefs(ref ctx.Args[i], i);
                if (s != ReplyStatus.Ok) return s;
            }

            if (m.Heavy)
            {
                RollFrame();
                heavyUsed++;
            }
            Calls++;
            m.Thunk(ctx);
            if (ctx.Failed) return ctx.FailStatus;
            if (ctx.Out.Length == 0) ctx.RetNull();
            if (ctx.Out.Length > Limits.MaxResultBytes) return Refuse(ReplyStatus.Limit, "result too large");
            return ReplyStatus.Ok;
        }

        byte ResolveRefs(ref BridgeValue v, int index)
        {
            if (v.Tag == ValueTag.Ref)
            {
                v.Obj = handles.Resolve(v.I, out byte s);
                return v.Obj != null ? ReplyStatus.Ok : Refuse(s, Explain(s, "argument " + index));
            }
            if (v.Tag != ValueTag.Array || v.A.ElemTag != ValueTag.Ref) return ReplyStatus.Ok;
            var a = v.A;
            a.Objects = new object[a.Count];
            for (int i = 0; i < a.Count; i++)
            {
                if (a.Ints[i] == 0) continue;
                a.Objects[i] = handles.Resolve(a.Ints[i], out byte s);
                if (a.Objects[i] == null) return Refuse(s, Explain(s, "argument " + index + "[" + i + "]"));
            }
            return ReplyStatus.Ok;
        }

        static string Explain(byte status, string what)
        {
            switch (status)
            {
                case ReplyStatus.Destroyed: return what + " has been destroyed";
                case ReplyStatus.Denied: return what + " does not belong to this world";
                default: return what + " is not an object this world knows";
            }
        }

        static uint CapFor(byte tier) => tier == BridgeTier.Read ? BridgeCap.Read : tier == BridgeTier.Write ? BridgeCap.Write : BridgeCap.Global;

        static bool TryRequestId(ReadOnlySpan<byte> call, out uint reqId)
        {
            var c = new PayloadCursor(call);
            reqId = c.Varint();
            return !c.Failed && reqId != 0;
        }

        void SendError(BridgeView view, BridgeSession session, uint reqId, byte status, string message)
        {
            var w = new ByteWriter(16 + (message?.Length ?? 0));
            w.Str(message ?? string.Empty);
            view.Reply(session.Epoch, reqId, status, w.Span);
        }

        /// <summary>
        /// A CALL has just arrived. False: it will not run, and it has had its answer (over the limit, or nobody can run
        /// it). Replies go out of order with the calls ahead of it: they carry their request id.
        /// </summary>
        internal static bool Admit(BridgeView view, BridgeCallHost host, BridgeSession session, ReadOnlySpan<byte> call, int maxQueued)
        {
            if (!TryRequestId(call, out uint reqId)) { view.Stats.DroppedOps++; return false; }
            if (host == null)
            {
                Reject(view, session, reqId, ReplyStatus.Unsupported, "this client has no typed API");
                return false;
            }
            if (session.CallsQueued >= maxQueued)
            {
                host.Refused++;
                session.CallsOverflowed++;
                Reject(view, session, reqId, ReplyStatus.Limit, "too many calls in flight");
                return false;
            }
            session.CallsQueued++;
            return true;
        }

        static void Reject(BridgeView view, BridgeSession session, uint reqId, byte status, string message)
        {
            if (!session.BeginRequest(reqId)) { view.Stats.ReusedRequestIds++; return; }
            var w = new ByteWriter(16 + message.Length);
            w.Str(message);
            view.Reply(session.Epoch, reqId, status, w.Span);
        }

        internal void SnapshotOnce(int member, Action restore)
        {
            if (snapshots.Add(member)) restores.Add(restore);
        }

        /// <summary>Global changes go back, newest first. Safe to call at any time: nothing is left to undo twice.</summary>
        public void RestoreGlobals()
        {
            for (int i = restores.Count - 1; i >= 0; i--)
            {
                try { restores[i](); }
                catch (Exception e) { Log?.Invoke("[Bridge] Restoring a global failed: " + e); }
            }
            restores.Clear();
            snapshots.Clear();
        }

        /// <summary>The world has gone: its global changes are undone, and its handles forgotten.</summary>
        public void WorldUnloaded()
        {
            RestoreGlobals();
            handles.Clear();
            loggedFailures = 0;
        }
    }
}
