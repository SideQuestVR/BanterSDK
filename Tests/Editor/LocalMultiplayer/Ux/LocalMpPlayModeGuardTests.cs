using System.Collections.Generic;
using System.Text.RegularExpressions;
using BS.LocalMultiplayer.Editor;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BS.LocalMultiplayer.Tests
{
    /// <summary>
    /// The Play guard never stops Play over an untitled scene, and never asks about one: playing alone needs no
    /// saved scene, and the Test Runner's edit-mode runs enter Play from an untitled scene of their own, where a
    /// dialog would hang the run. It says so in the Console instead.
    /// </summary>
    public class LocalMpPlayModeGuardTests
    {
        // A preview scene has no file, like an untitled one, and leaves the scenes the creator has open alone.
        Scene _untitled;

        [SetUp]
        public void SetUp() => _untitled = EditorSceneManager.NewPreviewScene();

        [TearDown]
        public void TearDown() => EditorSceneManager.ClosePreviewScene(_untitled);

        [Test]
        public void UntitledScene_OnlyWarns_AndPlayStarts()
        {
            LogAssert.Expect(LogType.Warning, new Regex(@"^\[LocalMP\] Never saved: .*Save it and press Play again"));
            Assert.IsTrue(LocalMpPlayModeGuard.LetPlayStart(new LocalMultiplayerSettings(),
                new List<Scene> { _untitled }, new List<Scene>()));
        }

        [Test]
        public void UntitledScene_StillDoesntAsk_WhenScenesAreNotSavedOnPlay()
        {
            // With autoSaveScenesOnPlay off, only scenes with a file and unsaved changes bring up the question.
            var settings = new LocalMultiplayerSettings { autoSaveScenesOnPlay = false };
            LogAssert.Expect(LogType.Warning, new Regex(@"^\[LocalMP\] Never saved: "));
            Assert.IsTrue(LocalMpPlayModeGuard.LetPlayStart(settings, new List<Scene> { _untitled }, new List<Scene>()));
        }

        [Test]
        public void SavedScenes_LetPlayStart()
        {
            Assert.IsTrue(LocalMpPlayModeGuard.LetPlayStart(new LocalMultiplayerSettings(), new List<Scene>(), new List<Scene>()));
            Assert.IsTrue(LocalMpPlayModeGuard.LetPlayStart(new LocalMultiplayerSettings(), null, null));
        }

        [Test]
        public void Warning_NamesTheScenes_AndSaysWhy()
        {
            var scenes = new List<Scene> { _untitled };
            var text = LocalMpPlayModeGuard.UntitledWarning(scenes);
            StringAssert.StartsWith("[LocalMP] Never saved: " + LocalMpScenes.Names(scenes) + ".", text);
            StringAssert.Contains("Multiplayer Play Mode players", text);
        }
    }
}
