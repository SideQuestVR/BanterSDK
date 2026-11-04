using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using BS.Bridge;

/// <summary>
/// 100k random frames per run. Decoders must never throw, and anything accepted must re-encode to exactly the input
/// (so a damaged frame is accepted only if it is itself a valid, canonical frame). The view fed garbage must never
/// throw and never dispatch from a frame it should refuse.
/// </summary>
static class FuzzTests
{
    public static void Run(int n, uint seed)
    {
        Console.WriteLine($"fuzz (seed {seed}, {n} frames)");
        var pool = VectorTests.V.GetProperty("frames").EnumerateArray().Where(x => x.GetProperty("ok").GetBoolean())
            .Select(x => BridgeHex.FromHex(x.GetProperty("hex").GetString())).ToList();
        var carrierPool = VectorTests.V.GetProperty("carriers").EnumerateArray().Where(x => x.GetProperty("ok").GetBoolean())
            .Select(x => x.GetProperty("carrier").GetString()).ToList();

        T.Run("frame decoder: never throws, never accepts a non-canonical or invalid frame", () =>
        {
            var r = new Rng(seed);
            int accepted = 0;
            for (int i = 0; i < n; i++)
            {
                int mode = r.Int(4);
                byte[] input = mode == 0 ? r.Bytes(r.Int(64))
                    : mode == 1 ? Mutate(r, pool[r.Int(pool.Count)])
                    : mode == 2 ? Mutate(r, RandomValidFrame(r))
                    : RandomValidFrame(r);
                var e = BridgeFrame.TryDecode(input, out var h, out var payload);
                if (e != BridgeError.None) continue;
                accepted++;
                T.Eq(BridgeError.None, BridgeFrame.Validate(h, false));
                T.Eq(h.Length, payload.Length);
                if (BridgeHex.ToHex(BridgeFrame.Encode(h, payload)) != BridgeHex.ToHex(input))
                    throw new Exception("accepted a frame that does not re-encode: " + BridgeHex.ToHex(input));
            }
            T.True(accepted > n / 8, "fuzz mix degenerated: " + accepted);
        });

        T.Run("carrier decoder: never throws; accepted strings re-encode exactly", () =>
        {
            var r = new Rng(seed ^ 0x5bd1e995);
            for (int i = 0; i < n / 4; i++)
            {
                string s = r.Int(2) == 1 ? MutateString(r, carrierPool[r.Int(carrierPool.Count)]) : MutateString(r, BridgeCarrier.EncodeBinary(RandomValidFrame(r)));
                if (BridgeCarrier.TryDecode(s, out var f) != BridgeError.None) continue;
                string again;
                if (f.IsText) { var h = f.Header; h.Flags = (ushort)(h.Flags & ~FrameFlags.Text); again = BridgeCarrier.EncodeText(h, f.Body); }
                else again = BridgeCarrier.EncodeBinary(f.Header, f.Payload);
                if (again != s) throw new Exception("carrier did not re-encode");
            }
        });

        T.Run("op and text-body decoders: never throw; accepted input re-encodes exactly", () =>
        {
            var r = new Rng(seed ^ 0x1234567);
            var ops = new List<RawOp>();
            var items = new List<BridgeItem>();
            var texts = new List<string>();
            for (int i = 0; i < n / 4; i++)
            {
                var b = r.Int(2) == 1 ? r.Bytes(r.Int(32)) : Mutate(r, BridgeOps.Encode((uint)r.Int(300), r.Bytes(r.Int(10))));
                ops.Clear();
                if (BridgeOps.Decode(b, ops) == BridgeError.None)
                {
                    var w = new ByteWriter();
                    foreach (var o in ops) BridgeOps.Write(w, o.Opcode, new ReadOnlySpan<byte>(b, o.Offset, o.Length));
                    if (BridgeHex.ToHex(w.Span) != BridgeHex.ToHex(b)) throw new Exception("ops did not re-encode");
                }
                items.Clear();
                BridgeMessage.Parse(null, b, items);
                HelloPayload.TryDecode(b); WelcomePayload.TryDecode(b); FarewellPayload.TryDecode(b, out _); ReplyPayload.TryDecode(b, out _, out _, out _);
                BridgeFrame.TryDecodeClose(b, out _);
                var body = MutateString(r, BridgeCarrier.PackEntries(new[] { "ab", "", "¶§|" }));
                texts.Clear();
                if (BridgeCarrier.UnpackEntries(body, 0, body.Length, texts) && BridgeCarrier.PackEntries(texts) != body) throw new Exception("entries did not re-encode");
            }
        });

        T.Run("view fed garbage never throws and never dispatches a refused frame", () =>
        {
            var r = new Rng(seed ^ 0x7777);
            var rig = new Rig();
            var page = new TestPage("fuzz");
            page.Hello();
            rig.Pump(page);
            T.Eq("open", page.State);
            int restarts = 0;
            for (int i = 0; i < n / 5; i++)
            {
                var s = rig.View.Current;
                if (s == null || s.State != BridgeSessionState.Open)
                {
                    rig = new Rig(); page = new TestPage("fuzz" + (++restarts)); page.Hello(); rig.Pump(page);
                    s = rig.View.Current;
                }
                string input;
                if (r.Int(2) == 0)
                {
                    var good = BridgeCarrier.EncodeText(new FrameHeader(FrameKind.Data, FrameFlags.Complete, s.Epoch, s.Link.LastSeqReceived + 1, 0, 0), BridgeCarrier.PackEntries(new[] { "x" }));
                    input = MutateString(r, good);
                }
                else input = BridgeCarrier.EncodeBinary(Mutate(r, RandomValidFrame(r)));
                int before = rig.Disp.Ran.Count;
                rig.View.Receive(input);
                rig.View.Drain();
                rig.View.Flush();
                rig.View.Tick();
                rig.Sink.Take();
                if (rig.Disp.Ran.Count > before && BridgeCarrier.IsCarrier(input))
                {
                    var ok = BridgeCarrier.TryDecode(input, out var f) == BridgeError.None && f.Header.Kind == FrameKind.Data
                             && (f.Header.Epoch == s.Epoch || f.Header.Epoch == 0);
                    if (!ok) throw new Exception("dispatched from a refused frame");
                }
            }
            T.True(restarts >= 1, "the fuzz never broke a session");
        });
    }

    static byte[] RandomValidFrame(Rng r)
    {
        byte[] kinds = { FrameKind.Data, FrameKind.Data, FrameKind.Data, FrameKind.Ack, FrameKind.Heartbeat, FrameKind.Credit, FrameKind.Close, FrameKind.Reset, FrameKind.Pause };
        byte kind = kinds[r.Int(kinds.Length)];
        byte[] payload;
        ushort flags = 0; uint seq = 0;
        switch (kind)
        {
            case FrameKind.Data:
                flags = new ushort[] { 3, 1, 0, 2, 3 | FrameFlags.Urgent }[r.Int(5)];
                seq = 1 + (uint)r.Int(1000);
                payload = r.Int(2) == 1 ? BridgeOps.Encode(BridgeOp.LegacyText, BridgeUtf8.Encode("!i!:" + r.Int(9999) + "¶!gp!¶")) : r.Bytes(1 + r.Int(40));
                break;
            case FrameKind.Credit: payload = new byte[] { (byte)r.Int(256), (byte)r.Int(256), (byte)r.Int(16), 0 }; break;
            case FrameKind.Close: case FrameKind.Reset: payload = new byte[] { (byte)r.Int(13), 1, 0, 0, 0, 0x61 }; break;
            default: payload = Array.Empty<byte>(); break;
        }
        return BridgeFrame.Encode(new FrameHeader(kind, flags, (uint)r.Int(5), seq, (uint)r.Int(1000), payload.Length), payload);
    }

    static byte[] Mutate(Rng r, byte[] b)
    {
        var x = new List<byte>(b);
        int k = 1 + r.Int(4);
        for (int i = 0; i < k; i++)
        {
            int op = r.Int(5);
            if (op == 0 && x.Count > 0) x[r.Int(x.Count)] = (byte)r.Int(256);
            else if (op == 1 && x.Count > 0) { int j = r.Int(x.Count); x[j] = (byte)(x[j] ^ (1 << r.Int(8))); }
            else if (op == 2) x.Insert(r.Int(x.Count + 1), (byte)r.Int(256));
            else if (op == 3 && x.Count > 0) x.RemoveAt(r.Int(x.Count));
            else if (x.Count > 0) { int cut = r.Int(x.Count); x.RemoveRange(cut, x.Count - cut); }
        }
        return x.ToArray();
    }

    static string MutateString(Rng r, string s)
    {
        var a = new List<char>(s);
        const string alphabet = "AZaz09+/=\u0002BT:¶\uD83D";
        int k = 1 + r.Int(3);
        for (int i = 0; i < k; i++)
        {
            int op = r.Int(4), at = r.Int(a.Count + 1);
            if (op == 0 && a.Count > 0) a[Math.Min(at, a.Count - 1)] = alphabet[r.Int(alphabet.Length)];
            else if (op == 1) a.Insert(at, alphabet[r.Int(alphabet.Length)]);
            else if (op == 2 && a.Count > 1) a.RemoveAt(Math.Min(at, a.Count - 1));
            else if (at < a.Count) a.RemoveRange(at, a.Count - at);
        }
        return new string(a.ToArray());
    }
}
