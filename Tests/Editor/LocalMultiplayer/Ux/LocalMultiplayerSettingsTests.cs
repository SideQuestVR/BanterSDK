using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace BS.LocalMultiplayer.Tests
{
    /// <summary>
    /// The one settings file every player reads (main project's UserSettings): its defaults, how it survives a
    /// save and a load, what a damaged or partial file falls back to, and how mode and roles are decided.
    /// </summary>
    public class LocalMultiplayerSettingsTests
    {
#if BANTER_MPPM
        const bool MppmInstalled = true;
#else
        const bool MppmInstalled = false;
#endif

        string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "sq-localmp-settings-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                Directory.Delete(_root, true);
            }
            catch (Exception)
            {
                // A leftover temp folder is harmless.
            }
        }

        void WriteFile(string json)
        {
            var path = LocalMultiplayerSettings.FilePath(_root);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, json);
        }

        static void AssertDefaults(LocalMultiplayerSettings settings)
        {
            Assert.AreEqual(1, settings.schema);
            Assert.AreEqual(LocalMultiplayerSettings.ModeAuto, settings.mode);
            Assert.AreEqual(LocalMultiplayerSettings.MainEditorSlot, settings.worldOwnerSlot);
            Assert.IsNotNull(settings.moderatorSlots);
            Assert.IsEmpty(settings.moderatorSlots);
            Assert.IsTrue(settings.persistSpaceState);
            Assert.IsTrue(settings.autoSaveScenesOnPlay);
            Assert.AreEqual("F8", settings.overlayKey);
            Assert.IsTrue(settings.overlayVisibleOnStart);
        }

        [Test]
        public void NewSettings_HaveTheDefaults()
        {
            AssertDefaults(new LocalMultiplayerSettings());
        }

        [Test]
        public void FilePath_IsTheMainProjectsUserSettings()
        {
            Assert.AreEqual(Path.Combine(_root, "UserSettings", "SideQuestLocalMultiplayer.json"),
                LocalMultiplayerSettings.FilePath(_root));
        }

        [Test]
        public void MissingFile_LoadsTheDefaults()
        {
            AssertDefaults(LocalMultiplayerSettings.Load(_root));
        }

        [Test]
        public void SaveThenLoad_RoundTrips()
        {
            var saved = new LocalMultiplayerSettings
            {
                mode = LocalMultiplayerSettings.ModeOn,
                worldOwnerSlot = "Player 3",
                moderatorSlots = new List<string> { "Main Editor", "Player 2" },
                persistSpaceState = false,
                autoSaveScenesOnPlay = false,
                overlayKey = "F9",
                overlayVisibleOnStart = false,
            };
            saved.Save(_root);

            var loaded = LocalMultiplayerSettings.Load(_root);
            Assert.AreEqual(saved.schema, loaded.schema);
            Assert.AreEqual(saved.mode, loaded.mode);
            Assert.AreEqual(saved.worldOwnerSlot, loaded.worldOwnerSlot);
            CollectionAssert.AreEqual(saved.moderatorSlots, loaded.moderatorSlots);
            Assert.AreEqual(saved.persistSpaceState, loaded.persistSpaceState);
            Assert.AreEqual(saved.autoSaveScenesOnPlay, loaded.autoSaveScenesOnPlay);
            Assert.AreEqual(saved.overlayKey, loaded.overlayKey);
            Assert.AreEqual(saved.overlayVisibleOnStart, loaded.overlayVisibleOnStart);
        }

        [Test]
        public void SavingAgain_ReplacesTheFile_AndLeavesNoTempFile()
        {
            new LocalMultiplayerSettings { overlayKey = "F9" }.Save(_root);
            new LocalMultiplayerSettings { overlayKey = "F10" }.Save(_root);

            Assert.AreEqual("F10", LocalMultiplayerSettings.Load(_root).overlayKey);
            Assert.IsFalse(File.Exists(LocalMultiplayerSettings.FilePath(_root) + ".tmp"));
        }

        [Test]
        public void BlankFields_FallBackToTheDefaults()
        {
            WriteFile("{\"mode\":\"\",\"worldOwnerSlot\":\"\",\"overlayKey\":\"\"}");

            var loaded = LocalMultiplayerSettings.Load(_root);
            Assert.AreEqual(LocalMultiplayerSettings.ModeAuto, loaded.mode);
            Assert.AreEqual(LocalMultiplayerSettings.MainEditorSlot, loaded.worldOwnerSlot);
            Assert.AreEqual("F8", loaded.overlayKey);
        }

        [Test]
        public void MissingFields_KeepTheDefaults()
        {
            WriteFile("{\"mode\":\"off\"}");

            var loaded = LocalMultiplayerSettings.Load(_root);
            Assert.AreEqual(LocalMultiplayerSettings.ModeOff, loaded.mode);
            Assert.AreEqual(LocalMultiplayerSettings.MainEditorSlot, loaded.worldOwnerSlot);
            Assert.IsNotNull(loaded.moderatorSlots);
            Assert.IsTrue(loaded.persistSpaceState);
            Assert.IsTrue(loaded.autoSaveScenesOnPlay);
            Assert.AreEqual("F8", loaded.overlayKey);
        }

        [Test]
        public void UnknownFields_AreIgnored()
        {
            WriteFile("{\"schema\":1,\"mode\":\"on\",\"aFutureField\":42,\"another\":{\"x\":true}}");

            Assert.AreEqual(LocalMultiplayerSettings.ModeOn, LocalMultiplayerSettings.Load(_root).mode);
        }

        [Test]
        public void UnreadableFile_LoadsTheDefaults()
        {
            // Load logs a warning (not an error) and carries on with the defaults.
            WriteFile("this is not { json");

            AssertDefaults(LocalMultiplayerSettings.Load(_root));
        }

        [TestCase("on", true)]
        [TestCase("ON", true)]
        [TestCase("On", true)]
        [TestCase("off", false)]
        [TestCase("OFF", false)]
        public void OnAndOff_DecideAlone(string mode, bool enabled)
        {
            Assert.AreEqual(enabled, new LocalMultiplayerSettings { mode = mode }.IsEnabled);
        }

        [TestCase("auto")]
        [TestCase("AUTO")]
        [TestCase("")]
        [TestCase(null)]
        [TestCase("sometimes")]
        public void AnyOtherMode_FollowsMultiplayerPlayMode(string mode)
        {
            // The same versionDefines as the runtime assembly, so BANTER_MPPM agrees with what it compiled.
            Assert.AreEqual(MppmInstalled, new LocalMultiplayerSettings { mode = mode }.IsEnabled);
        }

        [Test]
        public void ClientId_IsLocalPlusSlot()
        {
            Assert.AreEqual("local:Player 2", LocalMultiplayerSettings.ClientIdFor("Player 2"));
            Assert.AreEqual("local:Main Editor", LocalMultiplayerSettings.ClientIdFor(LocalMultiplayerSettings.MainEditorSlot));
        }

        [Test]
        public void RoleFor_TheDefaultOwner_IsTheMainEditor()
        {
            var settings = new LocalMultiplayerSettings();
            Assert.AreEqual(PlayerRole.Owner, settings.RoleFor("Main Editor"));
            Assert.AreEqual(PlayerRole.Member, settings.RoleFor("Player 2"));
        }

        [Test]
        public void RoleFor_ModeratorsAndMembers()
        {
            var settings = new LocalMultiplayerSettings
            {
                worldOwnerSlot = "Player 2",
                moderatorSlots = new List<string> { "Player 3", "Player 2" },
            };
            Assert.AreEqual(PlayerRole.Owner, settings.RoleFor("Player 2"), "the owner stays owner when also listed");
            Assert.AreEqual(PlayerRole.Moderator, settings.RoleFor("Player 3"));
            Assert.AreEqual(PlayerRole.Member, settings.RoleFor("Main Editor"));
            Assert.AreEqual(PlayerRole.Member, settings.RoleFor("Player 4"));
        }

        [Test]
        public void RoleFor_MatchesSlotsExactly()
        {
            var settings = new LocalMultiplayerSettings { moderatorSlots = new List<string> { "Player 3" } };
            Assert.AreEqual(PlayerRole.Member, settings.RoleFor("main editor"));
            Assert.AreEqual(PlayerRole.Member, settings.RoleFor("player 3"));
            Assert.AreEqual(PlayerRole.Member, settings.RoleFor(null));
        }

        [Test]
        public void RoleFor_WithoutAModeratorList_GivesMembers()
        {
            var settings = new LocalMultiplayerSettings { moderatorSlots = null };
            Assert.AreEqual(PlayerRole.Member, settings.RoleFor("Player 3"));
            Assert.AreEqual(PlayerRole.Owner, settings.RoleFor("Main Editor"));
        }
    }
}
