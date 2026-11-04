using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BS.Bridge;

/// <summary>Both sides in one process: the real BridgeView against the scripted TestPage.</summary>
static class SimTests
{
    static List<string> Decoded(IEnumerable<string> posted, out List<FrameHeader> headers)
    {
        headers = new List<FrameHeader>();
        var texts = new List<string>();
        foreach (var s in posted)
        {
            if (!BridgeCarrier.IsCarrier(s)) continue;
            BridgeCarrier.TryDecode(s, out var f);
            headers.Add(f.Header);
        }
        return texts;
    }

    public static void Run()
    {
        Console.WriteLine("session simulations");

        T.Run("handshake: WELCOME at Flush; legacy encoding until the page acknowledges the epoch, frames after", () =>
        {
            var rig = new Rig();
            var page = new TestPage("p1");
            page.Hello();
            rig.Deliver(page);
            var s = rig.View.Current;
            T.Eq(BridgeSessionState.AwaitingAck, s.State);
            rig.View.Send("!e!before-ack");
            rig.View.Flush();
            var down = rig.Sink.Take();
            T.Eq(2, down.Count, "legacy batch + WELCOME");
            T.True(down[0].StartsWith("‽", StringComparison.Ordinal), "unacked: legacy batch");
            T.True(BridgeCarrier.IsCarrier(down[1]));
            foreach (var d in down) page.Receive(d);
            T.Eq("open", page.State);
            T.Eq(s.Epoch, page.Epoch);
            page.Flush(); // the third leg: a bare ACK stamped with the epoch
            rig.Deliver(page);
            T.Eq(BridgeSessionState.Open, s.State);
            T.True(s.HandshakeComplete);
            rig.View.Send("!e!after-ack");
            rig.Back(page);
            T.Seq(new[] { "!e!before-ack", "!e!after-ack" }, page.Got);
            T.Eq(1L, rig.View.Stats.LegacyBatchesOut);
        });

        T.Run("Windows order: LoadStarted -> OnLoad -> HELLO is released at once", () =>
        {
            var rig = new Rig();
            rig.View.NotifyLoadStarted("https://world.example/space");
            rig.View.NotifyOnLoad();
            var page = new TestPage("w1");
            page.Hello();
            rig.Pump(page);
            T.True(!rig.View.Current.Held);
            page.Send("!sst!"); page.Send("!i!:1¶!gp!¶");
            rig.Pump(page);
            T.Seq(new[] { "!sst!", "!i!:1¶!gp!¶" }, rig.Disp.Texts());
        });

        T.Run("hold followed by LoadStarted (Android order): held until LoadStarted AND OnLoad, old document never replayed", () =>
        {
            var rig = new Rig(new BridgeViewOptions { LoadStartedMayTrail = true });
            rig.View.NotifyLoadStarted("https://a.example/one");
            rig.View.NotifyOnLoad();
            var p1 = new TestPage("doc1", "https://a.example/one");
            p1.Hello();
            rig.Pump(p1);
            var s1 = rig.View.Current;
            T.True(!s1.Held, "doc1 released");
            // doc1 has ops in flight when doc2 starts: they are queued at Unity but not dispatched yet.
            p1.Send("doc1-late"); p1.Flush();
            var lateFromDoc1 = p1.TakeOut();
            // doc2's HELLO and first ops overtake its LoadStarted.
            var p2 = new TestPage("doc2", "https://b.example/two");
            p2.Hello();
            rig.Deliver(p2);
            var s2 = rig.View.Current;
            T.True(s2 != s1 && s1.State == BridgeSessionState.Closed, "doc1 superseded");
            T.True(s2.Held && s2.ClaimedLoad == 0, "doc2 waits for its LoadStarted");
            rig.Back(p2); p2.Flush(); rig.Deliver(p2); // WELCOME + ack
            p2.Send("!sst!"); p2.Send("!i!:1¶!gp!¶");
            rig.Pump(p2);
            foreach (var s in lateFromDoc1) rig.View.Receive(s);
            rig.View.Drain();
            T.Eq(0, rig.Disp.Ran.Count(r => r.epoch == s2.Epoch), "held");
            T.True(!rig.Disp.Texts().Contains("doc1-late"), "doc1's late frame dropped");
            rig.View.NotifyLoadStarted("https://b.example/two");
            rig.View.Drain();
            T.Eq(0, rig.Disp.Ran.Count(r => r.epoch == s2.Epoch), "LoadStarted alone does not release");
            rig.View.NotifyOnLoad();
            rig.View.Drain();
            T.Seq(new[] { "!sst!", "!i!:1¶!gp!¶" }, rig.Disp.Texts(s2.Epoch));
            T.True(!rig.Disp.Texts().Contains("doc1-late"));
            T.True(rig.View.Stats.StaleFrames >= 1);
        });

        T.Run("Android: an unclaimed LoadStarted for another host is not taken; the hold times out and is counted", () =>
        {
            var rig = new Rig(new BridgeViewOptions { LoadStartedMayTrail = true, HoldTimeoutMs = 5000 });
            rig.View.NotifyLoadStarted("https://error.example/");
            rig.View.NotifyOnLoad();
            var p = new TestPage("x", "https://c.example/");
            p.Hello();
            rig.Pump(p);
            p.Send("held"); rig.Pump(p);
            T.True(rig.View.Current.Held && rig.View.Current.ClaimedLoad == 0);
            rig.Now += 4999; rig.View.Tick(); rig.View.Drain();
            T.Eq(0, rig.Disp.Ran.Count);
            rig.Now += 1; rig.View.Tick(); rig.View.Drain();
            T.Seq(new[] { "held" }, rig.Disp.Texts());
            T.Eq(1L, rig.View.Stats.HoldTimeouts);
        });

        T.Run("navigation mid-request: the late reply is dropped, the new document's same id is answered once", () =>
        {
            var rig = new Rig();
            rig.View.NotifyLoadStarted("https://w.example/1"); rig.View.NotifyOnLoad();
            var p1 = new TestPage("n1"); p1.Hello(); rig.Pump(p1);
            Action reply1 = null;
            rig.Disp.OnText = (s, t) =>
            {
                if (t.StartsWith("!i!:7¶!slow", StringComparison.Ordinal))
                {
                    var ec = ExecutionContext.Capture(); // what an await or a scheduler task carries
                    reply1 = () => ExecutionContext.Run(ec, _ => rig.View.Send("!i!:7¶late"), null);
                    return true;
                }
                return false;
            };
            rig.Disp.AutoReply = true;
            p1.Send("!i!:7¶!slow¶"); rig.Pump(p1);
            T.True(reply1 != null);
            T.Eq(1, rig.View.Current.OutstandingLegacyRequests);
            // The user walks through a portal.
            rig.View.NotifyLoadStarted("https://w.example/2");
            var closes = rig.Sink.Take();
            T.Eq(1, closes.Count, "CLOSE to the old document");
            rig.View.NotifyOnLoad();
            var p2 = new TestPage("n2"); p2.Hello(); rig.Pump(p2);
            p2.Send("!i!:7¶!gp!¶"); rig.Pump(p2);
            reply1();       // the old document's handler finishes now
            rig.Pump(p2);
            T.Seq(new[] { "!i!:7¶ok" }, p2.Got, "only the new document's answer");
            T.Eq(1L, rig.View.Stats.LateReplies);
        });

        T.Run("reused request id across documents never crosses, even when the old reply comes after the new request", () =>
        {
            var rig = new Rig();
            var replies = new List<(uint epoch, Action send)>();
            rig.Disp.OnText = (s, t) =>
            {
                int id = BridgeView.LegacyRequestId(t, true);
                if (id <= 0) return false;
                uint ep = BridgeContext.CurrentEpoch;
                // Answer from another thread later, after an await: the epoch must flow there.
                var tcs = new TaskCompletionSource<bool>();
                var task = Task.Run(async () => { await tcs.Task; rig.View.Send("!i!:" + id + "¶from-" + BridgeContext.CurrentEpoch); });
                replies.Add((ep, () => { tcs.SetResult(true); task.Wait(); }));
                return true;
            };
            rig.View.NotifyLoadStarted("https://w.example/1"); rig.View.NotifyOnLoad();
            var p1 = new TestPage("r1"); p1.Hello(); rig.Pump(p1);
            p1.Send("!i!:5¶!gp!¶"); rig.Pump(p1);
            rig.View.NotifyLoadStarted("https://w.example/2"); rig.View.NotifyOnLoad();
            var p2 = new TestPage("r2"); p2.Hello(); rig.Pump(p2);
            p2.Send("!i!:5¶!gp!¶"); rig.Pump(p2);
            T.Eq(2, replies.Count);
            replies[0].send(); // doc1's answer, id 5, arrives while doc2's id 5 is outstanding
            rig.Pump(p2);
            T.Eq(0, p2.Got.Count, "doc1's reply did not settle doc2's request");
            replies[1].send();
            rig.Pump(p2);
            T.Seq(new[] { "!i!:5¶from-" + replies[1].epoch }, p2.Got);
            replies.Clear();
        });

        T.Run("exactly one reply per request: duplicates dropped, id 10000 never tracked", () =>
        {
            var rig = new Rig();
            rig.View.NotifyLoadStarted("https://w.example/"); rig.View.NotifyOnLoad();
            var p = new TestPage("one"); p.Hello(); rig.Pump(p);
            rig.Disp.OnText = (s, t) =>
            {
                if (t.StartsWith("!i!:3¶", StringComparison.Ordinal)) { rig.View.Send("!i!:3¶a"); rig.View.Send("!i!:3¶b"); return true; }
                if (t.StartsWith("!i!:10000¶", StringComparison.Ordinal)) { rig.View.Send("!i!:10000¶x"); return true; }
                return false;
            };
            p.Send("!i!:3¶!gp!¶"); p.Send("!i!:10000¶!cu!¶"); rig.Pump(p);
            T.Seq(new[] { "!i!:3¶a" }, p.Got);
            T.Eq(2L, rig.View.Stats.DuplicateReplies);
            T.Eq(0, rig.View.Current.OutstandingLegacyRequests);
        });

        T.Run("typed request: REPLY sent after the handler, once, to the asking document", () =>
        {
            var rig = new Rig();
            rig.View.NotifyLoadStarted("https://w.example/"); rig.View.NotifyOnLoad();
            var p = new TestPage("typed"); p.Hello(); rig.Pump(p);
            p.Request(0x80, 9); rig.Pump(p);
            T.Seq(new[] { (9u, ReplyStatus.Ok) }, p.Replies);
            rig.View.Reply(rig.View.Current.Epoch, 9, ReplyStatus.Ok, ReadOnlySpan<byte>.Empty);
            rig.Pump(p);
            T.Eq(1, p.Replies.Count);
            T.Eq(1L, rig.View.Stats.DuplicateReplies);
        });

        T.Run("credits exhausted while held, then refilled: page blocks, nothing lost or duplicated", () =>
        {
            var rig = new Rig(new BridgeViewOptions { LoadStartedMayTrail = true, RecvWindow = 4096, HoldAckLimit = 1000 });
            var p = new TestPage("credit"); p.Hello(); rig.Pump(p);
            p.Link.FragmentSize = 512;
            var texts = Enumerable.Range(0, 60).Select(i => "t" + i + ":" + new string('x', 150)).ToList();
            foreach (var t in texts) p.Send(t);
            rig.Pump(p);
            T.True(p.Link.Blocked, "page stopped on the window");
            T.True(rig.View.Current.Held);
            T.Eq(0, rig.Disp.Ran.Count);
            rig.View.NotifyLoadStarted("https://world.example/space");
            rig.View.NotifyOnLoad();
            rig.Pump(p);
            for (int i = 0; i < 20 && p.Link.HasOutbound; i++) rig.Pump(p);
            T.Seq(texts, rig.Disp.Texts());
            T.Eq(0L, rig.View.Stats.ProtocolResets);
        });

        T.Run("a page that ignores the window is reset; RESET makes it re-send exactly the unconsumed strings", () =>
        {
            var rig = new Rig(new BridgeViewOptions { RecvWindow = 2048 });
            rig.View.NotifyLoadStarted("https://w.example/"); rig.View.NotifyOnLoad();
            var p = new TestPage("greedy"); p.Hello(); rig.Pump(p);
            p.HonorWindow = false;
            p.Link.FragmentSize = 600;
            var texts = Enumerable.Range(0, 20).Select(i => "g" + i + new string('y', 200)).ToList();
            foreach (var t in texts) p.Send(t);
            rig.Pump(p);
            T.Eq(1L, rig.View.Stats.ProtocolResets);
            T.Eq("legacy", p.State);
            T.Eq(BridgeSessionState.Legacy, rig.View.Current.State);
            T.Seq(texts, rig.Disp.Texts(), "every string once, in order, across frames and the legacy re-send");
        });

        T.Run("lost HELLO: the page falls back to strings, Unity runs them without a session, in order", () =>
        {
            var rig = new Rig();
            rig.View.NotifyLoadStarted("https://w.example/"); rig.View.NotifyOnLoad();
            var p = new TestPage("lost");
            p.Hello();
            var hello = p.TakeOut(); // lost
            p.Send("a"); p.Send("b");
            p.FallBack(BridgeReason.Timeout);
            p.Send("c");
            rig.Pump(p);
            T.Seq(new[] { "a", "b", "c" }, rig.Disp.Texts(0));
            T.Eq(null, rig.View.Current);
        });

        T.Run("lost-then-late HELLO: Unity opens, the page declines (LATE_WELCOME), its strings run once in its context", () =>
        {
            var rig = new Rig();
            rig.View.NotifyLoadStarted("https://w.example/"); rig.View.NotifyOnLoad();
            var p = new TestPage("late");
            p.Hello();
            var hello = p.TakeOut();
            p.Send("a");
            p.FallBack(BridgeReason.Timeout);
            p.Flush();
            var fallback = p.TakeOut();
            foreach (var s in hello) rig.View.Receive(s);   // the HELLO turns up after all
            foreach (var s in fallback) rig.View.Receive(s);
            rig.View.Drain();
            rig.Back(p);    // WELCOME reaches a page that already gave up
            rig.Deliver(p); // its LEGACY(LATE_WELCOME)
            var s1 = rig.View.Current;
            T.Eq(BridgeSessionState.Legacy, s1.State);
            p.Send("!i!:2¶!gp!¶");
            rig.Disp.AutoReply = true;
            rig.Pump(p);
            T.Seq(new[] { "a", "!i!:2¶!gp!¶" }, rig.Disp.Texts());
            T.Seq(new[] { "!i!:2¶ok" }, p.Got);
        });

        T.Run("refusals: about:, iframes, kill switch, closed tokens; duplicate HELLO keeps the epoch", () =>
        {
            var rig = new Rig();
            var a = new TestPage("about", "about:blank"); a.Hello(); rig.Pump(a);
            T.Eq("legacy", a.State); T.Eq(null, rig.View.Current);
            var k = new Rig(new BridgeViewOptions { SessionsEnabled = false });
            var kp = new TestPage("kill"); kp.Hello(); k.Pump(kp);
            T.Eq("legacy", kp.State); T.Eq(null, k.View.Current);
            rig.View.NotifyLoadStarted("https://w.example/"); rig.View.NotifyOnLoad();
            var d = new TestPage("dup"); d.Hello();
            var helloAgain = new List<string>(d.Out);
            rig.Pump(d);
            uint epoch = rig.View.Current.Epoch;
            foreach (var s in helloAgain) rig.View.Receive(s);
            rig.View.Drain();
            T.Eq(epoch, rig.View.Current.Epoch);
            T.Eq(1L, rig.View.Stats.Sessions, "no second session for the same token");
            rig.View.NotifyLoadStarted("https://w.example/next"); // replaces the document that claimed the first load
            var again = new TestPage("dup"); again.Hello(); rig.Pump(again);
            T.Eq("legacy", again.State, "a closed document's token is refused");
            T.Eq(null, rig.View.Current);
        });

        T.Run("host-only kinds from a page are dropped; fragmented text messages reassemble", () =>
        {
            var rig = new Rig();
            rig.View.NotifyLoadStarted("https://w.example/"); rig.View.NotifyOnLoad();
            var p = new TestPage("frag"); p.Hello(); rig.Pump(p);
            var s = rig.View.Current;
            rig.View.Receive(BridgeCarrier.EncodeBinary(new FrameHeader(FrameKind.Reset, 0, s.Epoch, 0, 0, 0), BridgeFrame.EncodeClose(6, 0, "")));
            rig.View.Drain();
            T.Eq(1L, rig.View.Stats.HostOnlyFromPage);
            T.Eq(BridgeSessionState.Open, s.State);
            var big = "!sg!¶" + new string('q', 200_000) + "😀" + new string('r', 70_000);
            p.Send(big); p.Send("after");
            rig.Pump(p);
            T.Seq(new[] { big, "after" }, rig.Disp.Texts());
        });

        T.Run("page BYE runs after the ops before it, then the document is closed", () =>
        {
            var rig = new Rig();
            rig.View.NotifyLoadStarted("https://w.example/"); rig.View.NotifyOnLoad();
            var p = new TestPage("bye"); p.Hello(); rig.Pump(p);
            p.Send("last-words");
            p.Link.EnqueueOp(BridgeOps.Encode(BridgeOp.Bye, new FarewellPayload { Reason = BridgeReason.PageHide, DocToken = "bye" }.Encode()));
            rig.Pump(p);
            T.Seq(new[] { "last-words" }, rig.Disp.Texts());
            T.Eq(null, rig.View.Current);
        });

        T.Run("unmarked strings keep FIFO with a held document's frames", () =>
        {
            var rig = new Rig(new BridgeViewOptions { LoadStartedMayTrail = true });
            var p = new TestPage("fifo"); p.Hello(); rig.Pump(p);
            p.Send("framed-1"); p.Flush();
            foreach (var s in p.TakeOut()) rig.View.Receive(s);
            rig.View.Receive("raw-O.sendMessage");
            p.Send("framed-2"); p.Flush();
            foreach (var s in p.TakeOut()) rig.View.Receive(s);
            rig.View.Drain();
            T.Eq(0, rig.Disp.Ran.Count);
            rig.View.NotifyLoadStarted("https://world.example/space"); rig.View.NotifyOnLoad();
            rig.View.Drain();
            T.Seq(new[] { "framed-1", "raw-O.sendMessage", "framed-2" }, rig.Disp.Texts());
        });

        T.Run("the frozen 4.0.25 page (legacy batches, no HELLO) still works end to end", () =>
        {
            var rig = new Rig();
            rig.View.NotifyLoadStarted("https://w.example/"); rig.View.NotifyOnLoad();
            rig.View.Receive(BridgeLegacyBatch.Pack(BridgeLegacyBatch.PageMarker, new[] { "!sst!", "!i!:1¶!gp!¶" }));
            rig.View.Receive("!sr!");
            rig.View.Drain();
            T.Seq(new[] { "!sst!", "!i!:1¶!gp!¶", "!sr!" }, rig.Disp.Texts(0));
            rig.View.Send("!i!:1¶x"); rig.View.Send("!e!y");
            rig.View.Flush();
            var down = rig.Sink.Take();
            T.Eq(1, down.Count);
            T.Eq(BridgeLegacyBatch.Pack(BridgeLegacyBatch.UnityMarker, new[] { "!i!:1¶x", "!e!y" }), down[0]);
        });
    }
}
