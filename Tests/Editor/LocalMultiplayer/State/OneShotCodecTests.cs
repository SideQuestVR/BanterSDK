// <mirror source="Assets/Tests/Emoji/Editor/EmojiRuntimePureTests.cs" sha256="d1111847223d1c112bf4fc5763dea2276e0d54a492b8deff517631e27308ba8c" mode="verbatim" />
// The OneShotCodecTests class, verbatim; the rest of that file tests emoji code. Extra cases live in OneShotCodecLimitTests.cs.
using System.Text;
using NUnit.Framework;

namespace BS.LocalMultiplayer.Tests
{
    public class OneShotCodecTests
    {
        [Test]
        public void RoundTrips_TopicAndPayload()
        {
            const string payload = "{\"id\":\"1f44b\",\"n\":6}";
            Assert.IsTrue(OneShotCodec.TryEncode("emoji", payload, out var bytes));
            Assert.IsTrue(OneShotCodec.TryDecode(bytes, out var topic, out var data));
            Assert.AreEqual("emoji", topic);
            Assert.AreEqual(payload, data);
        }

        [Test]
        public void OnlyTheFirstNewline_SplitsTopicFromPayload()
        {
            Assert.IsTrue(OneShotCodec.TryEncode("page", "line1\nline2\n", out var bytes));
            Assert.IsTrue(OneShotCodec.TryDecode(bytes, out var topic, out var data));
            Assert.AreEqual("page", topic);
            Assert.AreEqual("line1\nline2\n", data);
        }

        [Test]
        public void EmptyPayload_IsAllowed()
        {
            Assert.IsTrue(OneShotCodec.TryEncode("ping", null, out var bytes));
            Assert.IsTrue(OneShotCodec.TryDecode(bytes, out var topic, out var data));
            Assert.AreEqual("ping", topic);
            Assert.AreEqual(string.Empty, data);
        }

        [Test]
        public void RejectsBadTopics_AndOversizedFrames()
        {
            Assert.IsFalse(OneShotCodec.TryEncode("", "x", out _));
            Assert.IsFalse(OneShotCodec.TryEncode("has space", "x", out _));
            Assert.IsFalse(OneShotCodec.TryEncode("a\nb", "x", out _));
            Assert.IsFalse(OneShotCodec.TryEncode(new string('a', OneShotCodec.MaxTopicLength + 1), "x", out _));
            Assert.IsTrue(OneShotCodec.TryEncode(new string('a', OneShotCodec.MaxTopicLength), "x", out _));
            Assert.IsFalse(OneShotCodec.TryEncode("emoji", new string('x', OneShotCodec.MaxPayloadBytes), out _), "topic + separator push it over");
        }

        [Test]
        public void RejectsMalformedFrames()
        {
            Assert.IsFalse(OneShotCodec.TryDecode(null, out _, out _));
            Assert.IsFalse(OneShotCodec.TryDecode(new byte[0], out _, out _));
            Assert.IsFalse(OneShotCodec.TryDecode(Encoding.UTF8.GetBytes("no-separator"), out _, out _));
            Assert.IsFalse(OneShotCodec.TryDecode(Encoding.UTF8.GetBytes("\npayload"), out _, out _), "empty topic");
            Assert.IsFalse(OneShotCodec.TryDecode(Encoding.UTF8.GetBytes("bad topic\npayload"), out _, out _));
        }
    }
}
