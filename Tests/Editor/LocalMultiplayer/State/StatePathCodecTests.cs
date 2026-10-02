// <mirror source="Assets/Tests/SpaceState/Editor/StatePathCodecTests.cs" sha256="369cd2c45c2a400695bf946ae5ead8826d9e63cb6f1d9ae642b10a7a6dbaab3c" mode="verbatim" />
using System.Text;
using System.Text.RegularExpressions;
using BS.LocalMultiplayer.State;
using NUnit.Framework;

namespace BS.LocalMultiplayer.Tests
{
    /// <summary>
    /// The codec's contract is round-trip fidelity AND server-grammar conformance: a path we
    /// produce must always be one the server accepts, or the write dies as `invalid_path` with the
    /// author none the wiser.
    /// </summary>
    public class StatePathCodecTests
    {
        /// <summary>The server's own rule, from packetparty-server/src/state/keys.ts.</summary>
        private static readonly Regex ServerSegment = new Regex(@"^[A-Za-z0-9_\-:@]{1,64}$");

        private static void AssertServerWouldAccept(string path)
        {
            Assert.That(path.Length, Is.LessThanOrEqualTo(StatePathCodec.MaxPathChars), $"path too long: {path}");
            string[] segments = path.Split('.');
            Assert.That(segments.Length, Is.LessThanOrEqualTo(StatePathCodec.MaxSegments), $"too many segments: {path}");
            foreach (string segment in segments)
            {
                Assert.That(ServerSegment.IsMatch(segment), Is.True, $"segment '{segment}' violates the server grammar (from '{path}')");
            }
        }

        [TestCase("videoUrl")]
        [TestCase("playing")]
        [TestCase("round-2")]
        [TestCase("player_1_score")]
        [TestCase("a_b-c@d")]
        [TestCase("A")]
        [TestCase("0")]
        // Underscores are in the server's segment alphabet, so this is an ordinary key and must NOT
        // be escaped. It is safe verbatim: the server flattens with null-prototype accumulators and
        // Object.defineProperty, and the page's tree builder refuses the name explicitly — the
        // prototype-pollution defence lives there, not in this codec.
        [TestCase("__proto__")]
        public void VerbatimKeysPassThroughUnchanged(string key)
        {
            Assert.That(StatePathCodec.TryEncodePath(key, out string path, out string reason), Is.True, reason);
            Assert.That(path, Is.EqualTo(key), "a legal key must stay byte-identical on the wire");
            AssertServerWouldAccept(path);
            Assert.That(StatePathCodec.TryDecodePath(path, out string back), Is.True);
            Assert.That(back, Is.EqualTo(key));
        }

        [TestCase("my key")]
        [TestCase("a b c")]
        [TestCase("héllo")]
        [TestCase("emoji🎉key")]
        [TestCase("slash/key")]
        [TestCase("hash#key")]
        [TestCase("q?key")]
        [TestCase("colon:key")]     // ':' is the escape marker, so it must NOT pass through verbatim
        [TestCase(" leading")]
        [TestCase("trailing ")]
        public void IllegalKeysEscapeAndRoundTrip(string key)
        {
            Assert.That(StatePathCodec.TryEncodePath(key, out string path, out string reason), Is.True, reason);
            AssertServerWouldAccept(path);
            Assert.That(path.StartsWith(":"), Is.True, $"'{key}' should have been escaped, got '{path}'");
            Assert.That(StatePathCodec.TryDecodePath(path, out string back), Is.True);
            Assert.That(back, Is.EqualTo(key));
        }

        [Test]
        public void ColonKeysCannotCollideWithEscapedKeys()
        {
            // If ':' passed through verbatim, a literal ":x" key and the escaping of some other key
            // could decode to the same thing. Excluding ':' from the verbatim set is what prevents it.
            Assert.That(StatePathCodec.TryEncodeSegment(":x", out string escaped, out _), Is.True);
            Assert.That(escaped, Is.Not.EqualTo(":x"));
            Assert.That(StatePathCodec.TryDecodeSegment(escaped, out string back), Is.True);
            Assert.That(back, Is.EqualTo(":x"));
        }

        [Test]
        public void DotsAreSeparatorsOnBothSurfaces()
        {
            Assert.That(StatePathCodec.TryEncodePath("video.url", out string path, out _), Is.True);
            Assert.That(path, Is.EqualTo("video.url"), "a dotted key must map to a dotted path, not an escaped blob");
            AssertServerWouldAccept(path);
            Assert.That(StatePathCodec.TryDecodePath(path, out string back), Is.True);
            Assert.That(back, Is.EqualTo("video.url"));
        }

        [Test]
        public void MixedSegmentsEscapeOnlyThePartsThatNeedIt()
        {
            Assert.That(StatePathCodec.TryEncodePath("a.b c", out string path, out _), Is.True);
            Assert.That(path.StartsWith("a."), Is.True, $"the legal first segment should survive verbatim: {path}");
            AssertServerWouldAccept(path);
            Assert.That(StatePathCodec.TryDecodePath(path, out string back), Is.True);
            Assert.That(back, Is.EqualTo("a.b c"));
        }

        [Test]
        public void SixtyFourCharVerbatimKeyIsAcceptedAndSixtyFiveIsNot()
        {
            string ok = new string('a', 64);
            Assert.That(StatePathCodec.TryEncodePath(ok, out string path, out _), Is.True);
            AssertServerWouldAccept(path);

            string tooLong = new string('a', 65);
            Assert.That(StatePathCodec.TryEncodePath(tooLong, out _, out string reason), Is.False);
            Assert.That(reason, Does.Contain("65"), "the refusal must name the actual length");
        }

        [Test]
        public void EscapedKeyIsAcceptedAtTheByteLimitAndRefusedPastIt()
        {
            string ok = new string('a', StatePathCodec.MaxEscapedKeyBytes - 1) + " ";
            Assert.That(Encoding.UTF8.GetByteCount(ok), Is.EqualTo(StatePathCodec.MaxEscapedKeyBytes));
            Assert.That(StatePathCodec.TryEncodePath(ok, out string path, out string reason), Is.True, reason);
            AssertServerWouldAccept(path);

            string tooLong = new string('a', StatePathCodec.MaxEscapedKeyBytes) + " ";
            Assert.That(StatePathCodec.TryEncodePath(tooLong, out _, out _), Is.False);
        }

        [Test]
        public void EmptyAndEmptySegmentedKeysAreRefused()
        {
            Assert.That(StatePathCodec.TryEncodePath("", out _, out _), Is.False);
            Assert.That(StatePathCodec.TryEncodePath(null, out _, out _), Is.False);
            // "a..b", "a." and ".a" all produce an empty segment, which the server rejects.
            Assert.That(StatePathCodec.TryEncodePath("a..b", out _, out _), Is.False);
            Assert.That(StatePathCodec.TryEncodePath("a.", out _, out _), Is.False);
            Assert.That(StatePathCodec.TryEncodePath(".a", out _, out _), Is.False);
        }

        [Test]
        public void SixtyFourSegmentsIsAcceptedAndSixtyFiveIsNot()
        {
            var ok = new string[64];
            for (int i = 0; i < ok.Length; i++) ok[i] = "a";
            Assert.That(StatePathCodec.TryEncodePath(string.Join(".", ok), out string path, out string okReason), Is.True, okReason);
            AssertServerWouldAccept(path);

            var tooMany = new string[65];
            for (int i = 0; i < tooMany.Length; i++) tooMany[i] = "a";
            Assert.That(StatePathCodec.TryEncodePath(string.Join(".", tooMany), out _, out string reason), Is.False);
            Assert.That(reason, Does.Contain("65"));
        }

        [Test]
        public void OverlongPathIsRefusedEvenWhenEverySegmentIsLegal()
        {
            var parts = new string[8];
            for (int i = 0; i < parts.Length; i++) parts[i] = new string('a', 60);
            // 8 * 60 + 7 dots = 487 > 256
            Assert.That(StatePathCodec.TryEncodePath(string.Join(".", parts), out _, out string reason), Is.False);
            Assert.That(reason, Does.Contain("256"));
        }

        [Test]
        public void DecodingRejectsPathsWeCouldNotHaveWritten()
        {
            // This is how the bridge ignores room state belonging to other systems rather than
            // surfacing garbage to the page.
            Assert.That(StatePathCodec.TryDecodeSegment(":!!!not-base64!!!", out _), Is.False);
        }

        [Test]
        public void ProtectAndSetAgreeOnTheEncodedForm()
        {
            // A protected write batches protect(path) + set(path); if the two disagreed the value
            // would land unprotected. Encoding must therefore be deterministic.
            Assert.That(StatePathCodec.TryEncodePath("my key", out string first, out _), Is.True);
            Assert.That(StatePathCodec.TryEncodePath("my key", out string second, out _), Is.True);
            Assert.That(first, Is.EqualTo(second));
        }

        [Test]
        public void EncodingIsNotIdempotentButIsUnambiguous()
        {
            // Encoding an already-encoded path would double-escape; the bridge must encode exactly
            // once. This test pins the asymmetry so nobody "helpfully" makes encode() idempotent.
            Assert.That(StatePathCodec.TryEncodeSegment("my key", out string once, out _), Is.True);
            Assert.That(StatePathCodec.TryEncodeSegment(once, out string twice, out _), Is.True);
            Assert.That(twice, Is.Not.EqualTo(once));
            Assert.That(StatePathCodec.TryDecodeSegment(twice, out string back), Is.True);
            Assert.That(back, Is.EqualTo(once));
        }
    }
}
