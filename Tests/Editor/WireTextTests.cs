using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using NUnit.Framework;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// WireText and WireFrame keep text that holds the bus delimiters (¶ § | ‽ ¤) intact across the
    /// JS-Unity string bus. The JS mirrors (Injection~/src/game/wire-text.ts, wire-frame.ts) follow
    /// the same rules; these cases are the contract both sides keep.
    /// </summary>
    public class WireTextTests
    {
        const string AllDelimiters = "a¶b§c|d‽e¤f";

        static readonly string[] RoundTripCases =
        {
            "",
            "plain label",
            AllDelimiters,
            "\"quoted\"",
            "\"",
            "back\\slash",
            "line\nbreak\r\nand tab\t",
            "emoji 😀 and ñ",
            "lone \ud800 surrogate",
            "2026-10-08T10:00:00Z",
            "\"2026-10-08T10:00:00Z\"",
            "{\"actions\":[{\"actionType\":\"postmessage\",\"strParam1\":\"x§y¶z\"}]}",
            "x§y¶z|w‽!e!bogus¤\"\\",
            "‽",
            "null",
        };

        [Test]
        public void EncodeThenDecodeGivesTheSameText([ValueSource(nameof(RoundTripCases))] string text)
        {
            Assert.AreEqual(text, WireText.Decode(WireText.Encode(text)));
        }

        [Test]
        public void EncodedTextHoldsNoDelimiter([ValueSource(nameof(RoundTripCases))] string text)
        {
            Assert.IsFalse(WireText.HasDelimiter(WireText.Encode(text)), WireText.Encode(text));
        }

        [Test]
        public void PlainTextTravelsAsTheSameInstance()
        {
            const string label = "Score: 1234 points!!";
            Assert.AreSame(label, WireText.Encode(label));
            Assert.AreSame(label, WireText.Decode(label));
            Assert.AreSame(label, WireText.EscapeDelimiters(label));
        }

        [Test]
        public void TextStartingWithAQuoteIsEncoded()
        {
            // Raw text never starts with a quote: that's how the receiver tells the two apart.
            var encoded = WireText.Encode("\"hi\" she said");
            Assert.AreNotEqual("\"hi\" she said", encoded);
            Assert.AreEqual("\"hi\" she said", WireText.Decode(encoded));
        }

        [Test]
        public void LineBreaksAreEncoded()
        {
            var encoded = WireText.Encode("two\nlines");
            Assert.AreEqual("\"two\\nlines\"", encoded);
        }

        [Test]
        public void DelimitersBecomeUnicodeEscapes()
        {
            Assert.AreEqual("\"a\\u00b6b\\u00a7c\\u007cd\\u203de\\u00a4f\"", WireText.Encode(AllDelimiters));
        }

        [Test]
        public void DecodeAcceptsWhatJsonStringifyWrites()
        {
            // What the JS encodeWireText produces for the same text (JSON.stringify + escapes).
            Assert.AreEqual("x§y\n\"q\"", WireText.Decode("\"x\\u00a7y\\n\\\"q\\\"\""));
            Assert.AreEqual("lone \ud800", WireText.Decode("\"lone \\ud800\""));
            Assert.AreEqual("a/b\u00a7\u0001", WireText.Decode("\"a\\/b\\u00A7\\u0001\""));
        }

        [Test]
        public void EncodeWritesWhatJsonStringifyWrites()
        {
            // JSON.stringify("a\"b\\c\u0001\t") plus the delimiter escapes, character for character.
            Assert.AreEqual("\"a\\\"b\\\\c\\u0001\\t\\u00a7\"", WireText.Encode("a\"b\\c\u0001\t§"));
        }

        [Test]
        public void DecodeLeavesWhatIsNotAnEncodedValue()
        {
            Assert.AreEqual("raw", WireText.Decode("raw"));
            Assert.AreEqual("\"unterminated", WireText.Decode("\"unterminated"));
            Assert.AreEqual("\"a\" trailing", WireText.Decode("\"a\" trailing"));
            Assert.AreEqual("\"a\"\"b\"", WireText.Decode("\"a\"\"b\""));
            Assert.IsNull(WireText.Decode(null));
            Assert.AreEqual("", WireText.Decode(""));
        }

        [Test]
        public void DecodeKeepsDateLookingTextVerbatim()
        {
            // Newtonsoft's default DateParseHandling would hand this back reformatted.
            Assert.AreEqual("2026-10-08T10:00:00Z", WireText.Decode("\"2026-10-08T10:00:00Z\""));
        }

        [Test]
        public void EscapeDelimitersKeepsJsonValid()
        {
            // The test assembly doesn't reference Newtonsoft; the escapes are what JSON.parse reads back.
            Assert.AreEqual("{\"newValue\":\"a\\u00b6b\\u203dc\"}", WireText.EscapeDelimiters("{\"newValue\":\"a¶b‽c\"}"));
        }

        [Test]
        public void EncodeOfNullIsEmpty()
        {
            Assert.AreEqual("", WireText.Encode(null));
        }

        // ---- WireFrame ---------------------------------------------------------------------

        static string Pack(string marker, List<string> messages)
        {
            char[] buffer = null;
            return WireFrame.Pack(marker, messages, ref buffer);
        }

        [Test]
        public void PackReusesAndGrowsTheBuffer()
        {
            var buffer = new char[4];
            var frame = WireFrame.Pack("‽", new List<string> { "0123456789" }, ref buffer);
            Assert.AreEqual("‽10:0123456789", frame);
            Assert.GreaterOrEqual(buffer.Length, frame.Length);

            var grown = buffer;
            Assert.AreEqual("‽1:a", WireFrame.Pack("‽", new List<string> { "a" }, ref buffer));
            Assert.AreSame(grown, buffer, "a big enough buffer is reused");
        }

        [Test]
        public void FrameRoundTripsMessagesHoldingDelimiters()
        {
            var messages = new List<string> { "!e!bm!¶12§x‽!e!bogus", "", "¶§|", "😀", AllDelimiters, "12:34" };
            foreach (var marker in new[] { MessageDelimiters.BATCH, MessageDelimiters.PRIMARY + MessageDelimiters.SECONDARY + MessageDelimiters.TERTIARY })
            {
                var frame = Pack(marker, messages);
                var read = new List<string>();
                Assert.IsTrue(WireFrame.Unpack(frame, marker.Length, read), frame);
                CollectionAssert.AreEqual(messages, read);
            }
        }

        [Test]
        public void FrameMatchesTheDocumentedLayout()
        {
            Assert.AreEqual("‽2:ab0:10:0123456789", Pack("‽", new List<string> { "ab", "", "0123456789" }));
        }

        [Test]
        public void RandomFramesRoundTrip()
        {
            var random = new System.Random(1234);
            const string alphabet = "ab:019¶§|‽¤\"\\\n😀";
            for (int round = 0; round < 200; round++)
            {
                var messages = new List<string>();
                int count = random.Next(0, 20);
                for (int i = 0; i < count; i++)
                {
                    var builder = new StringBuilder();
                    int length = random.Next(0, 40);
                    for (int j = 0; j < length; j++) builder.Append(alphabet[random.Next(alphabet.Length)]);
                    messages.Add(builder.ToString());
                }
                var read = new List<string>();
                Assert.IsTrue(WireFrame.Unpack(Pack("‽", messages), 1, read));
                CollectionAssert.AreEqual(messages, read);
            }
        }

        [Test]
        public void MalformedFramesAreRejectedWithoutThrowing()
        {
            var read = new List<string>();
            Assert.IsFalse(WireFrame.Unpack("‽5:ab", 1, read), "length past the end");
            Assert.IsFalse(WireFrame.Unpack("‽ab", 1, read), "no length");
            Assert.IsFalse(WireFrame.Unpack("‽3ab", 1, read), "no colon");
            Assert.IsFalse(WireFrame.Unpack("‽2", 1, read), "length at the end");
            Assert.IsFalse(WireFrame.Unpack("‽9999999999:x", 1, read), "too many digits");

            read.Clear();
            Assert.IsFalse(WireFrame.Unpack("‽2:ok3:no", 1, read));
            CollectionAssert.AreEqual(new[] { "ok" }, read, "messages before the error are kept");

            read.Clear();
            Assert.IsTrue(WireFrame.Unpack("‽", 1, read), "an empty batch");
            Assert.IsEmpty(read);
        }

        // ---- Cost ---------------------------------------------------------------------------

        [Test]
        public void ReportTimings()
        {
            const int iterations = 1000000;
            const string label = "Score: 1234 points!!";
            const string uiEventJson = "{\"type\":\"change\",\"target\":\"field_12\",\"newValue\":\"Hello there, how are you today?\",\"previousValue\":\"Hello there, how are you today\"}";

            var decode = Time(iterations, () => WireText.Decode(label));
            var encode = Time(iterations, () => WireText.Encode(label));
            var escape = Time(iterations, () => WireText.EscapeDelimiters(uiEventJson));
            var encodeSlow = Time(iterations / 10, () => WireText.Encode(AllDelimiters));
            var decodeSlow = Time(iterations / 10, () => WireText.Decode("\"a\\u00b6b\\u00a7c\\u007cd\\u203de\\u00a4f\""));

            var messages = new List<string>();
            for (int i = 0; i < 200; i++) messages.Add("!e!tu!¶" + (10000 + i) + "§0|" + (i * 1.2345f).ToString("F4") + "|2.3456|3.4567");
            char[] buffer = null;
            var packed = Pack("‽", messages);
            var read = new List<string>(256);
            var joinToday = Time(20000, () => "‽" + string.Join("‽", messages));
            var packPlan = Time(20000, () => WireFrame.Pack("‽", messages, ref buffer));
            var joined = "‽" + string.Join("‽", messages);
            var splitToday = Time(20000, () => { joined.Substring(1).Split(MessageDelimiters.BATCH); return null; });
            var unpackPlan = Time(20000, () => { read.Clear(); WireFrame.Unpack(packed, 1, read); return null; });

            UnityEngine.Debug.Log(
                $"[WireTextTests] ns/op: Decode plain {decode:F1}, Encode plain {encode:F1}, EscapeDelimiters UI json ({uiEventJson.Length} ch) {escape:F1}, " +
                $"Encode with delimiters {encodeSlow:F1}, Decode encoded {decodeSlow:F1}; 200-message batch: join {joinToday:F0} vs pack {packPlan:F0}, " +
                $"split {splitToday:F0} vs unpack {unpackPlan:F0}");
        }

        static double Time(int iterations, System.Func<string> action)
        {
            for (int i = 0; i < 1000; i++) action();
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++) action();
            watch.Stop();
            return watch.Elapsed.TotalMilliseconds * 1000000.0 / iterations;
        }
    }
}
