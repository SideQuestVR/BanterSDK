using System.Diagnostics;
using NUnit.Framework;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// WireText keeps text that holds the bus delimiters (¶ § | ‽ ¤) intact across the JS-Unity
    /// string bus. The JS mirror (Injection~/src/game/wire-text.ts) follows the same rules; these
    /// cases are the contract both sides keep.
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

            UnityEngine.Debug.Log(
                $"[WireTextTests] ns/op: Decode plain {decode:F1}, Encode plain {encode:F1}, EscapeDelimiters UI json ({uiEventJson.Length} ch) {escape:F1}, " +
                $"Encode with delimiters {encodeSlow:F1}, Decode encoded {decodeSlow:F1}");
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
