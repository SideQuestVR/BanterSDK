// <mirror source="Assets/Tests/SpaceState/Editor/SdkWireCodecTests.cs" sha256="c26d79295d8c1e1bba694ba17eeb8096e164c96667180d5a9f79dba203908629" mode="verbatim" />
using BS.LocalMultiplayer.State;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace BS.LocalMultiplayer.Tests
{
    public class SdkWireCodecTests
    {
        // --- Render: the legacy compatibility guarantee ------------------------------------

        [Test]
        public void StringsRenderRawWithNoQuotes()
        {
            // This exact behaviour is what pages have always seen. Quoting a string here would
            // silently break every existing space that reads spaceState.public[k].
            Assert.That(SdkWireCodec.Render(JValue.CreateString("hello")), Is.EqualTo("hello"));
            Assert.That(SdkWireCodec.Render(JValue.CreateString("")), Is.EqualTo(""));
            Assert.That(SdkWireCodec.Render(JValue.CreateString("{\"a\":1}")), Is.EqualTo("{\"a\":1}"));
        }

        [Test]
        public void NullAndMissingRenderAsEmptyString()
        {
            Assert.That(SdkWireCodec.Render(null), Is.EqualTo(""));
            Assert.That(SdkWireCodec.Render(JValue.CreateNull()), Is.EqualTo(""));
        }

        [Test]
        public void NonStringLeavesRenderAsCompactJsonSoAPageCanParseThem()
        {
            Assert.That(SdkWireCodec.Render(new JValue(42)), Is.EqualTo("42"));
            Assert.That(SdkWireCodec.Render(new JValue(1.5)), Is.EqualTo("1.5"));
            Assert.That(SdkWireCodec.Render(new JValue(true)), Is.EqualTo("true"));
            Assert.That(SdkWireCodec.Render(JArray.Parse("[1,2]")), Is.EqualTo("[1,2]"));
            Assert.That(SdkWireCodec.Render(JObject.Parse("{\"a\":1}")), Is.EqualTo("{\"a\":1}"));
        }

        // --- Sanitisation: outbound legacy only --------------------------------------------

        [Test]
        public void SanStripsEveryBusDelimiterButLeavesColonAlone()
        {
            Assert.That(SdkWireCodec.San("a¶b§c|d‽e¤f"), Is.EqualTo("abcdef"));
            // ':' is the path-escape marker and is legal mid-message; stripping it would corrupt
            // escaped keys echoed back to the page.
            Assert.That(SdkWireCodec.San("a:b"), Is.EqualTo("a:b"));
        }

        [Test]
        public void SanLeavesCleanValuesIdentical()
        {
            Assert.That(SdkWireCodec.San("plain value"), Is.EqualTo("plain value"));
            Assert.That(SdkWireCodec.San(""), Is.EqualTo(""));
            Assert.That(SdkWireCodec.San(null), Is.EqualTo(""));
        }

        // --- base64 envelope: the new API's delimiter proof --------------------------------

        [Test]
        public void Base64EnvelopeRoundTripsValuesContainingEveryDelimiter()
        {
            // The whole point of base64: this value would destroy a legacy frame, and must survive
            // the new API untouched.
            var value = JObject.Parse("{\"note\":\"a|b§c¶d‽e¤f:g\"}");
            string encoded = SdkWireCodec.ToBase64Json(value);

            foreach (char delimiter in new[] { '¶', '§', '|', '‽', '¤' })
            {
                Assert.That(encoded.IndexOf(delimiter), Is.EqualTo(-1),
                    $"base64 must never emit '{delimiter}'");
            }

            var back = SdkWireCodec.FromBase64Json(encoded);
            Assert.That(JToken.DeepEquals(back, value), Is.True, "the value must survive verbatim");
        }

        [Test]
        public void Base64EnvelopeRoundTripsEveryJsonShape()
        {
            foreach (string json in new[] { "\"s\"", "42", "true", "null", "[1,2,3]", "{\"a\":{\"b\":[1]}}" })
            {
                var token = JToken.Parse(json);
                var back = SdkWireCodec.FromBase64Json(SdkWireCodec.ToBase64Json(token));
                Assert.That(JToken.DeepEquals(back, token), Is.True, $"round trip failed for {json}");
            }
        }

        [Test]
        public void MalformedBase64DecodesToNullRatherThanThrowing()
        {
            // A page can send anything; a bad payload must fail as a refusal, not an exception
            // that takes down the message pump.
            Assert.That(SdkWireCodec.FromBase64Json("!!!not base64!!!"), Is.Null);
            Assert.That(SdkWireCodec.FromBase64Json(""), Is.Null);
            Assert.That(SdkWireCodec.FromBase64Json(null), Is.Null);
        }

        // --- Size limit ---------------------------------------------------------------------

        [Test]
        public void ValueLimitIsMeasuredInUtf8BytesNotChars()
        {
            // The old bridge warned at 32K *chars*; the server's cap is 16 KiB of UTF-8 measured on
            // the encoded form, so a multi-byte value hits it far sooner than a char count implies.
            var multiByte = JValue.CreateString(new string('é', 9000)); // 18000 UTF-8 bytes
            Assert.That(SdkWireCodec.ExceedsValueLimit(multiByte, out int bytes), Is.True);
            Assert.That(bytes, Is.GreaterThan(SdkWireCodec.MaxValueBytes));

            var small = JValue.CreateString(new string('a', 100));
            Assert.That(SdkWireCodec.ExceedsValueLimit(small, out _), Is.False);
        }

        // --- Sentinels ----------------------------------------------------------------------

        [Test]
        public void SentinelNamesAreRecognisedSoTheyNeverReachTheServer()
        {
            Assert.That(SdkWireCodec.IsSentinel(SdkWireCodec.RevisionKey), Is.True);
            Assert.That(SdkWireCodec.IsSentinel(SdkWireCodec.ProtectedKeysKey), Is.True);
            Assert.That(SdkWireCodec.IsSentinel(SdkWireCodec.CanProtectKey), Is.True);
            Assert.That(SdkWireCodec.IsSentinel(SdkWireCodec.ErrorKey), Is.True);
            Assert.That(SdkWireCodec.IsSentinel("score"), Is.False);
            Assert.That(SdkWireCodec.IsSentinel(null), Is.False);
        }
    }
}
