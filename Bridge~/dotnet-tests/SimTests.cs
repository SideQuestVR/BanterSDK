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

        T.Run("handshake: WELCOME at Flush; frames follow it at once, before the page acknowledges the epoch", () =>
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
            T.Eq(2, down.Count, "WELCOME + one text frame");
            T.True(down.All(BridgeCarrier.IsCarrier), "nothing but frames");
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

        T.Run("a page that ignores the window is reset; RESET makes it send exactly the unconsumed strings again", () =>
        {
            var rig = new Rig(new BridgeViewOptions { RecvWindow = 2048 });
            rig.View.NotifyLoadStarted("https://w.example/"); rig.View.NotifyOnLoad();
            var p = new TestPage("greedy"); p.Hello(); rig.Pump(p);
            uint epoch = p.Epoch;
            p.HonorWindow = false;
            p.Link.FragmentSize = 600;
            var texts = Enumerable.Range(0, 20).Select(i => "g" + i + new string('y', 200)).ToList();
            foreach (var t in texts) p.Send(t);
            rig.Pump(p, 200);
            T.True(rig.View.Stats.ProtocolResets >= 1);
            T.Eq(rig.View.Stats.ProtocolResets, rig.View.Stats.Reopens, "every reset was followed by a HELLO");
            T.Eq("open", p.State);
            T.Eq(epoch, p.Epoch, "the same document, the same epoch");
            T.Eq(1L, rig.View.Stats.Sessions);
            T.Seq(texts, rig.Disp.Texts(epoch), "every string once, in order, in its session");
        });

        T.Run("RESET keeps the document: its reply in flight, its ledger and what was queued reach it after the new WELCOME", () =>
        {
            var rig = new Rig();
            rig.View.NotifyLoadStarted("https://w.example/"); rig.View.NotifyOnLoad();
            var p = new TestPage("keep"); p.Hello(); rig.Pump(p);
            var s = rig.View.Current;
            Action reply = null;
            rig.Disp.OnText = (ss, t) =>
            {
                var ec = ExecutionContext.Capture();
                reply = () => ExecutionContext.Run(ec, _ => rig.View.Send("!i!:7¶answer"), null);
                return true;
            };
            p.Send("!i!:7¶!slow¶"); rig.Pump(p);
            T.Eq(1, s.OutstandingLegacyRequests);
            // A protocol error: a seq gap.
            rig.View.Receive(BridgeCarrier.EncodeText(new FrameHeader(FrameKind.Data, FrameFlags.Complete, p.Epoch, p.Link.NextSeq + 5, p.Link.AckSeq, 0), BridgeCarrier.PackEntries(new[] { "x" })));
            rig.View.Drain();
            T.Eq(BridgeSessionState.Resetting, s.State);
            rig.View.Send("!e!queued");
            reply();
            rig.View.Flush();
            var down = rig.Sink.Take();
            T.Eq(1, down.Count, "the RESET, and nothing behind it");
            BridgeCarrier.TryDecode(down[0], out var f);
            T.Eq(FrameKind.Reset, f.Header.Kind);
            p.Receive(down[0]);
            T.Eq("hello", p.State);
            rig.Pump(p);
            T.Eq("open", p.State);
            T.Eq(BridgeSessionState.Open, s.State);
            T.True(rig.View.Current == s, "the same session");
            T.Seq(new[] { "!e!queued", "!i!:7¶answer" }, p.Got);
            T.Eq(0, s.OutstandingLegacyRequests);
            T.Eq(0L, rig.View.Stats.LateReplies);
        });

        T.Run("a page that lost its place says HELLO with what it took: the rest of Unity's frames go again, same epoch", () =>
        {
            var rig = new Rig();
            rig.View.NotifyLoadStarted("https://w.example/"); rig.View.NotifyOnLoad();
            var p = new TestPage("lost-place"); p.Hello(); rig.Pump(p);
            uint epoch = p.Epoch;
            rig.View.Send("first"); rig.Back(p);
            uint took = p.Link.AckSeq;
            p.Send("mine"); rig.Pump(p);
            rig.View.Send("second"); rig.View.Flush(); // posted, but the page breaks on it
            var lost = rig.Sink.Take();
            T.Eq(1, lost.Count);
            rig.View.Send("third");
            p.Got.Clear();
            p.Reopen(new List<OutEntry> { new OutEntry("unsent") }, took);
            rig.Pump(p);
            T.Eq(epoch, p.Epoch);
            T.Eq(1L, rig.View.Stats.Reopens);
            T.Seq(new[] { "second", "third" }, p.Got, "from the first frame it did not take");
            T.Seq(new[] { "mine", "unsent" }, rig.Disp.Texts(epoch));
        });

        T.Run("lost HELLO: the page says it again and its queue runs in its session, in order", () =>
        {
            var rig = new Rig();
            rig.View.NotifyLoadStarted("https://w.example/"); rig.View.NotifyOnLoad();
            var p = new TestPage("lost");
            p.Hello();
            p.TakeOut(); // lost
            p.Send("a"); p.Send("b");
            p.HelloAgain();
            p.Send("c");
            rig.Pump(p);
            T.Eq("open", p.State);
            T.Seq(new[] { "a", "b", "c" }, rig.Disp.Texts(p.Epoch));
            T.Eq(1L, rig.View.Stats.Sessions);
        });

        T.Run("a retry crossing WELCOME: same epoch, one session, the page's strings run once", () =>
        {
            var rig = new Rig();
            rig.View.NotifyLoadStarted("https://w.example/"); rig.View.NotifyOnLoad();
            var p = new TestPage("crossed");
            p.Hello();
            p.Send("a");
            p.HelloAgain();
            rig.Deliver(p);   // both HELLOs, before anything was flushed
            T.Eq(1L, rig.View.Stats.Sessions);
            T.Eq(2L, rig.View.Stats.Hellos);
            rig.Back(p);      // WELCOME twice: the answer to the retry, and the link's own
            T.Eq("open", p.State);
            rig.Pump(p);
            T.Seq(new[] { "a" }, rig.Disp.Texts(p.Epoch));
            T.Eq(BridgeSessionState.Open, rig.View.Current.State);
        });

        T.Run("a session closed before the page acknowledged still says CLOSE: a page already open parks, and is not left talking to nobody", () =>
        {
            var rig = new Rig();
            rig.View.NotifyLoadStarted("https://w.example/1"); rig.View.NotifyOnLoad();
            var p = new TestPage("unacked"); p.Hello();
            rig.Deliver(p); rig.Back(p);
            T.Eq("open", p.State);
            T.Eq(BridgeSessionState.AwaitingAck, rig.View.Current.State);
            rig.View.NotifyLoadStarted("https://w.example/2");
            foreach (var d in rig.Sink.Take()) p.Receive(d);
            T.Eq("parked", p.State);
        });

        T.Run("refusals: about:, iframes, closed tokens; a retried HELLO keeps the epoch", () =>
        {
            var rig = new Rig();
            var a = new TestPage("about", "about:blank"); a.Hello(); rig.Pump(a);
            T.Eq("closed", a.State); T.Eq(null, rig.View.Current);
            rig.View.NotifyLoadStarted("https://w.example/"); rig.View.NotifyOnLoad();
            var d = new TestPage("dup"); d.Hello();
            rig.Deliver(d);
            uint epoch = rig.View.Current.Epoch;
            d.HelloAgain();
            rig.Deliver(d);
            T.Eq(epoch, rig.View.Current.Epoch);
            rig.Pump(d);
            T.Eq(1L, rig.View.Stats.Sessions, "no second session for the same token");
            rig.View.NotifyLoadStarted("https://w.example/next"); // replaces the document that claimed the first load
            var again = new TestPage("dup"); again.Hello(); rig.Pump(again);
            T.Eq("closed", again.State, "a closed document's token is refused");
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

        T.Run("no session: a page's raw strings still run, and Unity has nobody to send to", () =>
        {
            var rig = new Rig();
            rig.View.NotifyLoadStarted("https://w.example/"); rig.View.NotifyOnLoad();
            rig.View.Receive("!sst!");
            rig.View.Receive("!i!:1¶!gp!¶");
            rig.View.Drain();
            T.Seq(new[] { "!sst!", "!i!:1¶!gp!¶" }, rig.Disp.Texts(0));
            rig.View.Send("!i!:1¶x"); rig.View.Send("!e!y");
            rig.View.Flush();
            T.Eq(0, rig.Sink.Take().Count, "nothing leaves without a session");
            T.Eq(1L, rig.View.Stats.DuplicateReplies);
            T.Eq(1L, rig.View.Stats.DroppedNoSession);
            T.True(!rig.View.HasOutbound);
        });

        T.Run("a message no page could reassemble never resets the session: a reply fails, an event is dropped", () =>
        {
            var rig = new Rig();
            rig.View.NotifyLoadStarted("https://w.example/"); rig.View.NotifyOnLoad();
            var p = new TestPage("huge"); p.Hello(); rig.Pump(p);
            var huge = new string('h', BridgeView.MaxTextChars + 1);
            rig.Disp.OnText = (s, t) => { rig.View.Send("!i!:2¶" + huge); return true; };
            p.Send("!i!:2¶!tex!¶"); rig.Pump(p);
            rig.View.Send(huge);
            rig.View.Send("!e!after");
            rig.Pump(p);
            T.Seq(new[] { "!i!:2¶!ree!¶bridge: reply too large", "!e!after" }, p.Got);
            T.Eq(2L, rig.View.Stats.OutboundDropped);
            T.Eq(0L, rig.View.Stats.ProtocolResets);
            T.Eq(BridgeSessionState.Open, rig.View.Current.State);
        });
    }
}
