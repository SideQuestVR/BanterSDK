using System;
using System.Collections.Generic;
using System.Linq;
using BS.Bridge;

static class T
{
    public static int Passed, Failed;
    public static readonly List<string> Failures = new List<string>();
    static string current = "";

    public static void Run(string name, Action body)
    {
        current = name;
        try
        {
            body();
            Passed++;
            Console.WriteLine("  ok   " + name);
        }
        catch (Exception e)
        {
            Failed++;
            Failures.Add(name + ": " + e.Message);
            Console.WriteLine("  FAIL " + name + "\n       " + e.Message.Replace("\n", "\n       "));
        }
    }

    public static void True(bool c, string what = "expected true") { if (!c) throw new Exception(what); }
    public static void Eq<TV>(TV expected, TV actual, string what = "")
    {
        if (!EqualityComparer<TV>.Default.Equals(expected, actual))
            throw new Exception($"{what} expected <{Show(expected)}> got <{Show(actual)}>");
    }
    public static void Seq<TV>(IEnumerable<TV> expected, IEnumerable<TV> actual, string what = "")
    {
        var a = expected.ToList(); var b = actual.ToList();
        if (a.Count != b.Count) throw new Exception($"{what} count expected {a.Count} got {b.Count}: [{string.Join(", ", b.Take(12).Select(x => Show(x)))}]");
        for (int i = 0; i < a.Count; i++)
            if (!EqualityComparer<TV>.Default.Equals(a[i], b[i])) throw new Exception($"{what} [{i}] expected <{Show(a[i])}> got <{Show(b[i])}>");
    }
    static string Show(object o) => o is string s ? (s.Length > 120 ? s.Substring(0, 120) + "…" : s) : o?.ToString() ?? "null";
}

/// <summary>xorshift32, same as the TS suite's Rng.</summary>
sealed class Rng
{
    uint s;
    public Rng(uint seed) { s = seed == 0 ? 0x9e3779b9 : seed; }
    public uint Next() { uint x = s; x ^= x << 13; x ^= x >> 17; x ^= x << 5; s = x; return x; }
    public int Int(int n) => (int)(Next() % (uint)n);
    public byte[] Bytes(int n) { var b = new byte[n]; for (int i = 0; i < n; i++) b[i] = (byte)Next(); return b; }
}

sealed class FakeSink : IBridgeSink
{
    public readonly List<string> Posted = new List<string>();
    public void Post(string data) => Posted.Add(data);
    public List<string> Take() { var r = new List<string>(Posted); Posted.Clear(); return r; }
}

/// <summary>Records what Unity "ran"; optionally answers legacy requests (inside the dispatch context).</summary>
sealed class FakeDispatcher : IBridgeDispatcher
{
    public BridgeView View;
    public readonly List<(uint epoch, string text)> Ran = new List<(uint, string)>();
    public readonly List<(uint epoch, uint opcode)> Ops = new List<(uint, uint)>();
    public Func<BridgeSession, string, bool> OnText;
    public bool AutoReply;

    public void DispatchText(BridgeSession session, string text)
    {
        Ran.Add((session?.Epoch ?? 0, text));
        if (OnText != null && OnText(session, text)) return;
        if (AutoReply)
        {
            int id = BridgeView.LegacyRequestId(text, true);
            if (id > 0 && id != BridgeView.NoReplyRequestId) View.Send("!i!:" + id + "¶ok");
        }
    }

    public void DispatchOp(BridgeSession session, uint opcode, ReadOnlySpan<byte> payload)
    {
        Ops.Add((session?.Epoch ?? 0, opcode));
        if (opcode == 0x80 && session != null)
        {
            int pos = 0;
            uint reqId = (uint)BridgeOps.ReadVarint(payload, ref pos);
            session.BeginRequest(reqId);
            View.Reply(BridgeContext.CurrentEpoch, reqId, ReplyStatus.Ok, new byte[] { 1 });
        }
    }

    public List<string> Texts(uint? epoch = null) => Ran.Where(r => epoch == null || r.epoch == epoch).Select(r => r.text).ToList();
}

/// <summary>
/// A scripted page that speaks the protocol with the C# codecs (the real page is TS, simulated in its own suite).
/// It follows the page rules: HELLO at epoch 0, adopt WELCOME's epoch, ack it, frames within the window, park on CLOSE,
/// HELLO again with the unconsumed work on RESET, drop frames of other epochs.
/// </summary>
sealed class TestPage
{
    public string Token;
    public readonly List<string> Parked = new List<string>();
    int revives;
    public readonly string Href;
    public BridgeLink Link = new BridgeLink();
    public uint Epoch;
    public string State = "idle";
    public readonly List<string> Out = new List<string>();
    public readonly List<string> Got = new List<string>();
    public readonly List<(uint reqId, byte status)> Replies = new List<(uint, byte)>();
    public readonly Dictionary<uint, byte[]> ReplyBodies = new Dictionary<uint, byte[]>();
    public uint Families, Caps;
    public int Stale;
    public bool HonorWindow = true;
    bool mustAck;
    string hello;

    public TestPage(string token, string href = "https://world.example/space") { Token = token; Href = href; }

    public void Hello(uint features = BridgeFeature.TextForm, int recvWindow = BridgeWire.DefaultWindow)
    {
        var h = new HelloPayload { Features = features, RecvWindow = (uint)recvWindow, SdkVersion = "test", DocToken = Token, Href = Href };
        Link.EnqueueOp(BridgeOps.Encode(BridgeOp.Hello, h.Encode()));
        var frames = new List<OutFrame>();
        Link.TakeFrames(frames);
        foreach (var f in frames) Out.Add(hello = f.ToCarrier());
        State = "hello";
    }

    /// <summary>The page rule when WELCOME does not come: the same HELLO again.</summary>
    public void HelloAgain() => Out.Add(hello);

    public void Send(string text)
    {
        if (State == "parked") Parked.Add(text);
        else Link.EnqueueText(text);
    }

    /// <summary>The page rule after a RESET, or when it can't follow Unity: the same token, a new link, the rest goes again.</summary>
    public void Reopen(List<OutEntry> rest, uint ack = 0)
    {
        int fragmentSize = Link.FragmentSize;
        Link = new BridgeLink { FragmentSize = fragmentSize };
        Epoch = 0;
        Link.AckSeq = ack;  // the HELLO frame's ack: what we took of Unity's frames
        Hello();
        Link.AckSeq = 0;
        foreach (var e in rest)
        {
            if (e.IsText) Link.EnqueueText(e.Text);
            else Link.EnqueueOp(e.Op);
        }
    }

    /// <summary>The page rule after a navigation CLOSE: still alive, so ask again as "&lt;token&gt;~n" with the parked strings.</summary>
    public void Revive()
    {
        Token = Token.Split('~')[0] + "~" + (++revives);
        Link = new BridgeLink();
        Epoch = 0;
        Hello();
        foreach (var t in Parked) Link.EnqueueText(t);
        Parked.Clear();
    }

    /// <summary>pagehide: BYE out of band (epoch 0, with the token).</summary>
    public void Bye() => Notice(0, BridgeOp.Bye, BridgeReason.PageHide);

    public void Request(uint opcode, uint reqId)
    {
        var w = new ByteWriter(); w.Varint(reqId);
        Link.EnqueueOp(BridgeOps.Encode(opcode, w.ToArray()));
    }

    public void Notice(uint epoch, int opcode, byte reason)
    {
        var body = BridgeOps.Encode((uint)opcode, new FarewellPayload { Reason = reason, DocToken = Token }.Encode());
        uint seq = epoch == 0 ? 1 : Link.NextSeq++;
        Out.Add(BridgeCarrier.EncodeBinary(new FrameHeader(FrameKind.Data, FrameFlags.Complete, epoch, seq, Link.AckSeq, 0), body));
    }

    public void Flush()
    {
        if (State != "open") return;
        if (!HonorWindow) Link.SendWindow = int.MaxValue;
        var frames = new List<OutFrame>();
        Link.TakeFrames(frames);
        foreach (var f in frames) Out.Add(f.ToCarrier());
        if (frames.Count == 0 && Link.NeedsAck(mustAck)) Out.Add(BridgeCarrier.EncodeBinary(Link.ControlHeader(FrameKind.Ack), ReadOnlySpan<byte>.Empty));
        mustAck = false;
    }

    public void Receive(string s)
    {
        if (!BridgeCarrier.IsCarrier(s))
        {
            Got.Add(s);
            return;
        }
        if (BridgeCarrier.TryDecode(s, out var f) != BridgeError.None) throw new Exception("Unity sent a bad carrier");
        var h = f.Header;
        if (State == "hello")
        {
            if (h.Kind != FrameKind.Data || f.IsText) return;
            var ops = new List<RawOp>();
            BridgeOps.Decode(f.Payload, ops);
            if (ops.Count == 0) return;
            var p = f.Payload.Slice(ops[0].Offset, ops[0].Length);
            if (h.Epoch == 0)
            {
                if (ops[0].Opcode == BridgeOp.Bye) State = "closed";
                return;
            }
            if (ops[0].Opcode != BridgeOp.Welcome) return;
            var w = WelcomePayload.TryDecode(p);
            Epoch = w.Epoch; Link.Epoch = w.Epoch; Link.SendWindow = (int)w.RecvWindow;
            Families = w.Families; Caps = w.Caps;
            State = "open"; mustAck = true;
        }
        if (State != "open" || h.Epoch != Epoch) { Stale++; return; }
        if (h.Kind == FrameKind.Close || h.Kind == FrameKind.Reset)
        {
            // Before the ack: the header ack of a CLOSE/RESET is its consumed seq.
            BridgeFrame.TryDecodeClose(f.Payload, out var c);
            var rest = Link.TakeUnconsumed(c.ConsumedSeq);
            if (h.Kind == FrameKind.Close)
            {
                if (c.Reason == BridgeReason.Timeout) { State = "closed"; return; } // told not to ask again
                State = "parked"; // this document is being replaced: hold, never replay into the next world
                foreach (var e in rest) if (e.IsText) Parked.Add(e.Text);
                return;
            }
            Reopen(rest, Link.AckSeq);
            return;
        }
        if (!Link.OnAck(h.Ack)) throw new Exception("Unity acked the future");
        switch (h.Kind)
        {
            case FrameKind.Data:
            {
                var msgs = new List<BridgeMessage>();
                var e = f.IsText ? Link.ReceiveData(h, f.Body, null, 0, 0, msgs) : Link.ReceiveData(h, null, f.Frame, BridgeWire.HeaderBytes, h.Length, msgs);
                if (e != BridgeError.None) throw new Exception("Unity frames broke the page link: " + e);
                foreach (var m in msgs)
                    foreach (var it in m.Items)
                    {
                        if (it.IsText) Got.Add(it.Text);
                        else if (it.Opcode == BridgeOp.Reply && ReplyPayload.TryDecode(it.Payload, out var id, out var st, out var body))
                        {
                            Replies.Add((id, st));
                            ReplyBodies[id] = body.ToArray();
                        }
                    }
                return;
            }
        }
    }

    public List<string> TakeOut() { var r = new List<string>(Out); Out.Clear(); return r; }
}

/// <summary>One view plus its fake ends and a manual clock.</summary>
sealed class Rig
{
    public long Now;
    public readonly FakeSink Sink = new FakeSink();
    public readonly FakeDispatcher Disp = new FakeDispatcher();
    public readonly BridgeView View;
    public readonly List<string> Logs = new List<string>();

    public Rig(BridgeViewOptions o = null)
    {
        o ??= new BridgeViewOptions();
        o.Log = m => Logs.Add(m);
        View = new BridgeView(Sink, Disp, o, () => Now);
        Disp.View = View;
    }

    /// <summary>Page posts -> Unity drains -> Unity flushes -> page receives, until quiet.</summary>
    public void Pump(TestPage page, int rounds = 40)
    {
        for (int i = 0; i < rounds; i++)
        {
            page.Flush();
            var up = page.TakeOut();
            foreach (var s in up) View.Receive(s);
            View.Drain();
            View.Flush();
            var down = Sink.Take();
            foreach (var s in down) page.Receive(s);
            if (up.Count == 0 && down.Count == 0 && !View.HasOutbound && page.Out.Count == 0) return;
        }
    }

    public void Deliver(TestPage page)
    {
        foreach (var s in page.TakeOut()) View.Receive(s);
        View.Drain();
    }

    public void Back(TestPage page)
    {
        View.Flush();
        foreach (var s in Sink.Take()) page.Receive(s);
    }
}
