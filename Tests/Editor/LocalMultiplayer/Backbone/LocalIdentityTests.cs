using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BS.LocalMultiplayer.Tests
{
    /// <summary>
    /// Player identity: the uid is NetworkService.HashUid of the stable user id ("local:" + slot), and the
    /// shared settings decide the roles (one world owner, any number of moderators).
    /// </summary>
    public class LocalIdentityTests
    {
        // NetworkService.HashUid (NetworkService.cs:1030-1037), spelled out: two Appends, raw id then salt.
        static string ReferenceHashUid(string rawId)
        {
            var hash = new Hash128();
            hash.Append(rawId);
            hash.Append("chicken and cheese no cheese");
            return hash.ToString();
        }

        [TestCase("local:Main Editor")]
        [TestCase("local:Player 2")]
        [TestCase("local:vp-mppmdeadbeef")]
        [TestCase("sq:12345")]
        public void HashUid_IsProductionsTwoAppendHash(string rawId)
        {
            Assert.AreEqual(ReferenceHashUid(rawId), LocalIdentityFactory.HashUid(rawId));
        }

        [Test]
        public void HashUid_IsA128BitHex()
        {
            var uid = LocalIdentityFactory.HashUid("local:Main Editor");

            Assert.AreEqual(32, uid.Length);
            StringAssert.IsMatch("^[0-9a-fA-F]{32}$", uid);
            Assert.AreNotEqual(uid, LocalIdentityFactory.HashUid("local:Player 2"));
        }

        [Test]
        public void HashUid_PassesNothingThrough()
        {
            Assert.IsNull(LocalIdentityFactory.HashUid(null));
            Assert.AreEqual("", LocalIdentityFactory.HashUid(""));
        }

        [Test]
        public void ClientId_IsLocalPlusSlot()
        {
            Assert.AreEqual("local:Main Editor", LocalMultiplayerSettings.ClientIdFor("Main Editor"));
            Assert.AreEqual("local:Player 2", LocalMultiplayerSettings.ClientIdFor("Player 2"));
        }

        [Test]
        public void DefaultSettings_MakeTheMainEditorTheOwner()
        {
            var settings = new LocalMultiplayerSettings();

            Assert.AreEqual(PlayerRole.Owner, settings.RoleFor("Main Editor"));
            Assert.AreEqual(PlayerRole.Member, settings.RoleFor("Player 2"));
            Assert.AreEqual(PlayerRole.Member, settings.RoleFor("Player ?"));
        }

        [Test]
        public void Moderators_AndAnotherOwner()
        {
            var settings = new LocalMultiplayerSettings
            {
                worldOwnerSlot = "Player 3",
                moderatorSlots = new List<string> { "Player 2", "Player 3" },
            };

            Assert.AreEqual(PlayerRole.Member, settings.RoleFor("Main Editor"));
            Assert.AreEqual(PlayerRole.Moderator, settings.RoleFor("Player 2"));
            // The owner is the owner even when also listed as a moderator.
            Assert.AreEqual(PlayerRole.Owner, settings.RoleFor("Player 3"));
            Assert.AreEqual(PlayerRole.Member, settings.RoleFor("Player 4"));
        }

        [Test]
        public void SlotNames_AreCaseSensitive()
        {
            var settings = new LocalMultiplayerSettings();

            Assert.AreEqual(PlayerRole.Member, settings.RoleFor("main editor"));
        }

        [TestCase("on", true)]
        [TestCase("ON", true)]
        [TestCase("off", false)]
        [TestCase("Off", false)]
        public void Mode_OnAndOff_Decide(string mode, bool enabled)
        {
            Assert.AreEqual(enabled, new LocalMultiplayerSettings { mode = mode }.IsEnabled);
        }

        [Test]
        public void Mode_Auto_FollowsTheMultiplayerPlayModePackage()
        {
            var settings = new LocalMultiplayerSettings { mode = "auto" };
#if BANTER_MPPM
            Assert.IsTrue(settings.IsEnabled);
#else
            Assert.IsFalse(settings.IsEnabled);
#endif
        }

        [Test]
        public void SettingsFile_LivesInTheMainProjectsUserSettings()
        {
            StringAssert.EndsWith("SideQuestLocalMultiplayer.json", LocalMultiplayerSettings.FilePath("C:/workspace/TestSDK"));
            StringAssert.Contains("UserSettings", LocalMultiplayerSettings.FilePath("C:/workspace/TestSDK"));
        }

        [Test]
        public void Identity_ForAClone()
        {
            var env = MppmEnvironment.Parse(new[] { "--virtual-project-clone", "-mainProcessId=99", "-name", "Player 2" },
                "C:/workspace/TestSDK/Library/VP/mppm1a2b3c4d/Assets", 5);
            var settings = new LocalMultiplayerSettings { moderatorSlots = new List<string> { "Player 2" } };

            var identity = LocalIdentityFactory.Build(env, settings);

            Assert.AreEqual("Player 2", identity.Slot);
            Assert.AreEqual(2, identity.SlotIndex);
            Assert.IsTrue(identity.IsClone);
            Assert.AreEqual(99, identity.MainProcessId);
            Assert.AreEqual("local:Player 2", identity.ClientId);
            Assert.AreEqual("Player 2", identity.DisplayName);
            Assert.AreEqual(ReferenceHashUid("local:Player 2"), identity.Uid);
            Assert.AreEqual(PlayerRole.Moderator, identity.Role);
            Assert.AreEqual("local:Main Editor", identity.WorldOwnerClientId);
            Assert.AreEqual(env.MainProjectRoot, identity.MainProjectRoot);
        }

        [Test]
        public void Identity_ForTheMainEditor_IsTheOwner()
        {
            var env = MppmEnvironment.Parse(new string[0], "C:/workspace/TestSDK/Assets", 1717);

            var identity = LocalIdentityFactory.Build(env, new LocalMultiplayerSettings());

            Assert.AreEqual("local:Main Editor", identity.ClientId);
            Assert.AreEqual(PlayerRole.Owner, identity.Role);
            Assert.AreEqual(identity.ClientId, identity.WorldOwnerClientId);
            Assert.AreEqual(1717, identity.MainProcessId);
            // The same uid the remote side derives from the clientId it gets from the relay.
            Assert.AreEqual(LocalIdentityFactory.HashUid(identity.ClientId), identity.Uid);
        }

        [TestCase("Main Editor", 1)]
        [TestCase("Player 2", 2)]
        [TestCase("player 4", 4)]
        [TestCase("Player12", 12)]
        [TestCase("vp-mppm1a2b3c4d", 0)]
        [TestCase("", 0)]
        public void SlotIndex(string slot, int index)
        {
            Assert.AreEqual(index, LocalIdentityFactory.SlotIndexOf(slot));
        }

        [Test]
        public void Tints_DifferPerSlot_AndAreStableForUnnamedClones()
        {
            var tints = new HashSet<Color>();
            for (var slot = 1; slot <= 4; slot++) tints.Add(LocalIdentityFactory.TintFor(slot, "x"));

            Assert.AreEqual(4, tints.Count);
            Assert.AreEqual(LocalIdentityFactory.TintFor(0, "local:vp-a"), LocalIdentityFactory.TintFor(0, "local:vp-a"));
            Assert.AreEqual(LocalIdentityFactory.TintFor(9, "local:Player 9"), LocalIdentityFactory.TintFor(9, "local:Player 9"));
        }

        [Test]
        public void UserDataColours_AreProductions()
        {
            Assert.AreEqual("4488FF", LocalIdentityFactory.LocalColor);
            Assert.AreEqual("AAAAAA", LocalIdentityFactory.RemoteColor);
        }
    }
}
