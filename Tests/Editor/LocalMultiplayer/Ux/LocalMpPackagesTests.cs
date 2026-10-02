using BS.LocalMultiplayer.Editor;
using NUnit.Framework;

namespace BS.LocalMultiplayer.Tests
{
    /// <summary>
    /// The window's package checks: which Multiplayer Play Mode runs (2.x is the engine switch; 1.x leaves it
    /// off), when it is core (Unity 6000.6), and Ora's minimum version for the relay.
    /// </summary>
    public class LocalMpPackagesTests
    {
        [Test]
        public void PinnedNames_AreTheOnesTheEditorUses()
        {
            Assert.AreEqual("com.unity.multiplayer.playmode", LocalMpPackages.MppmName);
            Assert.AreEqual("2.0.2", LocalMpPackages.MppmPinnedVersion);
            Assert.AreEqual("Window/Multiplayer/Multiplayer Play Mode", LocalMpPackages.MppmWindowMenu);
            Assert.AreEqual("com.sidequest.ora", LocalMpPackages.OraName);
            Assert.AreEqual("0.1.0", LocalMpPackages.OraMinimumVersion);
        }

        [TestCase("2.0.2", 2, 0, 2)]
        [TestCase("0.1.0-pre.1", 0, 1, 0)]
        [TestCase("1.0.0+build.5", 1, 0, 0)]
        [TestCase(" 3.0.0 ", 3, 0, 0)]
        public void Versions_ParseWithoutTheirSuffix(string text, int major, int minor, int build)
        {
            Assert.IsTrue(LocalMpPackages.TryParseVersion(text, out var version));
            Assert.AreEqual(major, version.Major);
            Assert.AreEqual(minor, version.Minor);
            Assert.AreEqual(build, version.Build);
        }

        [Test]
        public void AMajorVersionAlone_Parses()
        {
            Assert.IsTrue(LocalMpPackages.TryParseVersion("2", out var version));
            Assert.AreEqual(2, version.Major);
            Assert.AreEqual(0, version.Minor);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("abc")]
        [TestCase("1.x")]
        public void NonVersions_DoNotParse(string text)
        {
            Assert.IsFalse(LocalMpPackages.TryParseVersion(text, out _));
        }

        [TestCase("0.1.0", "0.1.0", true)]
        [TestCase("0.1", "0.1.0", true)]
        [TestCase("0.2.0", "0.1.0", true)]
        [TestCase("1.0.0-preview", "0.1.0", true)]
        [TestCase("0.0.2", "0.1.0", false)]
        [TestCase("0.0.1", "0.1.0", false)]
        [TestCase(null, "0.1.0", false)]
        public void MeetsMinimum_ComparesVersions(string version, string minimum, bool expected)
        {
            Assert.AreEqual(expected, LocalMpPackages.MeetsMinimum(version, minimum));
        }

        [TestCase("2.0.2", true)]
        [TestCase("2.0.0", true)]
        [TestCase("3.0.0", true)]
        [TestCase("1.6.3", false)]
        [TestCase(null, false)]
        public void OnlyMultiplayerPlayMode2AndLater_Runs(string version, bool usable)
        {
            Assert.AreEqual(usable, LocalMpPackages.IsUsableMppm(version));
        }

        [TestCase("6000.3.21f1", false)]
        [TestCase("6000.5.0a1", false)]
        [TestCase("6000.6.0b2", true)]
        [TestCase("6001.0.0f1", true)]
        [TestCase("2022.3.62f1", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        [TestCase("garbage", false)]
        public void MultiplayerPlayMode_IsCoreFrom6000_6(string unityVersion, bool core)
        {
            Assert.AreEqual(core, LocalMpPackages.MppmIsCore(unityVersion));
        }
    }
}
