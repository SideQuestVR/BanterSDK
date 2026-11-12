using System;
using System.Collections.Generic;
using BS.Bridge;

/// <summary>
/// One scripted session over today's wire, recorded as every string each side posted ("&gt; " page to Unity, "&lt; "
/// Unity to page). flags-off.golden.txt is this script's transcript from the core as it was before the M3 families,
/// so a run on today's core with those families off proves the wire did not change. Only APIs that existed then are
/// used here; extra is where the new code tries its new entry points, which with the families off must do nothing.
/// </summary>
static class FlagsOffScript
{
    public static List<string> Run(Action<Rig, TestPage> extra = null)
    {
        var transcript = new List<string>();
        var handles = new FakeHandles();
        var host = new BridgeCallHost(FakeApi.Table(), handles, () => 1);
        // No flush time budget: the frames must not depend on how fast this machine is.
        var rig = new Rig(new BridgeViewOptions { CallHost = host, FlushBudgetMs = 0 });
        rig.Disp.AutoReply = true;
        var page = new TestPage("doc-flags-off");

        void Pump()
        {
            for (int i = 0; i < 40; i++)
            {
                page.Flush();
                var up = page.TakeOut();
                foreach (var s in up) { transcript.Add("> " + s); rig.View.Receive(s); }
                rig.View.Drain();
                rig.View.Tick();
                rig.View.Flush();
                var down = rig.Sink.Take();
                foreach (var s in down) { transcript.Add("< " + s); page.Receive(s); }
                if (up.Count == 0 && down.Count == 0 && !rig.View.HasOutbound && page.Out.Count == 0) return;
            }
        }

        rig.View.NotifyLoadStarted("https://world.example/space");
        rig.View.NotifyOnLoad();
        // After the LoadStarted, which forgets the last world's handles.
        int obj = handles.Add(new FakeObject(2.5f));
        page.Hello();
        Pump();
        extra?.Invoke(rig, page);

        // The page's strings: requests, a no-reply request, a raw one, and a long one that fragments.
        page.Send("!i!:1¶!oa!¶uuid-1¶1¶Cube¶0¶Untagged¶0¶0¶0¶0¶0¶0¶0¶1¶0¶0¶0¶1¶1¶1");
        page.Send("!i!:10000¶!cu!¶0~5¶6¶2§3§1.5");
        page.Send("!i!:2¶!st!¶5¶0¶1§0§0§0§0§");
        page.Send(new string('x', 70000));
        // A typed call and its reply.
        page.Link.EnqueueOp(BridgeOps.Encode(BridgeOp.Call, FakeApi.Call(1, FakeApi.Get, obj)));
        Pump();
        extra?.Invoke(rig, page);

        // Unity's strings: property updates, a watched transform, an event, a presence marker, a request.
        rig.View.Send("!u!5¶6¶7¶8§3§1.5");
        rig.View.Send("!u!5¶6¶7¶8§3§2.5");
        rig.View.Send("!e!!tu!¶5§0‡1‡2‡3§1‡4‡5‡6");
        rig.View.Send("!u!9¶0¶0¶0§0§1");
        rig.View.Send("!e!!clk!¶5¶0¶0¶0");
        Pump();
        extra?.Invoke(rig, page);

        // The next document: this one is closed and parks.
        rig.View.NotifyLoadStarted("https://world.example/next");
        Pump();
        return transcript;
    }
}
