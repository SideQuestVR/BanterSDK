using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BS.Bridge;

/// <summary>
/// M3: the FENCE family, ACCEPT, the pushed families (DELTA, ANNOUNCE, DESTROYED) and the item numbering behind DELTA's
/// applied count. With the families off, flags-off.golden.txt holds the wire to what it was before them.
/// </summary>
static class PushTests
{
    static string Source([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
    static string Here => Path.GetDirectoryName(Source());

    const uint All = BridgeFamily.Fence | BridgeFamily.Pushed;

    /// <summary>A view offering families, a page, a fake main-thread queue the dispatcher's "work:" strings fill.</summary>
    sealed class Lab
    {
        public readonly FakeHandles Handles = new FakeHandles();
        public readonly Rig Rig;
        public readonly TestPage Page = new TestPage("doc");
        public readonly List<Action> Scheduler = new List<Action>();
        public readonly List<string> Effects = new List<string>();
        public readonly int Obj;
        uint nextReq = 1;

        public Lab(uint families, uint accept = 0, bool callHost = true, bool hold = false, int fenceMaxBusyMs = 5000)
        {
            var o = new BridgeViewOptions { Families = families, FlushBudgetMs = 0, FenceMaxBusyMs = fenceMaxBusyMs };
            if (callHost) o.CallHost = new BridgeCallHost(FakeApi.Table(), Handles, () => 1);
            Rig = new Rig(o);
            Rig.Disp.AutoReply = true;
            // A legacy handler that leaves its effect to the main-thread scheduler, as most of BSScene's do.
            Rig.Disp.OnText = (s, t) =>
            {
                if (t.StartsWith("work:")) Scheduler.Add(BridgeContext.Bind(() => Effects.Add(t)));
                else if (t.StartsWith("nest:"))
                    Scheduler.Add(BridgeContext.Bind(() =>
                    {
                        Effects.Add(t);
                        Scheduler.Add(BridgeContext.Bind(() => Effects.Add("inner " + t)));
                    }));
                else if (t.StartsWith("coro:")) Coroutines.Add(BridgeContext.Bind(Steps(t)));
                return false;
            };
            Rig.View.NotifyLoadStarted("https://world.example/space");
            if (!hold) Rig.View.NotifyOnLoad();
            Obj = Handles.Add(new FakeObject(1));
            Page.Hello();
            if (accept != 0) Page.Accept(accept);
            Rig.Pump(Page);
            FakeApi.Effects.Clear();
        }

        public readonly List<IEnumerator> Coroutines = new List<IEnumerator>();
        IEnumerator Steps(string t) { Effects.Add("step1 " + t); yield return null; Effects.Add("step2 " + t); }

        public BridgeSession S => Rig.View.Current;

        public uint Call(int member, params Action<ByteWriter>[] args)
        {
            uint id = nextReq++;
            Page.Link.EnqueueOp(BridgeOps.Encode(BridgeOp.Call, FakeApi.Call(id, member, member == FakeApi.Log ? 0 : Obj, args)));
            return id;
        }

        /// <summary>One frame of the scheduler: runs what was queued before it began.</summary>
        public void RunScheduler()
        {
            var items = Scheduler.ToList();
            Scheduler.Clear();
            foreach (var a in items) a();
        }

        public void Frame()
        {
            Rig.Pump(Page);
            Rig.View.Tick();
        }

        public List<string> Got() { var g = Page.Got.ToList(); Page.Got.Clear(); return g; }
    }

    static DeltaEntry D(int oid, int cid, int ct, int prop, string text) => new DeltaEntry(oid, cid, ct, prop, text);

    public static void Run()
    {
        Console.WriteLine("M3 families (fence, accept, delta, announce, destroyed)");

        // ------------------------------------------------------------------------------------------- flags off
        T.Run("flags off: the wire is byte for byte the pre-M3 transcript, whatever the new entry points are asked", () =>
        {
            int extras = 0;
            var transcript = FlagsOffScript.Run((rig, page) =>
            {
                extras++;
                var v = rig.View;
                T.Eq(BridgeFamily.Call, v.Current.Offered, "offered (CALL only)");
                T.True(v.Current.Fence == null, "no fence without FENCE or DELTA on offer");
                foreach (var f in new[] { BridgeFamily.Delta, BridgeFamily.Announce, BridgeFamily.Destroyed, All }) T.True(!v.Pushes(f), "pushes " + f);
                v.SendDelta(5, 6, 7, 8, "8§3§9", 1);
                v.SendAnnounce(new AnnounceEntry(5, true, "x", 0, "Untagged", 0));
                v.SendAnnounce(new AnnounceEntry(5, 6, 7));
                v.SendDestroyed(5, DestroyedReason.Destroyed);
                using (BridgeContext.Enter(v.Current.Epoch, v.Current.Fence))
                {
                    T.True(BridgeContext.CurrentFence == null, "no fence in the context");
                    var plain = BridgeContext.Bind(() => { });
                    plain();
                }
            });
            T.Eq(3, extras, "the script's three tries");
            var golden = File.ReadAllLines(Path.Combine(Here, "flags-off.golden.txt"), System.Text.Encoding.UTF8);
            T.Seq(golden, transcript, "transcript vs the pre-M3 core");
        });

        T.Run("flags off: an ACCEPT a page sends anyway accepts nothing, and nothing is pushed", () =>
        {
            var lab = new Lab(0, accept: All);
            T.Eq(1L, lab.Rig.View.Stats.Accepts, "ACCEPT read");
            T.Eq(0u, lab.S.Accepted, "accepted");
            T.Eq(0u, lab.Page.Families & ~BridgeFamily.Call, "WELCOME offers nothing new");
            lab.Rig.View.SendDelta(5, 6, 7, 8, "8§3§9", 0);
            lab.Rig.View.SendDestroyed(5, DestroyedReason.Destroyed);
            lab.Frame();
            T.Eq(0, lab.Page.Pushes.Count, "pushes");
            T.Eq(0L, lab.Rig.View.Stats.PushesSent, "sent");
        });

        T.Run("flags off: a CALL behind a string's queued work runs at once, as before (no fence)", () =>
        {
            var lab = new Lab(0);
            lab.Page.Send("work:a");
            uint id = lab.Call(FakeApi.Log, FakeApi.S("call"));
            lab.Frame();
            T.Seq(new[] { "call" }, FakeApi.Effects, "the call ran");
            T.Eq(0, lab.Effects.Count, "the string's work has not");
            T.Eq(1, lab.Page.Replies.Count(r => r.reqId == id), "replied");
            lab.RunScheduler();
            T.Seq(new[] { "work:a" }, lab.Effects, "then the work");
        });

        // ------------------------------------------------------------------------------------------------ FENCE
        T.Run("FENCE: offered in WELCOME; a CALL waits for the scheduler work earlier strings queued, then runs", () =>
        {
            var lab = new Lab(BridgeFamily.Fence);
            T.Eq(BridgeFamily.Call | BridgeFamily.Fence, lab.Page.Families, "WELCOME families");
            T.True(lab.S.Fence != null, "fenced");
            lab.Page.Send("work:a");
            lab.Page.Send("work:b");
            uint id = lab.Call(FakeApi.Log, FakeApi.S("call"));
            lab.Frame();
            T.Eq(0, FakeApi.Effects.Count, "the call waits");
            T.Eq(2, lab.S.Fence.Pending, "two items to start");
            T.True(lab.Rig.View.Stats.FenceWaits > 0, "counted");
            T.Eq(0, lab.Page.Replies.Count(r => r.reqId == id), "no reply yet");
            lab.RunScheduler();
            T.Eq(0, lab.S.Fence.Pending, "drained");
            lab.Frame();
            T.Seq(new[] { "work:a", "work:b" }, lab.Effects, "the strings' work first");
            T.Seq(new[] { "call" }, FakeApi.Effects, "then the call");
            T.Eq(1, lab.Page.Replies.Count(r => r.reqId == id), "one reply");
        });

        T.Run("FENCE: work that queues more work holds the CALL until that starts too; a coroutine until its first step", () =>
        {
            var lab = new Lab(BridgeFamily.Fence);
            lab.Page.Send("nest:a");
            lab.Page.Send("coro:c");
            lab.Call(FakeApi.Log, FakeApi.S("call"));
            lab.Frame();
            T.Eq(2, lab.S.Fence.Pending, "the work and the coroutine");
            lab.RunScheduler();                       // nest:a starts and queues its inner work
            T.Eq(2, lab.S.Fence.Pending, "the inner work and the coroutine");
            lab.Frame();
            T.Eq(0, FakeApi.Effects.Count, "still waits");
            lab.RunScheduler();                       // the inner work
            T.True(lab.Coroutines[0].MoveNext(), "the coroutine's first step");
            T.Eq(0, lab.S.Fence.Pending, "all started");
            lab.Frame();
            T.Seq(new[] { "call" }, FakeApi.Effects, "the call ran");
            T.Seq(new[] { "nest:a", "inner nest:a", "step1 coro:c" }, lab.Effects, "in order");
            T.True(!lab.Coroutines[0].MoveNext() && lab.Effects.Last() == "step2 coro:c", "later steps are not counted");
            T.Eq(0, lab.S.Fence.Pending, "nothing left");
        });

        T.Run("FENCE: a fence that never empties expires after FenceMaxBusyMs, lets the CALL go, and holds again once empty", () =>
        {
            var lab = new Lab(BridgeFamily.Fence, fenceMaxBusyMs: 1000);
            lab.Page.Send("work:stuck");
            lab.Call(FakeApi.Log, FakeApi.S("first"));
            lab.Frame();
            lab.Rig.Now += 600; lab.Frame();
            T.Eq(0, FakeApi.Effects.Count, "within the limit it waits");
            lab.Rig.Now += 600; lab.Frame();
            T.True(lab.S.Fence.Expired, "expired");
            T.Eq(1L, lab.Rig.View.Stats.FenceExpiries, "counted once");
            lab.Frame();
            T.Seq(new[] { "first" }, FakeApi.Effects, "the call went");
            lab.RunScheduler();
            lab.Frame();
            T.True(!lab.S.Fence.Expired, "re-armed once empty");
            lab.Page.Send("work:next");
            lab.Call(FakeApi.Log, FakeApi.S("second"));
            lab.Frame();
            T.Seq(new[] { "first" }, FakeApi.Effects, "holds again");
            lab.RunScheduler();
            lab.Frame();
            T.Seq(new[] { "first", "second" }, FakeApi.Effects, "and lets go when drained");
        });

        T.Run("FENCE: a fence that keeps emptying never expires, however long it is busy at each look", () =>
        {
            var lab = new Lab(BridgeFamily.Fence, fenceMaxBusyMs: 1000);
            for (int i = 0; i < 10; i++)
            {
                lab.Page.Send("work:" + i);
                lab.Frame();
                lab.RunScheduler();
                lab.Page.Send("work:again" + i);
                lab.Frame();
                lab.Rig.Now += 500;
                lab.Rig.View.Tick();
            }
            T.Eq(0L, lab.Rig.View.Stats.FenceExpiries, "never expired");
        });

        T.Run("FENCE: work started WithoutFence (the property worker) is not counted, and the fence comes back after", () =>
        {
            var f = new BridgeFence();
            using (BridgeContext.Enter(3, f))
            {
                using (BridgeContext.WithoutFence())
                {
                    T.True(BridgeContext.CurrentFence == null, "no fence inside");
                    T.Eq(3u, BridgeContext.CurrentEpoch, "the epoch stays");
                    BridgeContext.Bind(() => { })();
                    T.Eq(0, f.Pending, "not counted");
                }
                T.True(ReferenceEquals(BridgeContext.CurrentFence, f), "restored");
                var counted = BridgeContext.Bind(() => { });
                T.Eq(1, f.Pending, "counted again");
                counted();
                T.Eq(0, f.Pending, "released as it starts");
            }
            T.True(BridgeContext.CurrentFence == null, "and gone with the scope");
            using (BridgeContext.WithoutFence()) T.True(BridgeContext.CurrentFence == null, "no fence: nothing to do");
        });

        // ------------------------------------------------------------------------------------------------ ACCEPT
        T.Run("ACCEPT: keeps only pushed families WELCOME offered; FENCE and CALL can't be accepted", () =>
        {
            var lab = new Lab(BridgeFamily.Fence | BridgeFamily.Delta | BridgeFamily.Destroyed, accept: BridgeFamily.Delta | BridgeFamily.Announce | BridgeFamily.Fence | BridgeFamily.Call);
            T.Eq(BridgeFamily.Delta, lab.S.Accepted, "accepted");
            T.True(lab.Rig.View.Pushes(BridgeFamily.Delta) && !lab.Rig.View.Pushes(BridgeFamily.Announce) && !lab.Rig.View.Pushes(BridgeFamily.Destroyed), "pushes");
            lab.Page.Accept(BridgeFamily.Destroyed);
            lab.Frame();
            T.Eq(BridgeFamily.Destroyed, lab.S.Accepted, "a later ACCEPT replaces it");
            lab.Page.Link.EnqueueOp(BridgeOps.Encode(BridgeOp.Accept, new byte[] { 1, 2 }));
            lab.Frame();
            T.Eq(BridgeFamily.Destroyed, lab.S.Accepted, "a malformed ACCEPT is ignored");
        });

        // ------------------------------------------------------------------------------------------------- DELTA
        T.Run("DELTA: one entry per (object, component, property), newest in the first's place, among values of one stamp", () =>
        {
            var lab = new Lab(BridgeFamily.Delta, accept: BridgeFamily.Delta);
            var v = lab.Rig.View;
            v.SendDelta(5, 6, 7, 8, "8§3§1", 4);
            v.SendDelta(5, 6, 7, 9, "9§3§2", 4);
            v.SendDelta(5, 6, 7, 8, "8§3§3", 4);
            v.SendDelta(2, 0, 0, 1, "1‡0‡0‡0", 4);
            v.SendDelta(3, 0, 0, 1, "1‡1‡1‡1", 4);
            v.SendDelta(2, 0, 0, 1, "1‡2‡2‡2", 4);
            lab.Frame();
            T.Eq(1, lab.Page.Pushes.Count, "one DELTA");
            T.Eq((uint)BridgeOp.Delta, lab.Page.Pushes[0].opcode, "opcode");
            T.True(BridgePush.TryDecodeDelta(lab.Page.Pushes[0].payload, out uint applied, out var entries), "decodes");
            T.Eq(4u, applied, "the values' stamp");
            T.Seq(new[] { "5/6/8 8§3§3", "5/6/9 9§3§2", "2/0/1 1‡2‡2‡2", "3/0/1 1‡1‡1‡1" },
                entries.Select(e => e.Oid + "/" + e.Cid + "/" + e.Prop + " " + e.Text), "entries");
            T.Eq(7, entries[0].ComponentType, "component type");
        });

        T.Run("DELTA: a value read at a later stamp starts the next DELTA, so the page doesn't take it for one older than its write", () =>
        {
            var lab = new Lab(BridgeFamily.Delta, accept: BridgeFamily.Delta);
            var v = lab.Rig.View;
            v.SendDelta(5, 6, 7, 9, "9§3§2", 9);
            v.SendDelta(5, 6, 7, 8, "8§3§3", 10); // Unity's correction of the page's write, item 10
            lab.Frame();
            T.Eq(2, lab.Page.Pushes.Count, "two DELTAs");
            T.True(BridgePush.TryDecodeDelta(lab.Page.Pushes[0].payload, out uint first, out var a), "first decodes");
            T.True(BridgePush.TryDecodeDelta(lab.Page.Pushes[1].payload, out uint second, out var b), "second decodes");
            T.Eq(9u, first, "first applied");
            T.Eq(10u, second, "second applied: the correction is kept");
            T.Seq(new[] { "9§3§2" }, a.Select(e => e.Text), "first entries");
            T.Seq(new[] { "8§3§3" }, b.Select(e => e.Text), "second entries");
        });

        T.Run("DELTA: anything queued between values ends the batch, so values and events keep their order", () =>
        {
            var lab = new Lab(BridgeFamily.Delta, accept: BridgeFamily.Delta);
            var v = lab.Rig.View;
            v.SendDelta(5, 6, 7, 8, "8§3§1", 1);
            v.Send("!e!one");
            v.SendDelta(5, 6, 7, 8, "8§3§2", 1);
            v.SendDelta(5, 6, 7, 8, "8§3§3", 1);
            v.Send("!e!two");
            lab.Frame();
            T.Seq(new[] { "#51", "!e!one", "#51", "!e!two" }, lab.Got(), "order");
            T.True(BridgePush.TryDecodeDelta(lab.Page.Pushes[1].payload, out _, out var second), "second");
            T.Seq(new[] { "8§3§3" }, second.Select(e => e.Text), "coalesced within its run");
        });

        T.Run("DELTA: nothing goes before ACCEPT, and values for a document that has gone are dropped", () =>
        {
            var lab = new Lab(BridgeFamily.Delta);
            var v = lab.Rig.View;
            T.True(!v.Pushes(BridgeFamily.Delta), "not accepted yet");
            v.SendDelta(5, 6, 7, 8, "8§3§1", 0);
            lab.Frame();
            T.Eq(0, lab.Page.Pushes.Count, "nothing pushed");
            lab.Page.Accept(BridgeFamily.Delta);
            lab.Frame();
            v.SendDelta(5, 6, 7, 8, "8§3§1", 0);
            v.NotifyLoadStarted("https://world.example/next");
            v.Flush();
            T.Eq(1L, v.Stats.PushesDropped, "dropped for the closed document");
        });

        T.Run("items: strings and typed ops are numbered as sent, session-plane ops and raw strings are not; Applied follows", () =>
        {
            var lab = new Lab(BridgeFamily.Delta, accept: BridgeFamily.Delta, callHost: false);
            T.Eq(0u, lab.S.ItemsIn, "ACCEPT is not an item");
            lab.Page.Send("one");
            lab.Page.Link.EnqueueOp(BridgeOps.Encode(BridgeOp.Ping, BridgeOps.EncodeU32(7)));
            lab.Page.Send("two");
            lab.Page.Request(0x80, 9);
            lab.Rig.View.Receive("a raw O.sendMessage string");
            lab.Frame();
            T.Eq(3u, lab.S.ItemsIn, "two strings and a typed op");
            T.Eq(3u, lab.S.ItemsDone, "all dispatched");
            T.Eq(3u, lab.S.Applied, "applied");
            T.Eq(3u, lab.Rig.View.AppliedMark, "the mark Unity stamps with");
            lab.Call(FakeApi.Log, FakeApi.S("x")); // no call host: refused at once, still numbered
            lab.Page.Send("three");
            lab.Frame();
            T.Eq(5u, lab.S.ItemsIn, "the refused CALL is numbered");
            T.Eq(5u, lab.S.Applied, "and passed over");
        });

        T.Run("Applied waits for the fence: work the strings queued keeps it back until that work starts", () =>
        {
            var lab = new Lab(BridgeFamily.Delta, accept: BridgeFamily.Delta);
            lab.Page.Send("plain");
            lab.Frame();
            T.Eq(1u, lab.S.Applied, "nothing queued: applied at dispatch");
            lab.Page.Send("work:a");
            lab.Page.Send("plain2");
            lab.Frame();
            T.Eq(3u, lab.S.ItemsDone, "dispatched");
            T.Eq(1u, lab.S.Applied, "held back by the work still to start");
            T.Eq(1u, lab.Rig.View.AppliedMark, "what a value read now is stamped with");
            lab.RunScheduler();
            T.Eq(3u, lab.S.Applied, "moved on as the work started (the fence's Drained, no frame needed)");
        });

        T.Run("Held items are numbered as they arrive and count as done when they run", () =>
        {
            var lab = new Lab(BridgeFamily.Delta, accept: BridgeFamily.Delta, hold: true);
            T.True(lab.S.Held, "held");
            T.Eq(BridgeFamily.Delta, lab.S.Accepted, "ACCEPT runs at ingest, even held");
            lab.Page.Send("a");
            lab.Page.Send("b");
            lab.Frame();
            T.Eq(2u, lab.S.ItemsIn, "numbered");
            T.Eq(0u, lab.S.ItemsDone, "not run");
            lab.Rig.View.NotifyOnLoad();
            lab.Frame();
            T.Eq(2u, lab.S.ItemsDone, "run in order");
            T.Eq(2u, lab.S.Applied, "applied");
        });

        T.Run("a RESET keeps the session's numbering and its ACCEPT; the page's re-sent items are numbered once", () =>
        {
            var lab = new Lab(BridgeFamily.Delta, accept: BridgeFamily.Delta);
            lab.Page.Send("a");
            lab.Frame();
            // A frame past the credit window makes Unity RESET; the page says HELLO again and sends the rest.
            lab.Page.HonorWindow = false;
            lab.Page.Send(new string('x', 600_000));
            lab.Page.Send(new string('y', 600_000));
            lab.Frame();
            lab.Page.HonorWindow = true;
            lab.Frame();
            T.True(lab.Rig.View.Stats.ProtocolResets >= 1, "reset");
            T.Eq(BridgeFamily.Delta, lab.S.Accepted, "still accepted");
            T.Eq(3u, lab.S.ItemsIn, "each item numbered once");
            T.Eq(3u, lab.S.Applied, "applied");
        });

        // -------------------------------------------------------------------------------------- ANNOUNCE, DESTROYED
        T.Run("ANNOUNCE and DESTROYED: consecutive entries share one op, in order, and decode as sent", () =>
        {
            var lab = new Lab(BridgeFamily.Announce | BridgeFamily.Destroyed, accept: BridgeFamily.Announce | BridgeFamily.Destroyed);
            var v = lab.Rig.View;
            v.SendAnnounce(new AnnounceEntry(41, true, "Cube 名前", 3, "Untagged", 0));
            v.SendAnnounce(new AnnounceEntry(41, 42, 37));
            v.SendDestroyed(41, DestroyedReason.Destroyed);
            v.SendDestroyed(42, DestroyedReason.Swept);
            v.SendDestroyed(0, DestroyedReason.Destroyed); // not a handle: ignored
            lab.Frame();
            T.Seq(new[] { "#50", "#53" }, lab.Got(), "two ops");
            T.True(BridgePush.TryDecodeAnnounce(lab.Page.Pushes[0].payload, out var a), "announce decodes");
            T.Eq(2, a.Count, "entries");
            T.True(a[0].Kind == BridgePush.AnnounceObject && a[0].Oid == 41 && a[0].Active && a[0].Name == "Cube 名前" && a[0].Layer == 3 && a[0].Tag == "Untagged" && a[0].Parent == 0, "object");
            T.True(a[1].Kind == BridgePush.AnnounceComponent && a[1].Oid == 41 && a[1].Cid == 42 && a[1].ComponentType == 37, "component");
            T.True(BridgePush.TryDecodeDestroyed(lab.Page.Pushes[1].payload, out var d), "destroyed decodes");
            T.Seq(new[] { "41:1", "42:2" }, d.Select(x => x.handle + ":" + x.reason), "handles and reasons");
        });

        T.Run("ANNOUNCE names and tags are cut at a code point, within MaxText", () =>
        {
            var lab = new Lab(BridgeFamily.Announce, accept: BridgeFamily.Announce);
            string longName = new string('a', BridgePush.MaxNameChars - 1) + "😀" + "tail";
            lab.Rig.View.SendAnnounce(new AnnounceEntry(1, true, longName, 0, "t", 0));
            lab.Frame();
            T.True(BridgePush.TryDecodeAnnounce(lab.Page.Pushes[0].payload, out var a), "decodes");
            T.Eq(BridgePush.MaxNameChars - 1, a[0].Name.Length, "cut before the pair");
        });

        // ------------------------------------------------------------------------------------------- codecs
        T.Run("push vectors (TS-generated): decoded alike, re-encoded byte for byte", () =>
        {
            var path = Path.Combine(Here, "..", "vectors", "bridge_push_v1_vectors.json");
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            int n = 0;
            foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
            {
                n++;
                string name = c.GetProperty("name").GetString();
                uint opcode = (uint)c.GetProperty("opcode").GetInt32();
                var payload = BridgeHex.FromHex(c.GetProperty("payload").GetString());
                bool error = c.TryGetProperty("error", out var err) && err.GetBoolean();
                switch ((int)opcode)
                {
                    case BridgeOp.Accept:
                    {
                        long f = BridgePush.DecodeAccept(payload);
                        if (error) { T.Eq(-1L, f, name); break; }
                        T.Eq((long)c.GetProperty("families").GetInt32(), f, name);
                        T.Eq(BridgeHex.ToHex(payload), BridgeHex.ToHex(BridgePush.EncodeAccept((uint)f)), name + " re-encoded");
                        break;
                    }
                    case BridgeOp.Delta:
                    {
                        bool ok = BridgePush.TryDecodeDelta(payload, out uint applied, out var entries);
                        if (error) { T.True(!ok, name + ": refused"); break; }
                        T.True(ok, name);
                        var want = c.GetProperty("delta");
                        T.Eq(want.GetProperty("applied").GetUInt32(), applied, name + " applied");
                        var batch = new BridgePushBatch(BridgeFamily.Delta);
                        int i = 0;
                        foreach (var e in want.GetProperty("entries").EnumerateArray())
                        {
                            var x = entries[i++];
                            T.Eq(e.GetProperty("oid").GetInt32() + "/" + e.GetProperty("cid").GetInt32() + "/" + e.GetProperty("componentType").GetInt32() + "/" + e.GetProperty("prop").GetInt32() + " " + e.GetProperty("text").GetString(),
                                x.Oid + "/" + x.Cid + "/" + x.ComponentType + "/" + x.Prop + " " + x.Text, name);
                            batch.AddDelta(x, applied);
                        }
                        T.Eq(i, entries.Count, name + " count");
                        if (entries.Count > 0)
                            T.Eq(BridgeHex.ToHex(BridgeOps.Encode(opcode, payload)), BridgeHex.ToHex(batch.Encode()), name + " re-encoded");
                        break;
                    }
                    case BridgeOp.Announce:
                    {
                        bool ok = BridgePush.TryDecodeAnnounce(payload, out var entries);
                        if (error) { T.True(!ok, name + ": refused"); break; }
                        T.True(ok, name);
                        var batch = new BridgePushBatch(BridgeFamily.Announce);
                        int i = 0;
                        foreach (var e in c.GetProperty("announce").EnumerateArray())
                        {
                            var x = entries[i++];
                            T.Eq(e.GetProperty("kind").GetInt32(), (int)x.Kind, name + " kind");
                            T.Eq(e.GetProperty("oid").GetInt32(), x.Oid, name + " oid");
                            if (x.Kind == BridgePush.AnnounceObject)
                            {
                                T.Eq(e.GetProperty("active").GetBoolean(), x.Active, name + " active");
                                T.Eq(e.GetProperty("name").GetString(), x.Name, name + " name");
                                T.Eq(e.GetProperty("layer").GetInt32(), x.Layer, name + " layer");
                                T.Eq(e.GetProperty("tag").GetString(), x.Tag, name + " tag");
                                T.Eq(e.GetProperty("parent").GetInt32(), x.Parent, name + " parent");
                            }
                            else
                            {
                                T.Eq(e.GetProperty("cid").GetInt32(), x.Cid, name + " cid");
                                T.Eq(e.GetProperty("componentType").GetInt32(), x.ComponentType, name + " componentType");
                            }
                            batch.AddAnnounce(x);
                        }
                        T.Eq(BridgeHex.ToHex(BridgeOps.Encode(opcode, payload)), BridgeHex.ToHex(batch.Encode()), name + " re-encoded");
                        break;
                    }
                    case BridgeOp.Destroyed:
                    {
                        bool ok = BridgePush.TryDecodeDestroyed(payload, out var entries);
                        if (error) { T.True(!ok, name + ": refused"); break; }
                        T.True(ok, name);
                        var batch = new BridgePushBatch(BridgeFamily.Destroyed);
                        int i = 0;
                        foreach (var e in c.GetProperty("destroyed").EnumerateArray())
                        {
                            var x = entries[i++];
                            T.Eq(e.GetProperty("handle").GetInt32() + ":" + e.GetProperty("reason").GetInt32(), x.handle + ":" + x.reason, name);
                            batch.AddDestroyed(x.handle, x.reason);
                        }
                        T.Eq(BridgeHex.ToHex(BridgeOps.Encode(opcode, payload)), BridgeHex.ToHex(batch.Encode()), name + " re-encoded");
                        break;
                    }
                    default:
                        throw new Exception("unknown opcode in " + name);
                }
            }
            T.True(n >= 20, "cases read: " + n);
        });
    }

    /// <summary>The decoders take any bytes without throwing, and what they accept re-encodes to the same bytes.</summary>
    public static void Fuzz(int n, uint seed)
    {
        T.Run($"push decoders on {n} damaged payloads never throw; accepted ones re-encode exactly (seed {seed})", () =>
        {
            var rng = new Rng(seed ^ 0x5eed);
            var batch = new BridgePushBatch(BridgeFamily.Delta);
            batch.AddDelta(D(300, 16384, 2, 127, "127§2§1"), 9);
            batch.AddDelta(D(300, 0, 0, 5, "5‡1‡2‡3"), 9);
            var seeds = new List<byte[]> { Payload(batch.Encode()) };
            var an = new BridgePushBatch(BridgeFamily.Announce);
            an.AddAnnounce(new AnnounceEntry(42, false, "Ünïcode", 31, "UserTag1", 41));
            an.AddAnnounce(new AnnounceEntry(42, 43, 37));
            seeds.Add(Payload(an.Encode()));
            var de = new BridgePushBatch(BridgeFamily.Destroyed);
            de.AddDestroyed(16384, 2); de.AddDestroyed(5, 1);
            seeds.Add(Payload(de.Encode()));
            int accepted = 0;
            for (int i = 0; i < n; i++)
            {
                var src = seeds[rng.Int(seeds.Count)];
                var b = (byte[])src.Clone();
                int edits = 1 + rng.Int(3);
                for (int k = 0; k < edits; k++)
                {
                    switch (rng.Int(3))
                    {
                        case 0: if (b.Length > 0) b[rng.Int(b.Length)] = (byte)rng.Next(); break;
                        case 1: b = b.Take(rng.Int(b.Length + 1)).ToArray(); break;
                        default: b = b.Concat(rng.Bytes(1 + rng.Int(4))).ToArray(); break;
                    }
                }
                if (BridgePush.TryDecodeDelta(b, out uint applied, out var d))
                {
                    accepted++;
                    var again = new BridgePushBatch(BridgeFamily.Delta);
                    var keys = new HashSet<(int, int, int)>();
                    bool distinct = true;
                    foreach (var e in d) { distinct &= keys.Add((e.Oid, e.Cid, e.Prop)); again.AddDelta(e, applied); }
                    if (distinct && d.Count > 0 && BridgeUtf8Clean(d))
                        T.Eq(BridgeHex.ToHex(b), BridgeHex.ToHex(Payload(again.Encode())), "delta re-encode");
                }
                if (BridgePush.TryDecodeAnnounce(b, out var a))
                {
                    accepted++;
                    var again = new BridgePushBatch(BridgeFamily.Announce);
                    foreach (var e in a) again.AddAnnounce(e);
                    T.Eq(BridgeHex.ToHex(b), BridgeHex.ToHex(Payload(again.Encode())), "announce re-encode");
                }
                if (BridgePush.TryDecodeDestroyed(b, out var x))
                {
                    accepted++;
                    var again = new BridgePushBatch(BridgeFamily.Destroyed);
                    foreach (var e in x) again.AddDestroyed(e.handle, e.reason);
                    if (x.Count > 0) T.Eq(BridgeHex.ToHex(b), BridgeHex.ToHex(Payload(again.Encode())), "destroyed re-encode");
                }
                BridgePush.DecodeAccept(b);
            }
            T.True(accepted > 0, "some damaged payloads still decode");
        });
    }

    // Names longer than MaxNameChars are cut on encode, so only shorter ones re-encode exactly.
    static bool BridgeUtf8Clean(List<DeltaEntry> d) => d.All(e => e.Text.Length <= BridgePush.MaxNameChars);

    /// <summary>The payload of a whole encoded op.</summary>
    static byte[] Payload(byte[] op)
    {
        var ops = new List<RawOp>();
        T.Eq(BridgeError.None, BridgeOps.Decode(op, ops), "one op");
        T.Eq(1, ops.Count, "one op");
        return op.Skip(ops[0].Offset).Take(ops[0].Length).ToArray();
    }
}
