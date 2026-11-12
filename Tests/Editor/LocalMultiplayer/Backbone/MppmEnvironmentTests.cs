using System.IO;
using NUnit.Framework;

namespace BS.LocalMultiplayer.Tests
{
    /// <summary>
    /// Which Multiplayer Play Mode player an editor is, from its command line and data path. MPPM launches a
    /// clone as a full editor in &lt;Project&gt;/Library/VP/mppmXXXXXXXX with --virtual-project-clone,
    /// -vpId=, -name "Player N" and -mainProcessId=; the main editor has none of them.
    /// </summary>
    public class MppmEnvironmentTests
    {
        const string Project = "C:/workspace/TestSDK";

        static string Full(string path) => Path.GetFullPath(path).TrimEnd('\\', '/');

        [Test]
        public void MainEditor_IsSlotOne_AndItsOwnMain()
        {
            var env = MppmEnvironment.Parse(new[] { "Unity.exe", "-projectPath", Project }, Project + "/Assets", 1234);

            Assert.IsFalse(env.IsClone);
            Assert.AreEqual("Main Editor", env.Slot);
            Assert.AreEqual(1, env.SlotIndex);
            Assert.AreEqual("", env.VpId);
            Assert.AreEqual(1234, env.MainProcessId);
            Assert.AreEqual(Full(Project), env.MainProjectRoot);
        }

        [Test]
        public void NamedClone_TakesItsSlot_VpId_AndTheMainEditorsPid()
        {
            var args = new[]
            {
                "Unity.exe", "--virtual-project-clone", "-library-redirect", "../..", "-mainProcessId=99",
                "-vpId=mppm1a2b3c4d", "-name", "Player 3",
            };
            var env = MppmEnvironment.Parse(args, Project + "/Library/VP/mppm1a2b3c4d/Assets", 555);

            Assert.IsTrue(env.IsClone);
            Assert.AreEqual("Player 3", env.Slot);
            Assert.AreEqual(3, env.SlotIndex);
            Assert.AreEqual("mppm1a2b3c4d", env.VpId);
            Assert.AreEqual(99, env.MainProcessId);
            Assert.AreEqual(Full(Project), env.MainProjectRoot);
        }

        [Test]
        public void QuotedName_IsUnquoted()
        {
            var env = MppmEnvironment.Parse(new[] { "--virtual-project-clone", "-name", "\"Player 2\"" },
                Project + "/Library/VP/mppmdeadbeef/Assets", 7);

            Assert.AreEqual("Player 2", env.Slot);
            Assert.AreEqual(2, env.SlotIndex);
        }

        [Test]
        public void ScenarioCloneWithoutName_IsNamedAfterItsVpId()
        {
            var env = MppmEnvironment.Parse(new[] { "-scenarioClone", "-vpId=mppmdeadbeef" }, Project + "/Library/VP/mppmdeadbeef/Assets", 42);

            Assert.IsTrue(env.IsClone);
            Assert.AreEqual("vp-mppmdeadbeef", env.Slot);
            Assert.AreEqual(0, env.SlotIndex);
            // No -mainProcessId: its own pid is the best guess.
            Assert.AreEqual(42, env.MainProcessId);
            Assert.AreEqual("local:vp-mppmdeadbeef", LocalMultiplayerSettings.ClientIdFor(env.Slot));
        }

        [Test]
        public void ScenarioCloneWithNothingElse_IsStillAClone()
        {
            var env = MppmEnvironment.Parse(new[] { "-scenarioClone" }, "C:/Elsewhere/Assets", 42);

            Assert.IsTrue(env.IsClone);
            Assert.AreEqual("Player ?", env.Slot);
            Assert.AreEqual(0, env.SlotIndex);
        }

        [Test]
        public void VirtualProjectDataPath_AloneMakesAClone()
        {
            var env = MppmEnvironment.Parse(new string[0], Project + "/Library/VP/mppm0badf00d/Assets", 8);

            Assert.IsTrue(env.IsClone);
            Assert.AreEqual("Player ?", env.Slot);
            Assert.AreEqual(Full(Project), env.MainProjectRoot);
        }

        [Test]
        public void NullArguments_AreTheMainEditor()
        {
            var env = MppmEnvironment.Parse(null, Project + "/Assets", 3);

            Assert.IsFalse(env.IsClone);
            Assert.AreEqual("Main Editor", env.Slot);
        }

        [Test]
        public void MainProjectRoot_IsTheSameFromTheMainEditorAndEveryClone()
        {
            var main = MppmEnvironment.Parse(new string[0], Project + "/Assets", 1);
            var clone = MppmEnvironment.Parse(new[] { "--virtual-project-clone", "-name", "Player 2" },
                Project + "/Library/VP/mppm1a2b3c4d/Assets", 2);
            var backslashed = MppmEnvironment.Parse(new string[0], "C:\\workspace\\TestSDK\\Assets", 1);

            Assert.AreEqual(main.MainProjectRoot, clone.MainProjectRoot);
            Assert.AreEqual(main.ProjectHash8, clone.ProjectHash8);
            Assert.AreEqual(main.ProjectHash8, backslashed.ProjectHash8);
            Assert.AreEqual(8, main.ProjectHash8.Length);
        }

        [Test]
        public void CloneWithoutTheVpFolderInItsPath_WalksUpFromItsAssets()
        {
            // An MPPM clone's Assets sits four folders below the main project (Library/VP/<id>/Assets); with
            // the folder names unrecognised, the depth still finds the main project.
            var env = MppmEnvironment.Parse(new[] { "--virtual-project-clone" }, Project + "/Lib/VPx/mppm1/Assets", 2);

            Assert.IsTrue(env.IsClone);
            Assert.AreEqual(Full(Project), env.MainProjectRoot);
        }

        [TestCase("C:/workspace/TestSDK/", "c:\\workspace\\testsdk")]
        [TestCase("C:\\workspace\\TestSDK\\\\", "c:\\workspace\\testsdk")]
        [TestCase("c:/WORKSPACE/testsdk", "c:\\workspace\\testsdk")]
        public void NormalizeRoot_MatchesTheRelaysComparison(string path, string expected)
        {
            Assert.AreEqual(expected, MppmEnvironment.NormalizeRoot(path));
        }

        [Test]
        public void NormalizeRoot_OfNothing_IsEmpty()
        {
            Assert.AreEqual("", MppmEnvironment.NormalizeRoot(null));
            Assert.AreEqual("", MppmEnvironment.NormalizeRoot(""));
        }
    }
}
