using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using BS.LocalMultiplayer.State;
using NUnit.Framework;

namespace BS.LocalMultiplayer.Tests
{
    /// <summary>
    /// Properties of the copied StatePathCodec over thousands of generated keys: whatever it emits the relay accepts
    /// (it ports the server's keys.ts grammar), it round-trips, two keys never share a path, and only canonical
    /// escapes decode — the aliasing that would let one protected path lock another key out of the room.
    /// </summary>
    public class StatePathCodecPropertyTests
    {
        static readonly Regex ServerSegment = new Regex(@"^[A-Za-z0-9_\-:@]{1,64}$");

        // Ordinary characters, every delimiter the SDK bus uses, multi-byte letters and an astral emoji (a surrogate
        // pair). Either half of a pair on its own is mixed in rarely, so most keys still encode.
        static readonly string[] Alphabet =
        {
            "a", "Z", "0", "9", "_", "-", "@", ":", ".", " ", "/", "#", "?", "é", "ß", "日", "Ω",
            "¶", "§", "|", "‽", "¤", " ", "🎉", "b", "c", "x", "1"
        };

        static IEnumerable<string> Keys(int count, int seed)
        {
            var random = new Random(seed);
            for (int i = 0; i < count; i++)
            {
                int length = random.Next(0, 40);
                var builder = new StringBuilder();
                for (int j = 0; j < length; j++)
                {
                    double roll = random.NextDouble();
                    if (roll < 0.01) builder.Append('\uD800');
                    else if (roll < 0.02) builder.Append('\uDC00');
                    else builder.Append(Alphabet[random.Next(Alphabet.Length)]);
                }
                yield return builder.ToString();
            }
        }

        [Test]
        public void EveryEncodedPath_MeetsTheServerGrammar_AndRoundTrips()
        {
            int encoded = 0;
            foreach (var key in Keys(4000, 20261001))
            {
                if (!StatePathCodec.TryEncodePath(key, out var path, out var reason))
                {
                    Assert.IsFalse(string.IsNullOrEmpty(reason), $"a refusal explains itself: '{key}'");
                    continue;
                }
                encoded++;
                Assert.LessOrEqual(path.Length, StatePathCodec.MaxPathChars, path);
                var segments = path.Split('.');
                Assert.LessOrEqual(segments.Length, StatePathCodec.MaxSegments, path);
                foreach (var segment in segments)
                {
                    Assert.IsTrue(ServerSegment.IsMatch(segment), $"segment '{segment}' of '{path}' (from '{key}')");
                }
                Assert.IsTrue(StatePathCodec.TryDecodePath(path, out var back), path);
                Assert.AreEqual(key, back);
            }
            Assert.Greater(encoded, 1000, "the generator must exercise the encoder, not just its refusals");
        }

        [Test]
        public void DistinctKeys_NeverShareAPath()
        {
            var owners = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var key in Keys(4000, 7))
            {
                if (!StatePathCodec.TryEncodePath(key, out var path, out _)) continue;
                if (owners.TryGetValue(path, out var other))
                {
                    Assert.AreEqual(other, key, $"'{other}' and '{key}' both encode to '{path}'");
                }
                owners[path] = key;
            }
        }

        [Test]
        public void LoneSurrogates_AreRefused()
        {
            // Built in code: an attribute argument is stored as UTF-8, which would already have folded them.
            var keys = new[]
            {
                new string('\uD800', 1),
                new string('\uDC00', 1),
                "a" + '\uD800' + "b",
                new string(new[] { '\uDC00', '\uD800' })
            };
            foreach (var key in keys)
            {
                // UTF-8 folds them onto U+FFFD, so two different keys would alias one path.
                Assert.IsFalse(StatePathCodec.TryEncodeSegment(key, out _, out var reason), Escape(key));
                StringAssert.Contains("surrogate", reason);
            }
        }

        static string Escape(string text)
        {
            var builder = new StringBuilder();
            foreach (char c in text) builder.Append(c < 128 ? c.ToString() : $"\\u{(int)c:X4}");
            return builder.ToString();
        }

        [Test]
        public void ASurrogatePair_IsFine()
        {
            Assert.IsTrue(StatePathCodec.TryEncodeSegment("🎉", out var segment, out _));
            Assert.IsTrue(StatePathCodec.TryDecodeSegment(segment, out var back));
            Assert.AreEqual("🎉", back);
        }

        [Test]
        public void OnlyCanonicalEscapes_Decode()
        {
            // Any escaped segment that decodes must be exactly what encoding its key produces; otherwise several
            // server paths would alias one SDK key.
            const string base64Url = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
            var random = new Random(42);
            int decoded = 0;
            for (int i = 0; i < 20000; i++)
            {
                int length = random.Next(1, 64);
                var builder = new StringBuilder(":");
                for (int j = 0; j < length; j++) builder.Append(base64Url[random.Next(base64Url.Length)]);
                var segment = builder.ToString();
                if (!StatePathCodec.TryDecodeSegment(segment, out var key)) continue;
                decoded++;
                Assert.IsTrue(StatePathCodec.TryEncodeSegment(key, out var canonical, out _), segment);
                Assert.AreEqual(segment, canonical);
            }
            Assert.Greater(decoded, 0);
        }
    }
}
