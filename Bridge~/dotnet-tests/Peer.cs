using System;
using System.IO;
using System.Text.Json;
using BS.Bridge;

/// <summary>
/// --peer: the real BridgeView driven over stdin/stdout by the real TS PageSession (interop.mjs).
/// In:  "R <json string>" page message · "L <json url>" LoadStarted · "N" OnLoad · "S <json string>" Unity sends ·
///      "D" drain+tick+flush · "T <ms>" the clock moves on · "Q" quit.
/// Out: "O <json string>" message to the page · "X <epoch> <json string>" a string Unity dispatched · "." end of step.
/// Requests ("!i!:&lt;id&gt;¶…") are answered "!i!:&lt;id&gt;¶ok-&lt;epoch&gt;" unless the command is "!slow", whose
/// answer is sent by the next "F" (finish) line, from the captured context (the way an await would).
/// With BRIDGE_PEER_CALLS set the view has a call host, over FakeApi's table: handle 1001 is alive, 1002 dead, 1003 outside
/// the world. "M &lt;id&gt; &lt;argc&gt; &lt;0|1 static&gt;" adds a member that answers its argument count and logs "C &lt;id&gt; &lt;json args&gt;".
/// BRIDGE_PEER_FAMILIES (a number) is what the view offers besides CALL. "P &lt;json [oid,cid,ct,prop,text]&gt;" reads a
/// DELTA value now (stamped with the applied count as it stands), "K &lt;handle&gt;" reports one destroyed, "J &lt;json&gt;"
/// announces an object ([1,oid,active,name,layer,tag,parent]) or a component ([2,oid,cid,componentType]), and "Z" prints
/// "Z &lt;json&gt;" with the current document's accepted families, items in and applied count.
/// </summary>
static class Peer
{
    public static int Run()
    {
        Console.InputEncoding = System.Text.Encoding.UTF8;
        var options = new BridgeViewOptions { LoadStartedMayTrail = false };
        if (uint.TryParse(Environment.GetEnvironmentVariable("BRIDGE_PEER_FAMILIES"), out var families)) options.Families = families;
        BridgeMember[] members = null;
        Action seed = null;
        if (Environment.GetEnvironmentVariable("BRIDGE_PEER_CALLS") != null)
        {
            members = new BridgeMember[4096];
            FakeApi.Table().CopyTo(members, 0);
            var handles = new FakeHandles();
            // After OnLoad: a LoadStarted forgets the last world's handles.
            seed = () =>
            {
                handles.Table[1001] = new FakeObject(2.5f);
                handles.Table[1002] = new FakeObject();
                handles.Dead.Add(1002);
                handles.Table[1003] = FakeApi.Outsider;
                handles.OutOfWorld.Add(FakeApi.Outsider);
            };
            options.CallHost = new BridgeCallHost(members, handles, () => 1);
        }
        var rig = new Rig(options);
        var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = false };
        Action slow = null;
        rig.Disp.OnText = (s, t) =>
        {
            stdout.WriteLine("X " + (s?.Epoch ?? 0) + " " + JsonSerializer.Serialize(t));
            int id = BridgeView.LegacyRequestId(t, true);
            if (id <= 0 || id == BridgeView.NoReplyRequestId) return true;
            if (t.Contains("!slow"))
            {
                var ec = System.Threading.ExecutionContext.Capture();
                slow = () => System.Threading.ExecutionContext.Run(ec, _ => rig.View.Send("!i!:" + id + "¶ok-" + BridgeContext.CurrentEpoch), null);
                return true;
            }
            rig.View.Send("!i!:" + id + "¶ok-" + BridgeContext.CurrentEpoch);
            return true;
        };
        string line;
        while ((line = Console.ReadLine()) != null)
        {
            char c = line.Length > 0 ? line[0] : ' ';
            string arg = line.Length > 2 ? line.Substring(2) : "";
            switch (c)
            {
                case 'R': rig.View.Receive(JsonSerializer.Deserialize<string>(arg)); break;
                case 'L': rig.View.NotifyLoadStarted(JsonSerializer.Deserialize<string>(arg)); break;
                case 'N': rig.View.NotifyOnLoad(); seed?.Invoke(); break;
                case 'S': rig.View.Send(JsonSerializer.Deserialize<string>(arg)); break;
                case 'F': slow?.Invoke(); slow = null; break;
                case 'T': rig.Now += long.Parse(arg); break;
                case 'Q': stdout.Flush(); return 0;
                case 'M':
                {
                    var p = arg.Trim('"').Split(' ');
                    int id = int.Parse(p[0]), argc = int.Parse(p[1]);
                    members[id] = new BridgeMember(id, BridgeTier.Write, false, p[2] == "1", argc, "", 0, c =>
                    {
                        var args = new string[c.ArgCount];
                        for (int i = 0; i < c.ArgCount; i++) args[i] = CallTests.Describe(c.Args[i]);
                        stdout.WriteLine("C " + id + " " + JsonSerializer.Serialize(args));
                        c.RetInt(c.ArgCount);
                    });
                    break;
                }
                case 'P':
                {
                    var d = JsonSerializer.Deserialize<JsonElement>(arg);
                    rig.View.SendDelta(d[0].GetInt32(), d[1].GetInt32(), d[2].GetInt32(), d[3].GetInt32(), d[4].GetString(), rig.View.AppliedMark);
                    break;
                }
                case 'K': rig.View.SendDestroyed(int.Parse(arg.Trim('"')), DestroyedReason.Destroyed); break;
                case 'J':
                {
                    var a = JsonSerializer.Deserialize<JsonElement>(arg);
                    if (a[0].GetInt32() == 1) rig.View.SendAnnounce(new AnnounceEntry(a[1].GetInt32(), a[2].GetInt32() == 1, a[3].GetString(), a[4].GetInt32(), a[5].GetString(), a[6].GetInt32()));
                    else rig.View.SendAnnounce(new AnnounceEntry(a[1].GetInt32(), a[2].GetInt32(), a[3].GetInt32()));
                    break;
                }
                case 'Z':
                {
                    var s = rig.View.Current;
                    stdout.WriteLine("Z " + JsonSerializer.Serialize(new { accepted = s?.Accepted ?? 0, itemsIn = s?.ItemsIn ?? 0, applied = s?.Applied ?? 0 }));
                    break;
                }
                case 'D':
                    rig.View.Drain();
                    rig.View.Tick();
                    rig.View.Flush();
                    break;
            }
            foreach (var o in rig.Sink.Take()) stdout.WriteLine("O " + JsonSerializer.Serialize(o));
            stdout.WriteLine(".");
            stdout.Flush();
        }
        return 0;
    }
}
