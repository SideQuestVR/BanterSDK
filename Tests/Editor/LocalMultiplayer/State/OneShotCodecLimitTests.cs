using System.Text;
using NUnit.Framework;

namespace BS.LocalMultiplayer.Tests
{
    /// <summary>
    /// The one-shot limits a creator meets: every SDK one-shot travels as <c>"banter\n" + data</c>, the frame is capped
    /// at 4096 UTF-8 bytes, and topics follow <c>char.IsLetterOrDigit</c> plus <c>- _ .</c> up to 32 characters
    /// (RELAY.md 6.4; production OneShotService.cs:34-75).
    /// </summary>
    public class OneShotCodecLimitTests
    {
        const string SdkTopic = "banter";

        [Test]
        public void Constants_MatchProduction()
        {
            Assert.AreEqual(4096, OneShotCodec.MaxPayloadBytes);
            Assert.AreEqual(32, OneShotCodec.MaxTopicLength);
        }

        [Test]
        public void SdkFrame_Allows4089BytesOfData_AndRefuses4090()
        {
            // "banter" + "\n" is 7 bytes of the 4096.
            Assert.IsTrue(OneShotCodec.TryEncode(SdkTopic, new string('x', 4089), out var bytes));
            Assert.AreEqual(4096, bytes.Length);
            Assert.IsFalse(OneShotCodec.TryEncode(SdkTopic, new string('x', 4090), out var refused));
            Assert.IsNull(refused);
        }

        [Test]
        public void Cap_CountsUtf8Bytes_NotCharacters()
        {
            // 'é' is two UTF-8 bytes: 2044 of them is 4088 bytes (fits), 2045 is 4090 (does not).
            Assert.IsTrue(OneShotCodec.TryEncode(SdkTopic, new string('é', 2044), out _));
            Assert.IsFalse(OneShotCodec.TryEncode(SdkTopic, new string('é', 2045), out _));
        }

        [Test]
        public void Decode_AcceptsAFullFrame_AndRefusesOneByteMore()
        {
            var full = Encoding.UTF8.GetBytes(SdkTopic + "\n" + new string('x', 4089));
            Assert.IsTrue(OneShotCodec.TryDecode(full, out var topic, out var data));
            Assert.AreEqual(SdkTopic, topic);
            Assert.AreEqual(4089, data.Length);

            var over = Encoding.UTF8.GetBytes(SdkTopic + "\n" + new string('x', 4090));
            Assert.IsFalse(OneShotCodec.TryDecode(over, out _, out _));
        }

        [TestCase("banter")]
        [TestCase("emoji")]
        [TestCase("a-b_c.d")]
        [TestCase("Round2")]
        [TestCase("bänter")]
        [TestCase("日本")]
        [TestCase("٣")]
        public void Topics_AreUnicodeLettersDigitsAndDashUnderscoreDot(string topic)
        {
            // char.IsLetterOrDigit is Unicode-aware, so letters and digits of any script pass, as on the relay's
            // [\p{L}\p{Nd}_.-].
            Assert.IsTrue(OneShotCodec.IsValidTopic(topic), topic);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("has space")]
        [TestCase("slash/topic")]
        [TestCase("colon:topic")]
        [TestCase("at@topic")]
        [TestCase("bang!")]
        [TestCase("tab\ttopic")]
        [TestCase("line\nbreak")]
        public void Topics_RefuseEverythingElse(string topic)
        {
            Assert.IsFalse(OneShotCodec.IsValidTopic(topic), topic ?? "<null>");
        }

        [Test]
        public void Topics_AreLimitedTo32Characters()
        {
            Assert.IsTrue(OneShotCodec.IsValidTopic(new string('t', 32)));
            Assert.IsFalse(OneShotCodec.IsValidTopic(new string('t', 33)));
        }

        [Test]
        public void Data_KeepsDelimitersAndNewlines()
        {
            // The lane is opaque: only the first newline splits, and the page's own § split is its business.
            const string data = "a§b¶c|d\ne";
            Assert.IsTrue(OneShotCodec.TryEncode(SdkTopic, data, out var bytes));
            Assert.IsTrue(OneShotCodec.TryDecode(bytes, out _, out var back));
            Assert.AreEqual(data, back);
        }
    }
}
