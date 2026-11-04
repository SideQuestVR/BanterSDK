using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using BS.Bridge;

/// <summary>The single outbound queue: ordering across message kinds and threads, and per-flush burst splitting.</summary>
static class OutboxTests
{
    static TestPage OpenPage(Rig rig, string token)
    {
        rig.View.NotifyLoadStarted("https://w.example/"); rig.View.NotifyOnLoad();
        var p = new TestPage(token); p.Hello(); rig.Pump(p);
        T.Eq(BridgeSessionState.Open, rig.View.Current.State);
        return p;
    }

    public static void Run()
    {
        Console.WriteLine("outbound queue");

        T.Run("one queue: replies, events and RESPONSE_ID requests keep their order (nothing overtakes)", () =>
        {
            var rig = new Rig();
            var p = OpenPage(rig, "order");
            var expected = new List<string>();
            // A request is answered while events and Unity->page requests (BSLink.Send with a callback) interleave.
            rig.Disp.OnText = (s, t) =>
            {
                if (BridgeView.LegacyRequestId(t, true) != 4) return false;
                foreach (var m in new[] { "!e!tu!¶1", "!o!1¶!ijs!¶code", "!i!:4¶answer", "!e!tu!¶2", "!o!2¶x" })
                { rig.View.Send(m); expected.Add(m); }
                return true;
            };
            p.Send("!i!:4¶!gp!¶");
            rig.Pump(p);
            T.Seq(expected, p.Got);
        });

        T.Run("any thread may Send; each thread's order survives and nothing is lost or duplicated", () =>
        {
            var rig = new Rig();
            var p = OpenPage(rig, "mpsc");
            const int threads = 8, each = 2000;
            var start = new ManualResetEventSlim(false);
            var ts = Enumerable.Range(0, threads).Select(t => new Thread(() =>
            {
                start.Wait();
                for (int i = 0; i < each; i++) rig.View.Send("!e!t" + t + "¶" + i);
            })).ToList();
            ts.ForEach(t => t.Start());
            start.Set();
            // The main thread flushes while the producers run.
            while (ts.Any(t => t.IsAlive)) { rig.Back(p); p.Flush(); rig.Deliver(p); }
            for (int i = 0; i < 50 && rig.View.HasOutbound; i++) rig.Pump(p);
            T.Eq(threads * each, p.Got.Count);
            for (int t = 0; t < threads; t++)
            {
                var mine = p.Got.Where(g => g.StartsWith("!e!t" + t + "¶", StringComparison.Ordinal)).Select(g => int.Parse(g.Substring(g.IndexOf('¶') + 1))).ToList();
                T.Seq(Enumerable.Range(0, each), mine, "thread " + t);
            }
        });

        T.Run("burst splitting on the session lane: bounded frames per flush, order kept", () =>
        {
            var rig = new Rig(new BridgeViewOptions { FlushMaxBytes = 100_000, FlushMaxFrames = 3 });
            var p = OpenPage(rig, "burst");
            var msgs = Enumerable.Range(0, 20_000).Select(i => "!e!tu!¶" + i + "§" + new string('p', i % 40)).ToList();
            foreach (var m in msgs) rig.View.Send(m);
            int flushes = 0;
            while (rig.View.HasOutbound && flushes < 1000)
            {
                rig.View.Flush();
                flushes++;
                var down = rig.Sink.Take();
                long cost = 0;
                int frames = 0;
                foreach (var d in down)
                {
                    BridgeCarrier.TryDecode(d, out var f);
                    if (f.Header.Kind != FrameKind.Data) continue;
                    frames++;
                    T.True(f.Header.Length <= BridgeWire.FragmentSize, "frame over 64 KiB");
                    cost += BridgeWire.HeaderBytes + f.Header.Length;
                }
                T.True(frames <= 3, "more than FlushMaxFrames in one flush");
                T.True(frames <= 1 || cost <= 100_000, "over FlushMaxBytes in one flush: " + cost);
                foreach (var d in down) p.Receive(d);
                p.Flush(); rig.Deliver(p); // acks refill Unity's window
            }
            T.True(flushes > 3, "a 1 MB burst should take several frames, took " + flushes);
            T.Seq(msgs, p.Got);
        });

        T.Run("burst splitting on the legacy lane: one bounded batch per flush, order kept", () =>
        {
            var rig = new Rig(new BridgeViewOptions { LegacyBatchMaxChars = 10_000 });
            var msgs = Enumerable.Range(0, 3000).Select(i => "!e!x¶" + i).ToList();
            msgs.Insert(1500, new string('Z', 25_000)); // one message bigger than the limit still goes, alone
            foreach (var m in msgs) rig.View.Send(m);
            var got = new List<string>();
            int flushes = 0;
            while (rig.View.HasOutbound && flushes < 1000)
            {
                rig.View.Flush(); flushes++;
                var down = rig.Sink.Take();
                T.Eq(1, down.Count);
                var part = new List<string>();
                T.True(BridgeLegacyBatch.Unpack(down[0], 1, part));
                T.True(down[0].Length <= 10_001 || part.Count == 1, "batch over the limit: " + down[0].Length);
                got.AddRange(part);
            }
            T.True(flushes > 3);
            T.Seq(msgs, got);
        });

        T.Run("legacy backlog spilling over a flush is sent before newer frames", () =>
        {
            var rig = new Rig(new BridgeViewOptions { LegacyBatchMaxChars = 50 });
            rig.View.NotifyLoadStarted("https://w.example/"); rig.View.NotifyOnLoad();
            var p = new TestPage("spill"); p.Hello();
            rig.Deliver(p);
            var early = Enumerable.Range(0, 10).Select(i => "early-" + i + new string('e', 20)).ToList();
            foreach (var m in early) rig.View.Send(m); // AwaitingAck: legacy lane, spills over several flushes
            rig.Back(p);
            p.Flush(); rig.Deliver(p); // epoch acknowledged: framed from now on
            rig.View.Send("framed-1");
            for (int i = 0; i < 30; i++) rig.Back(p);
            T.Seq(early.Concat(new[] { "framed-1" }), p.Got);
        });
    }
}
