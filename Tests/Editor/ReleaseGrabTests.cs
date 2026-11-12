using NUnit.Framework;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// scene.ReleaseGrab / obj.ReleaseGrab on the Unity side: the request's payload, "side¶unityId", and the hook that
    /// does the letting go, which holds nothing until the client or the SDK's desktop player installs one.
    /// </summary>
    public class ReleaseGrabTests
    {
        const string P = MessageDelimiters.PRIMARY;

        [Test]
        public void TheCommand_IsItsOwn()
        {
            Assert.AreEqual("!rlg!", APICommands.RELEASE_GRAB);
            foreach (var field in typeof(APICommands).GetFields())
            {
                if (field.Name == nameof(APICommands.RELEASE_GRAB) || field.FieldType != typeof(string))
                    continue;
                var other = (string)field.GetValue(null);
                // BSLink routes by StartsWith, so neither may be a prefix of the other.
                Assert.IsFalse(other.Length > 0 && (other.StartsWith(APICommands.RELEASE_GRAB) || APICommands.RELEASE_GRAB.StartsWith(other)),
                    $"{field.Name} ({other}) clashes with RELEASE_GRAB");
            }
        }

        [TestCase("-1" + P, -1, null)]
        [TestCase("0" + P, 0, null)]
        [TestCase("1" + P, 1, null)]
        [TestCase("1", 1, null)]
        [TestCase("0" + P + "12345", 0, 12345)]
        [TestCase("-1" + P + "-4242", -1, -4242)]
        [TestCase("7" + P + "5", -1, 5)] // not a hand: both
        public void Parse_SideAndObject(string payload, int side, int? objectId)
        {
            Assert.IsTrue(BSScene.TryParseReleaseGrab(payload, out var parsedSide, out var parsedId));
            Assert.AreEqual(side, parsedSide);
            Assert.AreEqual(objectId, parsedId);
        }

        [TestCase("")]
        [TestCase(null)]
        [TestCase("left" + P)]
        [TestCase("1" + P + "notAnId")]
        public void Parse_Malformed(string payload)
        {
            Assert.IsFalse(BSScene.TryParseReleaseGrab(payload, out _, out _));
        }

        [Test]
        public void TheDefaultHook_ReleasesNothing()
        {
            var data = new DataBridge();
            Assert.IsFalse(data.ReleaseGrab(null, -1));
            Assert.IsFalse(data.ReleaseGrab(null, (int)HandSide.LEFT));
        }
    }
}
