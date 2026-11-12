using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using BS.Bridge;

/// <summary>Regression tests for the Hex/Relay review of M1 (probe names in brackets).</summary>
static class ReviewTests
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
        T.Eq(BridgeSessionState.Open, rig.View.Current.State);
        return (rig, page);
    }

    public static void Run()
    {
        Console.WriteLine("review findings");

        T.Run("Bind: no epoch = the same delegate; an epoch flows into actions, coroutines and nested coroutines", () =>
        {
            Action a = () => { };
            T.True(ReferenceEquals(a, BridgeContext.Bind(a)), "no epoch: no wrapper");
            IEnumerator e0 = new List<object>().GetEnumerator();
            T.True(ReferenceEquals(e0, BridgeContext.Bind(e0)));
            uint seen = 0;
            Action bound;
            using (BridgeContext.Enter(7)) bound = BridgeContext.Bind(() => seen = BridgeContext.CurrentEpoch);
            T.Eq(0u, BridgeContext.CurrentEpoch);
            bound();
            T.Eq(7u, seen);
            T.Eq(0u, BridgeContext.CurrentEpoch, "restored after");
            var seenSteps = new List<uint>();
            IEnumerator Inner() { seenSteps.Add(BridgeContext.CurrentEpoch); yield return null; seenSteps.Add(BridgeContext.CurrentEpoch); }
            IEnumerator Outer() { seenSteps.Add(BridgeContext.CurrentEpoch); yield return Inner(); seenSteps.Add(BridgeContext.CurrentEpoch); }
            IEnumerator co;
            using (BridgeContext.Enter(9)) co = BridgeContext.Bind(Outer());
            // Drive it the way Unity does: a yielded IEnumerator runs as a nested coroutine.
            var stack = new Stack<IEnumerator>();
            stack.Push(co);
            while (stack.Count > 0)
            {
                var top = stack.Peek();
                if (!top.MoveNext()) { stack.Pop(); continue; }
                if (top.Current is IEnumerator nested) stack.Push(nested);
            }
            T.Seq(new uint[] { 9, 9, 9, 9 }, seenSteps, "every step, nested ones included");
        });

        T.Run("[P2] a reply from queued work (the scheduler binds the epoch) never reaches the next document", () =>
        {
            var (rig, x) = Open("tokX");
            var view = rig.View;
            var queue = new List<Action>(); // stands in for UnityMainThreadTaskScheduler.Enqueue(Action), which now Binds
            rig.Disp.OnText = (s, t) =>
            {
                int id = BridgeView.LegacyRequestId(t, true);
                if (id > 0 && t.Contains("RELEASE")) queue.Add(BridgeContext.Bind(() => view.Send("!i!:" + id + "¶from-X")));
                else if (id > 0) view.Send("!i!:" + id + "¶ok");
                return true;
            };
            x.Send("!i!:7¶RELEASE"); rig.Pump(x);
            view.NotifyLoadStarted("https://world.example/two");
            view.NotifyOnLoad();
            var y = new TestPage("tokY", "https://world.example/two");
            y.Hello(); rig.Pump(y);
            y.Send("!i!:7¶GET"); rig.Pump(y);
            T.Seq(new[] { "!i!:7¶ok" }, y.Got);
            foreach (var a in queue) a(); // X's queued work runs now, twice
            foreach (var a in queue) a();
            rig.Pump(y);
            T.Seq(new[] { "!i!:7¶ok" }, y.Got, "Y got only its own answer");
            T.Eq(2L, view.Stats.LateReplies);
        });

        T.Run("[P2] a reply made outside any context is checked against the current document's ledger", () =>
        {
            var (rig, p) = Open("tok0");
            rig.Disp.OnText = (s, t) => true; // nothing answers in context
            p.Send("!i!:3¶GET"); rig.Pump(p);
            rig.View.Send("!i!:3¶a"); // epoch 0
            rig.View.Send("!i!:3¶b"); // a second answer
            rig.View.Send("!i!:4¶never-asked");
            rig.Pump(p);
            T.Seq(new[] { "!i!:3¶a" }, p.Got);
            T.Eq(2L, rig.View.Stats.DuplicateReplies);
        });

        T.Run("[P1] the carrier is FIFO: a gap resets at once, nothing out of order is kept", () =>
        {
            var (rig, page) = Open("tokG");
            var h = new FrameHeader(FrameKind.Data, FrameFlags.Complete, page.Epoch, page.Link.NextSeq + 5, page.Link.AckSeq, 0);
            rig.View.Receive(BridgeCarrier.EncodeText(h, BridgeCarrier.PackEntries(new[] { "x" })));
            rig.View.Drain();
            T.Eq(1L, rig.View.Stats.ProtocolResets);
            T.Eq(BridgeSessionState.Resetting, rig.View.Current.State);
        });

        T.Run("[P3] RESET for a message that failed: header ack = consumed seq; the message is re-sent and runs once", () =>
        {
            var o = new BridgeViewOptions { ReassemblyBudget = new BridgeReassembler.Budget(1024 * 1024) };
            var (rig, page) = Open("tokB", o);
            page.Send("before"); rig.Pump(page);
            string big = new string('y', 150 * 1024);
            o.ReassemblyBudget.Used = 1024 * 1024 - 140 * 1024; // another view's message, being reassembled
            page.Send(big); page.Send("after");
            page.Flush();
            foreach (var u in page.TakeOut()) rig.View.Receive(u);
            rig.View.Drain();
            rig.View.Flush();
            foreach (var d in rig.Sink.Take())
            {
                if (BridgeCarrier.TryDecode(d, out var f) == BridgeError.None && f.Header.Kind == FrameKind.Reset)
                {
                    BridgeFrame.TryDecodeClose(f.Payload, out var c);
                    T.True(f.Header.Ack <= c.ConsumedSeq, "the RESET header acknowledges nothing it reports unconsumed");
                }
                page.Receive(d);
            }
            o.ReassemblyBudget.Used -= 1024 * 1024 - 140 * 1024; // and it finished
            rig.Pump(page);
            T.Seq(new[] { "before", big, "after" }, rig.Disp.Texts(), "once each, in order");
            T.Eq(0L, o.ReassemblyBudget.Used);
        });

        T.Run("[P4] a page that stops acknowledging: past the cap its events are dropped, its replies still queue, and it catches up", () =>
        {
            var o = new BridgeViewOptions { MaxOutboundBytes = 512 * 1024 };
            var (rig, page) = Open("tokQ", o);
            rig.Disp.OnText = (s, t) => true;
            page.Send("!i!:9¶!slow¶"); rig.Pump(page);
            long maxQueued = 0;
            for (int f = 0; f < 600; f++)
            {
                rig.Now += 16; // one 60 Hz frame
                for (int i = 0; i < 200; i++) rig.View.Send("!e!transform¶" + f + "¶" + i + "¶0.1234,0.5678,0.9012,0.1,0.2,0.3,1");
                rig.View.Flush();
                rig.Sink.Take(); // never acknowledged
                maxQueued = Math.Max(maxQueued, rig.View.Current.Link.QueuedCost);
            }
            T.True(rig.View.Stats.OutboundDropped > 0);
            T.True(maxQueued <= o.MaxOutboundBytes + 200 * 2 * 70, "the queue stays at the cap: " + maxQueued);
            T.Eq(0L, rig.View.Stats.ProtocolResets);
            T.Eq(BridgeSessionState.Open, rig.View.Current.State);
            T.Eq(1, rig.Logs.Count(l => l.Contains("dropping its events")), "said once");
            long dropped = rig.View.Stats.OutboundDropped;
            rig.View.Send("!i!:9¶answer"); // over the cap, and still queued
            rig.View.Flush();
            T.Eq(dropped, rig.View.Stats.OutboundDropped);
            T.True(rig.View.Current.Link.HasOutbound);
        });

        T.Run("a burst of replies past the hard cap fails those answers, each await settles once, and the session stays", () =>
        {
            var o = new BridgeViewOptions { MaxOutboundBytes = 64 * 1024, MaxOutboundHardBytes = 512 * 1024 };
            var (rig, page) = Open("tokBurst", o);
            var body = new string('r', 16000);
            rig.Disp.OnText = (s, t) => { rig.View.Send("!i!:" + BridgeView.LegacyRequestId(t, true) + "¶" + body); return true; };
            var burst = rig.View.Current;
            const int asked = 100;
            page.Got.Clear();
            for (int i = 1; i <= asked; i++) page.Send("!i!:" + i + "¶GET");
            rig.Pump(page, 400);
            T.True(rig.View.Current == burst, "the same session");
            T.Eq(BridgeSessionState.Open, burst.State);
            T.Eq(0L, rig.View.Stats.OutboundCloses);
            var replies = page.Got.Where(g => g.StartsWith("!i!:")).ToList();
            T.Eq(asked, replies.Count, "every await settles");
            T.Eq(asked, replies.Select(r => r.Substring(0, r.IndexOf('¶'))).Distinct().Count(), "once each");
            int failed = replies.Count(r => r.Contains("¶!ree!¶"));
            T.True(failed > 0 && failed < asked, "failed " + failed);
        });

        T.Run("a page past the hard cap that goes on acknowledging is not closed, however much it asks", () =>
        {
            var o = new BridgeViewOptions { MaxOutboundBytes = 64 * 1024, MaxOutboundHardBytes = 128 * 1024, MaxRepliesPastCap = 8, FlushMaxBytes = 32 * 1024 };
            var (rig, page) = Open("tokReading", o);
            var body = new string('r', 16000);
            rig.Disp.OnText = (s, t) => { rig.View.Send("!i!:" + BridgeView.LegacyRequestId(t, true) + "¶" + body); return true; };
            var reading = rig.View.Current;
            int asked = 0;
            for (int round = 0; round < 60; round++)
            {
                for (int i = 0; i < 5; i++) page.Send("!i!:" + (++asked) + "¶GET");
                rig.Pump(page, 1);
            }
            // Only a reply past the cap is dropped here, so this says it went past the cap and past the limit.
            T.True(rig.View.Stats.OutboundDropped > o.MaxRepliesPastCap, "failed " + rig.View.Stats.OutboundDropped);
            T.True(rig.View.Current == reading, "the same session");
            T.Eq(0L, rig.View.Stats.OutboundCloses);
        });

        T.Run("a frozen page past the hard cap is left alone: it asks nothing, so the queue stops growing", () =>
        {
            var o = new BridgeViewOptions { MaxOutboundBytes = 64 * 1024, MaxOutboundHardBytes = 128 * 1024, MaxRepliesPastCap = 8 };
            var (rig, page) = Open("tokFrozen", o);
            var body = new string('r', 16000);
            rig.Disp.OnText = (s, t) => { rig.View.Send("!i!:" + BridgeView.LegacyRequestId(t, true) + "¶" + body); return true; };
            var frozen = rig.View.Current;
            for (int i = 1; i <= 8; i++) page.Send("!i!:" + i + "¶GET");
            page.Flush();
            foreach (var u in page.TakeOut()) rig.View.Receive(u);
            rig.View.Drain();
            rig.View.Flush();
            long queued = frozen.Link.QueuedCost;
            for (int i = 0; i < 100; i++) { rig.View.Flush(); rig.Sink.Take(); }
            T.True(rig.View.Current == frozen, "the same session");
            T.True(frozen.Link.QueuedCost <= queued, "no growth");
            T.Eq(0L, rig.View.Stats.OutboundCloses);
        });

        T.Run("a page that keeps asking and never acknowledges is closed past the cap on replies, framed or raw, and the queue is freed", () =>
        {
            foreach (var raw in new[] { false, true })
            {
                var o = new BridgeViewOptions { MaxOutboundBytes = 64 * 1024, MaxOutboundHardBytes = 512 * 1024, MaxRepliesPastCap = 64 };
                var (rig, page) = Open("tokFlood" + raw, o);
                page.HonorWindow = false;
                var body = new string('r', 4000);
                rig.Disp.OnText = (s, t) => { rig.View.Send("!i!:" + BridgeView.LegacyRequestId(t, true) + "¶" + body); return true; };
                var flooded = rig.View.Current;
                long maxQueued = 0;
                int asked = 0;
                string close = null;
                while (rig.View.Current == flooded && asked < 5000)
                {
                    string request = "!i!:" + (asked++ % 9999 + 1) + "¶GET";
                    if (raw) rig.View.Receive(request);
                    else { page.Send(request); page.Flush(); foreach (var u in page.TakeOut()) rig.View.Receive(u); }
                    rig.View.Drain();
                    rig.View.Flush();
                    foreach (var d in rig.Sink.Take()) // read by nobody: nothing is ever acknowledged
                        if (BridgeCarrier.TryDecode(d, out var f) == BridgeError.None && f.Header.Kind == FrameKind.Close) close = d;
                    maxQueued = Math.Max(maxQueued, flooded.Link.QueuedCost);
                }
                T.Eq(null, rig.View.Current);
                T.Eq(BridgeSessionState.Closed, flooded.State);
                T.Eq(BridgeReason.Overflow, flooded.CloseReason);
                T.Eq(1L, rig.View.Stats.OutboundCloses);
                T.True(maxQueued <= o.MaxOutboundHardBytes + 2 * 4100 * 2 + (o.MaxRepliesPastCap + 2) * 256, "queued " + maxQueued);
                T.Eq(0L, flooded.Link.QueuedCost, "the queue is freed");
                T.True(close != null, "the page is told");
                // More of the same now reaches nobody, and nothing queues.
                rig.View.Receive("!i!:1¶GET");
                rig.View.Drain();
                rig.View.Flush();
                T.True(!rig.View.HasOutbound);
                // A page that was only frozen comes back: it parks, and revives like any closed document.
                page.Receive(close);
                T.Eq("parked", page.State);
                page.Revive(); rig.Pump(page);
                T.Eq("open", page.State);
            }
        });

        T.Run("[P5] the drain budget counts ingest too; stale frames are refused from the header", () =>
        {
            var rig = new Rig();
            rig.View.NotifyLoadStarted("https://world.example/space"); // held: OnLoad not yet
            var page = new TestPage("tokH"); page.Hello(); rig.Pump(page);
            for (int i = 0; i < 1000; i++) rig.View.Receive("!e!raw¶" + i);
            rig.View.Drain(int.MaxValue, () => true);
            T.Eq(999, rig.View.InboundCount, "one unit per exhausted call");
            var (rig2, p2) = Open("tokS");
            var payload = BridgeOps.Encode(BridgeOp.LegacyText, new byte[256 * 1024 - 8]);
            var stale = BridgeCarrier.EncodeBinary(new FrameHeader(FrameKind.Data, FrameFlags.Complete, p2.Epoch + 7, 2, 0, 0), payload);
            for (int i = 0; i < 100; i++) rig2.View.Receive(stale);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            rig2.View.Drain();
            T.Eq(100L, rig2.View.Stats.StaleFrames);
            T.True(sw.ElapsedMilliseconds < 50, "stale frames were not base64-decoded: " + sw.ElapsedMilliseconds + " ms");
        });

        T.Run("[P5] unframed strings held for a document past the cap are dropped and counted", () =>
        {
            var o = new BridgeViewOptions { MaxHeldBytes = 1000 };
            var rig = new Rig(o);
            rig.View.NotifyLoadStarted("https://world.example/space");
            var page = new TestPage("tokCap"); page.Hello(); rig.Pump(page);
            for (int i = 0; i < 100; i++) rig.View.Receive("!e!raw-" + i.ToString("000"));
            rig.View.Drain();
            T.True(rig.View.Current.HeldBytes <= 1000, "held " + rig.View.Current.HeldBytes);
            T.True(rig.View.Stats.HoldOverflowDropped > 0);
        });

        T.Run("[P7] a fragmented message is copied once into its strings", () =>
        {
            var o = new BridgeViewOptions { RecvWindow = BridgeWire.MaxWindow };
            var (rig, page) = Open("tokM", o);
            string huge = new string('z', 15 * 1024 * 1024); // 30 MiB of UTF-16, under the 32 MiB message cap
            page.Send(huge);
            long drainAlloc = 0;
            int frames = 0;
            for (int round = 0; round < 2000 && !rig.Disp.Texts().Any(t => t.Length == huge.Length); round++)
            {
                page.Flush();
                var up = page.TakeOut();
                frames += up.Count;
                foreach (var u in up) rig.View.Receive(u);
                long a0 = GC.GetAllocatedBytesForCurrentThread();
                rig.View.Drain();
                drainAlloc += GC.GetAllocatedBytesForCurrentThread() - a0;
                rig.View.Flush();
                foreach (var d in rig.Sink.Take()) page.Receive(d);
            }
            T.True(rig.Disp.Texts().Any(t => t.Length == huge.Length), "dispatched");
            double mib = drainAlloc / (1024.0 * 1024);
            Console.WriteLine($"       {frames} frames of a 15 M-char (30 MiB) message: {mib:F0} MiB allocated by Unity while ingesting");
            T.True(mib < 40, "about one copy: " + mib);
        });

        T.Run("[P8] CLOSE(superseded) reports held work unconsumed", () =>
        {
            var rig = new Rig();
            rig.View.NotifyLoadStarted("https://world.example/space");
            var a = new TestPage("tokA"); a.Hello(); rig.Pump(a);
            uint firstHeld = a.Link.NextSeq;
            a.Send("held-1"); rig.Pump(a);
            T.Eq(1, rig.View.Current.HeldCount);
            var b = new TestPage("tokB2"); b.Hello(); rig.Deliver(b);
            foreach (var d in rig.Sink.Take())
            {
                if (BridgeCarrier.TryDecode(d, out var f) == BridgeError.None && f.Header.Kind == FrameKind.Close)
                {
                    BridgeFrame.TryDecodeClose(f.Payload, out var c);
                    T.Eq(BridgeReason.Superseded, c.Reason);
                    T.True(c.ConsumedSeq < firstHeld && f.Header.Ack <= c.ConsumedSeq, "consumed " + c.ConsumedSeq + " vs held from " + firstHeld);
                }
                a.Receive(d);
            }
            T.Eq("parked", a.State);
            T.Seq(new[] { "held-1" }, a.Parked, "the page keeps it (parked), never replayed into B");
        });

        T.Run("[P9] BYE mid-message abandons it without a RESET and frees the budget", () =>
        {
            var budget = new BridgeReassembler.Budget(32 * 1024 * 1024);
            var o = new BridgeViewOptions { ReassemblyBudget = budget, RecvWindow = 70000 };
            var (rig, page) = Open("tokP", o);
            page.Send(new string('q', 150 * 1024));
            page.Flush();
            foreach (var u in page.TakeOut()) rig.View.Receive(u);
            rig.View.Drain();
            T.True(budget.Used > 0);
            rig.Sink.Take();
            page.Bye();
            rig.Deliver(page);
            T.Eq(0L, rig.View.Stats.ProtocolResets);
            T.Eq(null, rig.View.Current);
            T.Eq(0L, budget.Used);
        });

        T.Run("[P10] what is queued is per document: a paused reply for X never reaches Y", () =>
        {
            var rig = new Rig();
            rig.Disp.AutoReply = true;
            rig.View.NotifyLoadStarted("https://world.example/space"); rig.View.NotifyOnLoad();
            var x = new TestPage("tokOX"); x.Hello(); rig.Pump(x);
            rig.View.Pause();
            x.Send("!i!:3¶GET"); rig.Pump(x);
            rig.View.NotifyLoadStarted("https://world.example/two"); rig.View.NotifyOnLoad();
            var y = new TestPage("tokOY", "https://world.example/two"); y.Hello(); rig.Deliver(y);
            rig.View.Resume();
            rig.Pump(y);
            T.Eq(0, y.Got.Count);
        });

        T.Run("[P11] a HELLO is always seq 1: anything else is refused", () =>
        {
            var rig = new Rig();
            rig.View.NotifyLoadStarted("https://world.example/space"); rig.View.NotifyOnLoad();
            var hello = new HelloPayload { Features = BridgeFeature.TextForm, SdkVersion = "x", DocToken = "tokW", Href = "https://world.example/space" };
            rig.View.Receive(BridgeCarrier.EncodeBinary(new FrameHeader(FrameKind.Data, FrameFlags.Complete, 0, uint.MaxValue, 0, 0), BridgeOps.Encode(BridgeOp.Hello, hello.Encode())));
            rig.View.Drain();
            T.Eq(null, rig.View.Current);
            T.Eq(1L, rig.View.Stats.Refusals);
        });

        T.Run("[P13] navigation: the replaced page parks its strings; a revival waits for its own load and never displaces a newer document", () =>
        {
            var (rig, x) = Open("tokN");
            x.Send("!u!A"); rig.Pump(x);
            rig.View.NotifyLoadStarted("https://other.example/world"); // did-start-navigation; X lives until commit
            rig.View.NotifyOnLoad();
            x.Send("!u!B"); x.Send("!i!:4¶CREATE¶x");
            rig.Pump(x);
            T.Seq(new[] { "!u!A" }, rig.Disp.Texts(), "nothing of X's after the navigation ran");
            T.Eq("parked", x.State);
            // Case 1: the new document arrives: X (still parked) would be refused if it revived now.
            var y = new TestPage("tokNY", "https://other.example/world"); y.Hello(); rig.Pump(y);
            T.Eq(2u, y.Epoch);
            x.Revive(); rig.Pump(x);
            T.Eq("closed", x.State, "refused: a newer document owns the view");
            T.Eq(2u, rig.View.Current.Epoch);
            T.Seq(new[] { "!u!A" }, rig.Disp.Texts());
            // Case 2 (N3): a navigation slower than the park grace + the hold (or one that never commits): the revival
            // is accepted and waits. It never takes the pending load, so past the hold timeout it stays held: nothing
            // of it runs in that load's world, and the slow document, when it comes, claims its load at once.
            var (rig2, z) = Open("tokZ");
            rig2.View.NotifyLoadStarted("https://other.example/slow");
            rig2.View.NotifyOnLoad();
            z.Send("!u!late"); rig2.Pump(z);
            T.Eq("parked", z.State);
            z.Revive(); rig2.Pump(z);
            T.Eq("open", z.State);
            var rs = rig2.View.Current;
            T.True(rs.Held && rs.ClaimedLoad == 0, "it does not take the load that closed it");
            rig2.Now += 5000; rig2.View.Tick(); rig2.Pump(z);
            rig2.Now += 20000; rig2.View.Tick(); rig2.Pump(z);
            T.Eq(0, rig2.Disp.Ran.Count, "nothing ran in the pending load's world");
            T.True(rs.Held && rs.ClaimedLoad == 0 && rs.HoldExpired, rs.ToString());
            var w = new TestPage("tokZW", "https://other.example/slow"); w.Hello(); rig2.Deliver(w);
            rig2.View.Flush();
            foreach (var d in rig2.Sink.Take()) { z.Receive(d); w.Receive(d); } // each ignores the other's epoch
            T.Eq(BridgeSessionState.Closed, rs.State);
            T.Eq("parked", z.State, "the revival was told (superseded) and keeps its strings");
            T.Seq(new[] { "!u!late" }, z.Parked);
            rig2.Pump(w);
            T.True(!rig2.View.Current.Held && rig2.View.Current.ClaimedLoad == rig2.View.LoadGeneration, "the new document has its load: " + rig2.View.Current);
            w.Send("!u!new"); rig2.Pump(w);
            T.Seq(new[] { "!u!new" }, rig2.Disp.Texts());
            T.Eq(1L, rig2.View.Stats.Revivals);
            // Case 3: a LoadStarted while a revival waits is a newer document's: the revival is closed, not given it.
            var (rig3, v) = Open("tokV");
            rig3.View.NotifyLoadStarted("https://other.example/cancelled"); rig3.View.NotifyOnLoad();
            v.Send("!u!late"); rig3.Pump(v);
            v.Revive(); rig3.Pump(v);
            var vs = rig3.View.Current;
            rig3.View.NotifyLoadStarted("https://other.example/next"); rig3.View.NotifyOnLoad();
            T.Eq(BridgeSessionState.Closed, vs.State);
            T.Eq(BridgeReason.Navigated, vs.CloseReason);
            rig3.Pump(v);
            T.Eq("parked", v.State);
            var n = new TestPage("tokVN", "https://other.example/next"); n.Hello(); rig3.Pump(n);
            T.True(!rig3.View.Current.Held && rig3.View.Current.ClaimedLoad == rig3.View.LoadGeneration, rig3.View.Current.ToString());
            T.Eq(0, rig3.Disp.Ran.Count);
        });

        T.Run("a revival is never held for ever: at the limit it is closed for good, and the pending load stays free", () =>
        {
            var (rig, z) = Open("tokFor");
            rig.View.NotifyLoadStarted("https://other.example/never-commits");
            rig.View.NotifyOnLoad();
            z.Send("!u!late"); rig.Pump(z);
            T.Eq("parked", z.State);
            z.Revive(); rig.Pump(z);
            var rs = rig.View.Current;
            rig.Now += 5000; rig.View.Tick();
            T.True(rs.Held && rs.HoldExpired);
            rig.Now += rig.View.Options.RevivalHoldLimitMs - 5000 - 1; rig.View.Tick();
            T.Eq(BridgeSessionState.Open, rs.State, "not yet");
            rig.Now += 1; rig.View.Tick();
            T.Eq(BridgeSessionState.Closed, rs.State);
            T.Eq(BridgeReason.Timeout, rs.CloseReason);
            T.Eq(1L, rig.View.Stats.RevivalsGivenUp);
            T.Eq(null, rig.View.Current);
            T.Eq(0, rig.Disp.Ran.Count, "nothing of it ran in that load's world");
            rig.Pump(z);
            T.Eq("closed", z.State, "told not to ask again");
            var w = new TestPage("tokForW", "https://other.example/never-commits"); w.Hello(); rig.Pump(w);
            T.True(!rig.View.Current.Held && rig.View.Current.ClaimedLoad == rig.View.LoadGeneration, "a late document still gets its load: " + rig.View.Current);
        });

        T.Run("HELLO ahead of its LoadStarted, claiming another load of the same host: closed by its own LoadStarted, and not held for good", () =>
        {
            var rig = new Rig(new BridgeViewOptions { LoadStartedMayTrail = true });
            rig.View.NotifyLoadStarted("https://world.example/a"); // nobody claims this one
            rig.View.NotifyOnLoad();
            var b = new TestPage("tokWrong", "https://world.example/b");
            b.Hello(); rig.Pump(b);
            T.Eq(rig.View.LoadGeneration, rig.View.Current.ClaimedLoad, "it claimed the wrong load");
            b.Send("!sst!"); rig.Pump(b);
            rig.View.NotifyLoadStarted("https://world.example/b"); // its own
            rig.View.NotifyOnLoad();
            rig.Pump(b);
            T.Eq("parked", b.State);
            b.Revive(); rig.Pump(b);
            var rs = rig.View.Current;
            for (int ms = 0; ms <= rig.View.Options.RevivalHoldLimitMs; ms += 100) { rig.Now += 100; rig.View.Tick(); }
            T.Eq(null, rig.View.Current);
            T.Eq(BridgeSessionState.Closed, rs.State);
            rig.Pump(b);
            T.Eq("closed", b.State);
        });

        T.Run("pagehide BYE out of band: what came before it still runs, then the document closes", () =>
        {
            var (rig, p) = Open("tokBye");
            p.Send("last"); p.Flush();
            p.Bye();
            rig.Deliver(p);
            T.Seq(new[] { "last" }, rig.Disp.Texts());
            T.Eq(null, rig.View.Current);
        });
    }
}
