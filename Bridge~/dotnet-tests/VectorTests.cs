using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using BS.Bridge;

static class VectorTests
{
    // The creator-sdk package is two folders up from this file (Bridge~/dotnet-tests).
    static readonly string SdkRoot = Path.GetFullPath(Path.Combine(SourceDir(), "..", ".."));
    static string SourceDir([System.Runtime.CompilerServices.CallerFilePath] string path = "") => Path.GetDirectoryName(path);
    static readonly string VectorPath = Path.Combine(SdkRoot, "Bridge~", "vectors", "bridge_v1_vectors.json");
    static readonly string Models = Path.Combine(SdkRoot, "Runtime", "Scripts", "Models");

    public static JsonElement V;

    static byte[] Hex(JsonElement e) => BridgeHex.FromHex(e.GetString());
    static FrameHeader Header(JsonElement e) => new FrameHeader(
        (byte)e.GetProperty("kind").GetInt32(), (ushort)e.GetProperty("flags").GetInt32(), e.GetProperty("epoch").GetUInt32(),
        e.GetProperty("seq").GetUInt32(), e.GetProperty("ack").GetUInt32(), e.GetProperty("length").GetInt32());
    static void SameHeader(FrameHeader a, FrameHeader b, string what)
    {
        T.Eq(a.Kind, b.Kind, what + " kind"); T.Eq(a.Flags, b.Flags, what + " flags"); T.Eq(a.Epoch, b.Epoch, what + " epoch");
        T.Eq(a.Seq, b.Seq, what + " seq"); T.Eq(a.Ack, b.Ack, what + " ack"); T.Eq(a.Length, b.Length, what + " length");
    }

    public static void Run()
    {
        V = JsonDocument.Parse(File.ReadAllText(VectorPath)).RootElement;
        Console.WriteLine("golden vectors");

        T.Run("constants match", () =>
        {
            var c = V.GetProperty("constants");
            T.Eq(BridgeWire.Magic, c.GetProperty("magic").GetUInt32());
            T.Eq(BridgeWire.MaxFramePayload, c.GetProperty("maxFramePayload").GetInt32());
            T.Eq(BridgeWire.FragmentSize, c.GetProperty("fragmentSize").GetInt32());
            T.Eq(BridgeWire.MaxWindow, c.GetProperty("maxWindow").GetInt32());
        });

        T.Run("varints", () =>
        {
            foreach (var x in V.GetProperty("varints").EnumerateArray())
            {
                var b = Hex(x.GetProperty("hex"));
                int pos = 0;
                T.Eq(x.GetProperty("value").GetInt64(), BridgeOps.ReadVarint(b, ref pos), x.GetProperty("hex").GetString());
                T.Eq(b.Length, pos);
                var w = new ByteWriter(); w.Varint((uint)x.GetProperty("value").GetInt64());
                T.Eq(x.GetProperty("hex").GetString(), BridgeHex.ToHex(w.Span));
            }
            foreach (var x in V.GetProperty("badVarints").EnumerateArray())
            {
                var b = Hex(x); int pos = 0;
                T.Eq(-1L, BridgeOps.ReadVarint(b, ref pos), "bad varint " + x.GetString());
            }
        });

        T.Run("utf8 strict", () =>
        {
            foreach (var x in V.GetProperty("utf8").EnumerateArray())
            {
                var text = x.GetProperty("text").GetString();
                T.Eq(x.GetProperty("hex").GetString(), BridgeHex.ToHex(BridgeUtf8.Encode(text)), text);
                T.Eq(text, BridgeUtf8.Decode(Hex(x.GetProperty("hex"))));
            }
            foreach (var x in V.GetProperty("badUtf8").EnumerateArray()) T.Eq(null, BridgeUtf8.Decode(Hex(x)), "bad utf8 " + x.GetString());
            T.Eq("61efbfbd62", BridgeHex.ToHex(BridgeUtf8.Encode("a\ud800b")), "lone surrogate");
        });

        T.Run("base64 strict", () =>
        {
            foreach (var x in V.GetProperty("base64").EnumerateArray())
            {
                var b = Hex(x.GetProperty("hex"));
                var s = x.GetProperty("b64").GetString();
                T.Eq(s, BridgeBase64.Encode(b));
                T.Eq(x.GetProperty("hex").GetString(), BridgeHex.ToHex(BridgeBase64.TryDecode(s, 0, s.Length)));
            }
            foreach (var x in V.GetProperty("badBase64").EnumerateArray())
            {
                var s = x.GetString();
                T.Eq(null, BridgeBase64.TryDecode(s, 0, s.Length), "bad base64 " + s);
            }
        });

        T.Run("frames (" + V.GetProperty("frames").GetArrayLength() + ")", () =>
        {
            foreach (var x in V.GetProperty("frames").EnumerateArray())
            {
                var name = x.GetProperty("name").GetString();
                var b = Hex(x.GetProperty("hex"));
                var e = BridgeFrame.TryDecode(b, out var h, out var payload);
                if (!x.GetProperty("ok").GetBoolean()) { T.Eq(x.GetProperty("error").GetString(), BridgeErrors.Name(e), name); continue; }
                T.Eq("NONE", BridgeErrors.Name(e), name);
                SameHeader(Header(x.GetProperty("header")), h, name);
                T.Eq(x.GetProperty("payloadHex").GetString(), BridgeHex.ToHex(payload), name);
                T.Eq(x.GetProperty("hex").GetString(), BridgeHex.ToHex(BridgeFrame.Encode(h, payload)), name + " re-encode");
            }
        });

        T.Run("carriers (" + V.GetProperty("carriers").GetArrayLength() + ")", () =>
        {
            foreach (var x in V.GetProperty("carriers").EnumerateArray())
            {
                var name = x.GetProperty("name").GetString();
                var s = x.GetProperty("carrier").GetString();
                var e = BridgeCarrier.TryDecode(s, out var f);
                if (!x.GetProperty("ok").GetBoolean()) { T.Eq(x.GetProperty("error").GetString(), BridgeErrors.Name(e), name); continue; }
                T.Eq("NONE", BridgeErrors.Name(e), name);
                T.Eq(x.GetProperty("form").GetString(), f.IsText ? "text" : "binary", name);
                SameHeader(Header(x.GetProperty("header")), f.Header, name);
                if (f.IsText)
                {
                    T.Eq(x.GetProperty("body").GetString(), f.Body, name);
                    if (x.TryGetProperty("entries", out var entries))
                    {
                        var got = new List<string>();
                        T.True(BridgeCarrier.UnpackEntries(f.Body, 0, f.Body.Length, got), name + " entries");
                        T.Seq(entries.EnumerateArray().Select(z => z.GetString()), got, name);
                    }
                    var h = f.Header; h.Flags = (ushort)(h.Flags & ~FrameFlags.Text);
                    T.Eq(s, BridgeCarrier.EncodeText(h, f.Body), name + " re-encode");
                }
                else
                {
                    T.Eq(x.GetProperty("payloadHex").GetString(), BridgeHex.ToHex(f.Payload), name);
                    T.Eq(s, BridgeCarrier.EncodeBinary(f.Header, f.Payload), name + " re-encode");
                }
            }
        });

        T.Run("text bodies and split", () =>
        {
            foreach (var x in V.GetProperty("textBodies").EnumerateArray())
            {
                var body = x.GetProperty("body").GetString();
                var got = new List<string>();
                bool ok = BridgeCarrier.UnpackEntries(body, 0, body.Length, got);
                T.Eq(x.GetProperty("ok").GetBoolean(), ok, "body " + body);
                if (ok) T.Seq(x.GetProperty("entries").EnumerateArray().Select(z => z.GetString()), got, body);
            }
            foreach (var x in V.GetProperty("textSplit").EnumerateArray())
                T.Seq(x.GetProperty("pieces").EnumerateArray().Select(z => z.GetString()),
                      BridgeCarrier.SplitText(x.GetProperty("body").GetString(), x.GetProperty("size").GetInt32()), "split");
        });

        T.Run("ops", () =>
        {
            foreach (var x in V.GetProperty("ops").EnumerateArray())
            {
                var name = x.GetProperty("name").GetString();
                var b = Hex(x.GetProperty("hex"));
                var ops = new List<RawOp>();
                var e = BridgeOps.Decode(b, ops);
                if (!x.GetProperty("ok").GetBoolean())
                {
                    T.Eq(x.GetProperty("error").GetString(), BridgeErrors.Name(e), name);
                    T.Eq(x.GetProperty("opsBefore").GetInt32(), ops.Count, name);
                    continue;
                }
                T.Eq(BridgeError.None, e, name);
                T.Seq(x.GetProperty("ops").EnumerateArray().Select(o => o.GetProperty("opcode").GetUInt32() + ":" + o.GetProperty("payloadHex").GetString()),
                      ops.Select(o => o.Opcode + ":" + BridgeHex.ToHex(new ReadOnlySpan<byte>(b, o.Offset, o.Length))), name);
                var w = new ByteWriter();
                foreach (var o in ops) BridgeOps.Write(w, o.Opcode, new ReadOnlySpan<byte>(b, o.Offset, o.Length));
                T.Eq(x.GetProperty("hex").GetString(), BridgeHex.ToHex(w.Span), name + " re-encode");
            }
        });

        T.Run("payloads round-trip and bad payloads refused", () =>
        {
            var p = V.GetProperty("payloads");
            foreach (var x in p.GetProperty("hello").EnumerateArray())
            {
                var h = HelloPayload.TryDecode(Hex(x.GetProperty("hex")));
                var v = x.GetProperty("value");
                T.Eq(v.GetProperty("schemaHash").GetString(), h.SchemaHash);
                T.Eq(v.GetProperty("docToken").GetString(), h.DocToken);
                T.Eq(v.GetProperty("href").GetString(), h.Href);
                T.Eq(v.GetProperty("recvWindow").GetUInt32(), h.RecvWindow);
                T.Eq(v.GetProperty("isTop").GetBoolean(), h.IsTop);
                T.Eq(x.GetProperty("hex").GetString(), BridgeHex.ToHex(h.Encode()));
            }
            foreach (var x in p.GetProperty("welcome").EnumerateArray())
            {
                var w = WelcomePayload.TryDecode(Hex(x.GetProperty("hex")));
                T.Eq(x.GetProperty("value").GetProperty("epoch").GetUInt32(), w.Epoch);
                T.Eq(x.GetProperty("hex").GetString(), BridgeHex.ToHex(w.Encode()));
            }
            foreach (var x in p.GetProperty("farewell").EnumerateArray())
            {
                T.True(FarewellPayload.TryDecode(Hex(x.GetProperty("hex")), out var f));
                T.Eq(x.GetProperty("value").GetProperty("docToken").GetString(), f.DocToken);
                T.Eq(x.GetProperty("hex").GetString(), BridgeHex.ToHex(f.Encode()));
            }
            foreach (var x in p.GetProperty("reply").EnumerateArray())
            {
                T.True(ReplyPayload.TryDecode(Hex(x.GetProperty("hex")), out var id, out var st, out var body));
                var v = x.GetProperty("value");
                T.Eq(v.GetProperty("reqId").GetUInt32(), id); T.Eq((byte)v.GetProperty("status").GetInt32(), st);
                T.Eq(v.GetProperty("bodyHex").GetString(), BridgeHex.ToHex(body));
                T.Eq(x.GetProperty("hex").GetString(), BridgeHex.ToHex(ReplyPayload.Encode(id, st, body)));
            }
            foreach (var x in p.GetProperty("close").EnumerateArray())
            {
                T.True(BridgeFrame.TryDecodeClose(Hex(x.GetProperty("hex")), out var c));
                T.Eq(x.GetProperty("value").GetProperty("consumedSeq").GetUInt32(), c.ConsumedSeq);
                T.Eq(x.GetProperty("hex").GetString(), BridgeHex.ToHex(BridgeFrame.EncodeClose(c.Reason, c.ConsumedSeq, c.Text)));
            }
            var bad = V.GetProperty("badPayloads");
            foreach (var x in bad.GetProperty("hello").EnumerateArray()) T.Eq(null, HelloPayload.TryDecode(Hex(x)), "hello " + x.GetString());
            foreach (var x in bad.GetProperty("welcome").EnumerateArray()) T.Eq(null, WelcomePayload.TryDecode(Hex(x)), "welcome " + x.GetString());
            foreach (var x in bad.GetProperty("farewell").EnumerateArray()) T.True(!FarewellPayload.TryDecode(Hex(x), out _), "farewell " + x.GetString());
            foreach (var x in bad.GetProperty("reply").EnumerateArray()) T.True(!ReplyPayload.TryDecode(Hex(x), out _, out _, out _), "reply " + x.GetString());
        });

        T.Run("sender parity: same queue, same carriers as the TS link", () =>
        {
            foreach (var x in V.GetProperty("sequences").EnumerateArray())
            {
                var name = x.GetProperty("name").GetString();
                var link = new BridgeLink(x.GetProperty("fragmentSize").GetInt32(), BridgeWire.DefaultWindow, x.GetProperty("window").GetInt32());
                link.Epoch = x.GetProperty("epoch").GetUInt32();
                link.NextSeq = x.GetProperty("startSeq").GetUInt32();
                link.AckSeq = x.GetProperty("ack").GetUInt32();
                foreach (var e in x.GetProperty("entries").EnumerateArray())
                {
                    if (e.TryGetProperty("text", out var t)) link.EnqueueText(t.GetString());
                    else link.EnqueueOp(Hex(e.GetProperty("opHex")));
                }
                var frames = new List<OutFrame>();
                link.TakeFrames(frames);
                T.Seq(x.GetProperty("carriers").EnumerateArray().Select(z => z.GetString()), frames.Select(f => f.ToCarrier()), name);
                T.Eq(x.GetProperty("leftover").GetInt32(), link.QueuedCount, name + " leftover");
            }
        });

        T.Run("carrier marker is unambiguous against APICommands/UICommands/MessageDelimiters (C# sources)", () =>
        {
            var consts = new List<string>();
            var re = new Regex("public const string \\w+ = \"((?:[^\"\\\\]|\\\\.)*)\";");
            foreach (var file in new[] { "APICommands.cs", "UICommands.cs", "MessageDelimiters.cs" })
                foreach (Match m in re.Matches(File.ReadAllText(Path.Combine(Models, file))))
                    consts.Add(Regex.Unescape(m.Groups[1].Value));
            T.True(consts.Count > 200, "found only " + consts.Count + " constants");
            consts.AddRange(new[] { "\u00B6\u00A7|", "\u203D", "\"", "PanelSettings 0" });
            foreach (var c in consts)
            {
                T.True(c.Length == 0 || c[0] != BridgeCarrier.Mark, "constant starts with U+0002: " + c);
                T.True(!BridgeCarrier.IsCarrier(c + "\u00B6x"), c);
                T.Eq(BridgeError.NotCarrier, BridgeCarrier.TryDecode(c + "\u00B6x", out _), c);
            }
            var sample = BridgeCarrier.EncodeText(new FrameHeader(FrameKind.Data, FrameFlags.Complete, 1, 2, 1, 0), "3:abc");
            foreach (var c in consts) T.True(c.Length == 0 || !sample.StartsWith(c, StringComparison.Ordinal), c);
            // Why the check must be ordinal: culture-sensitive comparison ignores U+0002 entirely.
            T.True(BridgeCarrier.IsCarrier("\u0002!l!x") && !"\u0002!l!x".StartsWith("!l!", StringComparison.Ordinal));
        });
    }
}
