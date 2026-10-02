using BS.LocalMultiplayer.Overlay;
using NUnit.Framework;

namespace BS.LocalMultiplayer.Tests
{
    /// <summary>The settings' overlay key: Input System key names, any case, nothing else.</summary>
    public class OverlayKeysTests
    {
        [Test]
        public void TheDefault_IsAKey()
        {
            Assert.AreEqual("F8", OverlayKeys.DefaultName);
            Assert.IsTrue(OverlayKeys.IsValid(OverlayKeys.DefaultName));
        }

        [TestCase("F8")]
        [TestCase("f8")]
        [TestCase(" F9 ")]
        [TestCase("Backquote")]
        [TestCase("Digit5")]
        [TestCase("Space")]
        [TestCase("Numpad0")]
        public void KeyNames_AreValid(string name)
        {
            Assert.IsTrue(OverlayKeys.IsValid(name));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("None")]
        [TestCase("IMESelected")]
        [TestCase("5")]
        [TestCase("74")]
        [TestCase("F99")]
        [TestCase("Ctrl+F8")]
        public void AnythingElse_IsRefused(string name)
        {
            Assert.IsFalse(OverlayKeys.IsValid(name));
            Assert.IsNull(OverlayKeys.Canonical(name));
        }

        [TestCase("f8", "F8")]
        [TestCase("BACKQUOTE", "Backquote")]
        [TestCase(" digit5 ", "Digit5")]
        public void Canonical_IsTheEnumsSpelling(string name, string expected)
        {
            Assert.AreEqual(expected, OverlayKeys.Canonical(name));
        }
    }
}
