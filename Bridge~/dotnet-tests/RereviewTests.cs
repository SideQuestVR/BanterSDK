using System;
using System.Collections.Generic;
using System.Linq;
using BS.Bridge;

/// <summary>Regression tests for the M1 re-review (findings N1-N8; probe names in brackets).</summary>
static class RereviewTests
{
    static (Rig rig, TestPage page) Open(string token, BridgeViewOptions o = null, string href = "https://world.example/space")
    {
        var rig = new Rig(o);
        rig.View.NotifyLoadStarted(href);
        rig.View.NotifyOnLoad();
        var page = new TestPage(token, href);
        page.Hello();
        rig.Pump(page);
        T.Eq("open", page.State);
        return (rig, page);
    }

    /// <summary>A document that has said HELLO and is held (LoadStarted seen, OnLoad not yet).</summary>
    static (Rig rig, TestPage page) Held(string token, BridgeViewOptions o = null)
    {
        var rig = new Rig(o);
        rig.View.NotifyLoadStarted("https://world.example/space");
        var page = new TestPage(token);
        page.Hello();
        rig.Pump(page);
        T.Eq("open", page.State);
        T.True(rig.View.Current.Held);
        return (rig, page);
    }

    /// <summary>Rig.Pump with the clock moving: msPerRound per round trip.</summary>
    static void PumpTimed(Rig rig, TestPage page, int msPerRound, int rounds)
    {
        for (int i = 0; i < rounds; i++)
        {
            rig.Now += msPerRound;
            page.Flush();
            foreach (var s in page.TakeOut()) rig.View.Receive(s);
            rig.View.Drain();
            rig.View.Flush();
            foreach (var s in rig.Sink.Take()) page.Receive(s);
        }
    }

    static string Frame(TestPage page, uint seq, params (int opcode, byte[] payload)[] ops)
    {
        var w = new ByteWriter();
        foreach (var op in ops) BridgeOps.Write(w, (uint)op.opcode, op.payload);
        return BridgeCarrier.EncodeBinary(new FrameHeader(FrameKind.Data, FrameFlags.Complete, page.Epoch, seq, page.Link.AckSeq, 0), w.ToArray());
    }

    static byte[] Farewell(TestPage page) => new FarewellPayload { Reason = BridgeReason.PageHide, DocToken = page.Token }.Encode();

    public static void Run()
    {
        Console.WriteLine("re-review findings");

        T.Run("[N1/P15] big frames while held: RESET at the window, the hold stays bounded", () =>
        {
            var (rig, page) = Held("tokF");
            var s = rig.View.Current;
            uint seq = page.Link.NextSeq;
            var big = new byte[250 * 1024];
            for (int i = 0; i < 100; i++) rig.View.Receive(Frame(page, seq + (uint)i, (BridgeOp.LegacyText, big)));
            rig.View.Drain();
            T.Eq(1L, rig.View.Stats.ProtocolResets);
            T.Eq(BridgeSessionState.Legacy, s.State);
            T.True(s.Link.UnackedBytes <= s.Link.RecvWindow, "unacked " + s.Link.UnackedBytes);
            T.True(s.HeldBytes <= rig.View.Options.MaxHeldBytes, "held " + s.HeldBytes);
            T.Eq(4, s.HeldCount, "the frames within the window");
            T.Eq(95L, rig.View.Stats.StaleFrames, "frame 5 reset the session; the rest are stale");
        });

        T.Run("[R3] BYE in a frame while held closes at once: nothing piles up (frames ending in BYE, or BYE alone)", () =>
        {
            foreach (var withText in new[] { true, false })
            {
                var (rig, page) = Held("tokBye" + withText);
                var s = rig.View.Current;
                uint seq = page.Link.NextSeq;
                int n = withText ? 100 : 50_000;
                for (int i = 0; i < n; i++)
                {
                    rig.View.Receive(withText
                        ? Frame(page, seq + (uint)i, (BridgeOp.LegacyText, new byte[250 * 1024]), (BridgeOp.Bye, Farewell(page)))
                        : Frame(page, seq + (uint)i, (BridgeOp.Bye, Farewell(page))));
                }
                while (rig.View.InboundCount > 0) rig.View.Drain();
                T.Eq(BridgeSessionState.Closed, s.State);
                T.Eq(BridgeReason.PageHide, s.CloseReason);
                T.Eq(0, s.HeldCount);
                T.Eq((long)(n - 1), rig.View.Stats.StaleFrames, "everything after the first BYE is stale");
                T.Eq(0L, rig.View.Stats.ProtocolResets);
                T.Eq(0, rig.Disp.Ran.Count, "nothing of the document ran");
            }
        });

        T.Run("[R2] held items are charged a per-item overhead: many tiny strings can't hide under the byte cap", () =>
        {
            var (rig, page) = Held("tokTiny");
            var sb = new System.Text.StringBuilder(BridgeLegacyBatch.PageMarker);
            for (int i = 0; i < 200_000; i++) sb.Append("1:x");
            rig.View.Receive(sb.ToString());
            rig.View.Drain();
            var s = rig.View.Current;
            long cap = rig.View.Options.MaxHeldBytes;
            T.True(s.HeldBytes <= cap, "counted " + s.HeldBytes);
            T.True(s.HeldCount <= cap / 66, "held " + s.HeldCount + " items");
            T.Eq(200_000L - s.HeldCount, rig.View.Stats.HoldOverflowDropped);
        });

        T.Run("[N1] a farewell mixed with other ops, or bigger than a control payload, is held to the window", () =>
        {
            foreach (var mixed in new[] { true, false })
            {
                var o = new BridgeViewOptions { RecvWindow = 4096 };
                var (rig, page) = Held("tokMix" + mixed, o);
                uint seq = page.Link.NextSeq;
                rig.View.Receive(Frame(page, seq, (BridgeOp.LegacyText, new byte[4090]))); // fills the window (one frame may always go)
                rig.View.Drain();
                T.Eq(0L, rig.View.Stats.ProtocolResets);
                var tooBig = new FarewellPayload { Reason = BridgeReason.Protocol, DocToken = new string('t', BridgeWire.MaxDocToken) }.Encode();
                var f = mixed
                    ? Frame(page, seq + 1, (BridgeOp.LegacyText, new byte[] { (byte)'x' }), (BridgeOp.Legacy, Farewell(page)))
                    : Frame(page, seq + 1, (BridgeOp.Legacy, tooBig), (BridgeOp.Legacy, tooBig), (BridgeOp.Legacy, tooBig), (BridgeOp.Legacy, tooBig));
                rig.View.Receive(f);
                rig.View.Drain();
                T.Eq(1L, rig.View.Stats.ProtocolResets, mixed ? "mixed" : "oversized");
            }
            // The page's own farewell (one LEGACY op) still goes when credit-blocked: covered by [P9].
        });

        T.Run("[N1] framed work held past the cap: RESET reports it unconsumed; the re-sent strings fall under the unframed cap", () =>
        {
            var o = new BridgeViewOptions { RecvWindow = 64 * 1024 * 1024, MaxHeldBytes = 100_000 };
            var (rig, page) = Held("tokCapF", o);
            string a = "a" + new string('1', 40_000), b = "b" + new string('2', 40_000);
            page.Send(a); rig.Pump(page);
            uint bSeq = page.Link.NextSeq;
            page.Send(b);
            page.Flush();
            rig.Deliver(page);
            rig.View.Flush();
            uint consumed = 0;
            foreach (var d in rig.Sink.Take())
            {
                if (BridgeCarrier.TryDecode(d, out var f) == BridgeError.None && f.Header.Kind == FrameKind.Reset)
                {
                    BridgeFrame.TryDecodeClose(f.Payload, out var c);
                    consumed = c.ConsumedSeq;
                    T.Eq(BridgeReason.Overflow, c.Reason);
                }
                page.Receive(d);
            }
            T.Eq(bSeq - 1, consumed, "b is reported unconsumed");
            T.Eq("legacy", page.State);
            rig.Pump(page); // b comes back as a string, past the cap
            var s = rig.View.Current;
            T.True(s.HeldFramedBytes <= o.MaxHeldBytes && s.HeldBytes <= o.MaxHeldBytes, $"held {s.HeldBytes} / framed {s.HeldFramedBytes}");
            T.True(rig.View.Stats.HoldOverflowDropped >= 1);
            rig.View.NotifyOnLoad();
            rig.View.Drain();
            T.Seq(new[] { a }, rig.Disp.Texts());
        });

        T.Run("[N2/P4b] a page that keeps acknowledging gets one 5 M-char reply on frames, not a reset", () =>
        {
            var (rig, page) = Open("tokBig");
            string body = "!i!:3¶OBJECT_TEX_TO_BASE_64§" + new string('A', 5 * 1024 * 1024);
            rig.Disp.OnText = (s, t) => { rig.View.Send(body); return true; };
            page.Send("!i!:3¶OBJECT_TEX_TO_BASE_64¶1¶0");
            for (int i = 0; i < 400 && !page.Got.Contains(body); i++) PumpTimed(rig, page, 16, 1);
            T.Eq(0L, rig.View.Stats.OutboundOverflows);
            T.Eq(BridgeSessionState.Open, rig.View.Current.State);
            T.Eq(1, page.Got.Count(g => g == body), "delivered once, on the session lane");
        });

        T.Run("[flush] the budget counts payload (two full 64K-char frames fit 256 KiB); a prompt page gets its window per flush", () =>
        {
            var link = new BridgeLink();
            link.EnqueueText(new string('x', 4 * 65536));
            var frames = new List<OutFrame>();
            T.Eq(2, link.TakeFrames(frames, 256 * 1024));
            T.Eq(256 * 1024, frames.Sum(f => 2 * (int)f.Header.Length));
            var (rig, page) = Open("tokWin", new BridgeViewOptions { FlushBudgetMs = 0 });
            rig.View.Send(new string('y', 2 * 1024 * 1024));
            rig.View.Flush();
            int data = 0;
            long cost = 0;
            foreach (var d in rig.Sink.Take())
            {
                if (BridgeCarrier.TryDecode(d, out var f) != BridgeError.None || f.Header.Kind != FrameKind.Data) continue;
                data++;
                cost += BridgeLink.FrameCost(f.Header);
            }
            T.Eq(7, data, "seven full text frames fill the 1 MiB window in one flush (each header counts against it)");
            T.True(cost <= rig.View.Current.Link.SendWindow, "within the credit window: " + cost);
        });

        T.Run("[N2] past MaxOutboundHardBytes even an acknowledging page goes to strings, and still gets the reply", () =>
        {
            var o = new BridgeViewOptions { MaxOutboundBytes = 64 * 1024, MaxOutboundHardBytes = 1024 * 1024 };
            var (rig, page) = Open("tokHard", o);
            string body = "!i!:4¶" + new string('B', 600 * 1024);
            rig.Disp.OnText = (s, t) => { rig.View.Send(body); return true; };
            page.Send("!i!:4¶GET");
            PumpTimed(rig, page, 16, 20);
            T.Eq(1L, rig.View.Stats.OutboundOverflows);
            T.Eq(BridgeSessionState.Legacy, rig.View.Current.State);
            T.Eq(1, page.Got.Count(g => g == body));
        });

        T.Run("[N2] a pause is not a stall", () =>
        {
            var o = new BridgeViewOptions { MaxOutboundBytes = 64 * 1024 };
            var (rig, page) = Open("tokPause", o);
            for (int i = 0; i < 20; i++) rig.View.Send("!e!big¶" + new string('e', 10_000));
            rig.View.Flush();
            var sentBeforePause = rig.Sink.Take(); // in flight: the page has not seen them yet
            T.True(rig.View.Current.Link.InflightBytes > 0);
            rig.View.Pause();
            for (int f = 0; f < 600; f++) // 10 s paused, nothing acknowledged, the queue over the cap
            {
                rig.Now += 16;
                for (int i = 0; i < 20; i++) rig.View.Send("!e!tick¶" + f + "¶" + i + new string('p', 40));
                rig.View.Flush();
            }
            T.Eq(0L, rig.View.Stats.OutboundOverflows);
            T.True(rig.View.Current.Link.QueuedCost > o.MaxOutboundBytes);
            rig.View.Resume();
            rig.Sink.Take(); // PAUSE / RESUME notices
            foreach (var d in sentBeforePause) page.Receive(d);
            PumpTimed(rig, page, 16, 200); // the page acknowledges again: everything drains, no reset
            T.Eq(0L, rig.View.Stats.OutboundOverflows);
            T.Eq(BridgeSessionState.Open, rig.View.Current.State);
            T.Eq(0L, rig.View.Current.Link.QueuedCost);
            T.Eq(20 + 600 * 20, page.Got.Count);
        });

        T.Run("[N7/T7] a revival that gave up waiting says BYE: the session Unity opens for its late HELLO is closed at once", () =>
        {
            var (rig, x) = Open("tokLate");
            rig.View.NotifyLoadStarted("https://world.example/next"); rig.View.NotifyOnLoad();
            rig.Pump(x);
            T.Eq("parked", x.State);
            x.Send("!u!parked");
            x.Revive();                 // HELLO "tokLate~1" (+ the parked string, queued until WELCOME)
            x.Bye();                    // the page timed out first: BYE for "tokLate~1", epoch 0
            rig.Deliver(x);             // Unity, busy, reads both now
            T.Eq(null, rig.View.Current);
            T.Eq(0, rig.Disp.Ran.Count);
            var y = new TestPage("tokLateY", "https://world.example/next"); y.Hello(); rig.Pump(y);
            T.True(!rig.View.Current.Held && rig.View.Current.ClaimedLoad == rig.View.LoadGeneration, rig.View.Current.ToString());
        });

        T.Run("[R1] Flush keeps to its time budget: one frame always goes, the rest next call, in order", () =>
        {
            long ticks = 0;
            long step = System.Diagnostics.Stopwatch.Frequency * 3 / 2000; // 1.5 ms per clock read
            var o = new BridgeViewOptions { FlushBudgetMs = 2, FlushClock = () => ticks += step };
            var (rig, page) = Open("tokBudget", o);
            var texts = Enumerable.Range(0, 40).Select(i => i + ":" + new string('b', 60_000)).ToList();
            foreach (var t in texts) rig.View.Send(t);
            int flushes = 0, maxFrames = 0;
            long stops0 = rig.View.Stats.FlushBudgetStops;
            while (!texts.All(t => page.Got.Contains(t)) && flushes < 200)
            {
                page.Flush(); rig.Deliver(page);
                rig.View.Flush();
                flushes++;
                int frames = 0;
                foreach (var d in rig.Sink.Take())
                {
                    if (BridgeCarrier.TryDecode(d, out var f) == BridgeError.None && f.Header.Kind == FrameKind.Data) frames++;
                    page.Receive(d);
                }
                maxFrames = Math.Max(maxFrames, frames);
            }
            T.Seq(texts, page.Got, "everything, once, in order");
            T.True(maxFrames <= 2, "at most two frames per flush under a 2 ms budget at 1.5 ms per frame: " + maxFrames);
            T.True(rig.View.Stats.FlushBudgetStops > stops0);
            T.Eq(0L, rig.View.Stats.OutboundOverflows);
        });

        T.Run("[R1] text frames written straight from the entries are byte-identical to building the body (fuzz)", () =>
        {
            var rng = new Rng(0x5eed1234);
            string[] alphabet = { "a", "z", "0", ":", "\u00b6", "\u00a7", "\ud83d\ude00", "\ud800", "\udc00", "\u2603" };
            for (int round = 0; round < 3000; round++)
            {
                int size = 1 + rng.Int(round % 3 == 0 ? 4 : 40);
                var texts = new List<string>();
                int count = 1 + rng.Int(6);
                for (int k = 0; k < count; k++)
                {
                    int[] lens = { 0, 1, 2, 9, 10, 11, 99, 100, 101 };
                    int len = rng.Int(4) == 0 ? lens[rng.Int(lens.Length)] : rng.Int(30);
                    var sb = new System.Text.StringBuilder();
                    while (sb.Length < len) sb.Append(alphabet[rng.Int(alphabet.Length)]);
                    texts.Add(sb.ToString(0, Math.Min(len, sb.Length)));
                }
                // The cursor reads the body it never builds, char for char.
                var asEntries = texts.Select(t => new OutEntry(t)).ToList();
                var whole = BridgeCarrier.PackEntries(texts);
                T.Eq(whole.Length, TextBody.Size(asEntries));
                var c = new TextCursor();
                for (int i = 0; i < whole.Length; i++) { T.Eq(whole[i], TextBody.CharAt(asEntries, c), "char " + i); c = TextBody.Advance(asEntries, c, 1); }
                var link = new BridgeLink(size, BridgeWire.DefaultWindow, int.MaxValue) { Epoch = 3, NextSeq = 5, AckSeq = 2 };
                foreach (var t in texts) link.EnqueueText(t);
                var frames = new List<OutFrame>();
                link.TakeFrames(frames);
                var got = frames.Select(f => f.ToCarrier()).ToList();
                // Reference: the body built and split (the code before the change).
                var expected = new List<string>();
                uint seq = 5;
                var queue = new Queue<string>(texts);
                while (queue.Count > 0)
                {
                    var batch = new List<string>();
                    int total = 0;
                    while (queue.Count > 0)
                    {
                        int es = BridgeCarrier.EntrySize(queue.Peek());
                        if (batch.Count > 0 && total + es > size) break;
                        batch.Add(queue.Dequeue());
                        total += es;
                        if (total >= size) break;
                    }
                    var body = BridgeCarrier.PackEntries(batch);
                    var pieces = body.Length <= size ? new List<string> { body } : BridgeCarrier.SplitText(body, size);
                    for (int i = 0; i < pieces.Count; i++)
                    {
                        ushort flags = (ushort)((i == 0 ? FrameFlags.FragFirst : 0) | (i == pieces.Count - 1 ? FrameFlags.FragLast : 0));
                        expected.Add(BridgeCarrier.EncodeText(new FrameHeader(FrameKind.Data, flags, 3, seq++, 2, pieces[i].Length), pieces[i]));
                    }
                }
                T.Seq(expected, got, "round " + round + " size " + size);
            }
        });

        T.Run("[N4] Shutdown releases the current document's share of the shared reassembly budget", () =>
        {
            var budget = new BridgeReassembler.Budget(32 * 1024 * 1024);
            var o = new BridgeViewOptions { ReassemblyBudget = budget };
            var (rig, page) = Open("tokBud", o);
            page.Send(new string('q', 150 * 1024));
            page.Flush();
            rig.View.Receive(page.TakeOut()[0]); // the first fragment only
            rig.View.Drain();
            T.True(budget.Used > 0);
            rig.View.Shutdown();
            T.Eq(0L, budget.Used);
            T.Eq(null, rig.View.Current);
            rig.View.Shutdown();
            T.Eq(0, rig.Sink.Posted.Count, "nothing is sent to the page");
        });

        T.Run("[N6] a RESET during a hold keeps framed and unframed work in arrival order", () =>
        {
            var (rig, page) = Held("tokOrder");
            page.Send("a"); rig.Pump(page);
            rig.View.Receive("u"); rig.View.Drain();          // a raw O.sendMessage string, unframed
            page.Send("b"); rig.Pump(page);
            T.Eq(3, rig.View.Current.HeldCount);
            // A protocol error (a seq gap): RESET while held.
            var gap = new FrameHeader(FrameKind.Data, FrameFlags.Complete, page.Epoch, page.Link.NextSeq + 5, page.Link.AckSeq, 0);
            rig.View.Receive(BridgeCarrier.EncodeText(gap, BridgeCarrier.PackEntries(new[] { "x" })));
            rig.View.Drain();
            T.Eq(1L, rig.View.Stats.ProtocolResets);
            rig.Back(page);
            T.Eq("legacy", page.State);
            page.Send("c"); rig.Pump(page);
            T.Eq(0L, rig.View.Stats.HoldDiscarded);
            rig.View.NotifyOnLoad();
            rig.View.Drain();
            T.Seq(new[] { "a", "u", "b", "c" }, rig.Disp.Texts(), "once each, in arrival order");
        });

        T.Run("[N8] Bind<T>: no epoch = the same delegate; a reply callback answers the document that asked, or nobody", () =>
        {
            Action<string> cb0 = _ => { };
            T.True(ReferenceEquals(cb0, BridgeContext.Bind(cb0)), "no epoch: no wrapper");
            var (rig, x) = Open("tokCbX");
            Action<string> later = null;
            rig.Disp.AutoReply = true;
            rig.Disp.OnText = (s, t) =>
            {
                if (!t.StartsWith("!i!:7¶!sso!", StringComparison.Ordinal)) return false;
                later = BridgeContext.Bind<string>(r => rig.View.Send("!i!:7¶" + r)); // what BSScene.StateOp now does
                return true;
            };
            x.Send("!i!:7¶!sso!¶get§e30="); rig.Pump(x);
            T.True(later != null);
            rig.View.NotifyLoadStarted("https://world.example/two"); rig.View.NotifyOnLoad();
            var y = new TestPage("tokCbY", "https://world.example/two"); y.Hello(); rig.Pump(y);
            rig.Disp.OnText = null;
            y.Send("!i!:7¶!gp!¶");
            later("from-X");                  // the app answers X's request now, from no context
            rig.Pump(y);
            T.Seq(new[] { "!i!:7¶ok" }, y.Got, "Y got only its own answer");
            T.Eq(1L, rig.View.Stats.LateReplies);
        });
    }
}
