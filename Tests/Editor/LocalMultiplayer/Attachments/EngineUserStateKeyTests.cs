using BS.LocalMultiplayer.Attachments;
using NUnit.Framework;

namespace BS.LocalMultiplayer.Tests
{
    /// <summary>
    /// Whether an "attachment_&lt;Id&gt;" key passes the room's path grammar (scrombus state/keys.ts validatePath):
    /// the warning raised before a broadcast the room would refuse.
    /// </summary>
    public class EngineUserStateKeyTests
    {
        [TestCase("attachment_-25810")]                          // instance-id style
        [TestCase("attachment_kQx3vM0aRmS8oW1cZ2dXyA")]          // BSObjectId.ForceGenerateId (base64url)
        [TestCase("attachment_Az09_-:@")]
        [TestCase("pilot")]
        public void PlainKeys_AreValid(string key)
        {
            Assert.AreEqual(EngineUserStateKey.Shape.Valid, EngineUserStateKey.Classify(key));
        }

        [TestCase("attachment_hat.v2")]
        [TestCase("attachment_a.b.c")]
        public void DottedKeys_AreNested(string key)
        {
            Assert.AreEqual(EngineUserStateKey.Shape.Nested, EngineUserStateKey.Classify(key));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("attachment_my hat")]
        [TestCase("attachment_a/b")]
        [TestCase("attachment_café")]
        [TestCase("attachment_a+b")]
        [TestCase("attachment_a..b")]
        [TestCase("attachment_a.")]
        [TestCase(".attachment_a")]
        public void OtherKeys_AreRefused(string key)
        {
            Assert.AreEqual(EngineUserStateKey.Shape.Invalid, EngineUserStateKey.Classify(key));
        }

        [Test]
        public void Segments_AreAtMost64Characters()
        {
            // "attachment_" is 11 characters, so an Id may have 53.
            Assert.AreEqual(EngineUserStateKey.Shape.Valid, EngineUserStateKey.Classify("attachment_" + new string('a', 53)));
            Assert.AreEqual(EngineUserStateKey.Shape.Invalid, EngineUserStateKey.Classify("attachment_" + new string('a', 54)));
        }

        [Test]
        public void Paths_AreAtMost256Characters_And64Segments()
        {
            var segments = new string[64];
            for (var i = 0; i < segments.Length; i++) segments[i] = "ab";
            var sixtyFour = string.Join(".", segments);
            Assert.AreEqual(EngineUserStateKey.Shape.Nested, EngineUserStateKey.Classify(sixtyFour));
            Assert.AreEqual(EngineUserStateKey.Shape.Invalid, EngineUserStateKey.Classify(sixtyFour + ".d"));

            var longPath = new string('a', 60) + "." + new string('b', 60) + "." + new string('c', 60) + "." + new string('d', 60);
            Assert.AreEqual(243, longPath.Length);
            Assert.AreEqual(EngineUserStateKey.Shape.Nested, EngineUserStateKey.Classify(longPath + "." + new string('e', 12)));
            Assert.AreEqual(EngineUserStateKey.Shape.Invalid, EngineUserStateKey.Classify(longPath + "." + new string('e', 13)));
        }
    }
}
