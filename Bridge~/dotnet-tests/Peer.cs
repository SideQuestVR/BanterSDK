using System;
using System.IO;
using System.Text.Json;
using BS.Bridge;

/// <summary>
/// --peer: the real BridgeView driven over stdin/stdout by the real TS PageSession (interop.mjs).
/// In:  "R <json string>" page message · "L <json url>" LoadStarted · "N" OnLoad · "S <json string>" Unity sends ·
///      "D" drain+tick+flush · "Q" quit.
/// Out: "O <json string>" message to the page · "X <epoch> <json string>" a string Unity dispatched · "." end of step.
/// Requests ("!i!:&lt;id&gt;¶…") are answered "!i!:&lt;id&gt;¶ok-&lt;epoch&gt;" unless the command is "!slow", whose
/// answer is sent by the next "F" (finish) line, from the captured context (the way an await would).
/// </summary>
static class Peer
{
    public static int Run()
    {
        Console.InputEncoding = System.Text.Encoding.UTF8;
        var rig = new Rig(new BridgeViewOptions { LoadStartedMayTrail = false });
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
                case 'N': rig.View.NotifyOnLoad(); break;
                case 'S': rig.View.Send(JsonSerializer.Deserialize<string>(arg)); break;
                case 'F': slow?.Invoke(); slow = null; break;
                case 'Q': stdout.Flush(); return 0;
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
