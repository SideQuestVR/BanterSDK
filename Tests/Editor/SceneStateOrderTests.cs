using System.Reflection;
using System.Runtime.Serialization;
using BS;
using NUnit.Framework;
using UnityEngine;

namespace BS.SDKEditor.Tests
{
    /// <summary>DOM ready comes from the host, on its own schedule: it never takes a load backwards.</summary>
    public class SceneStateOrderTests
    {
        [TestCase(SceneState.NONE, SceneState.DOM_READY)]
        [TestCase(SceneState.DOM_READY, SceneState.DOM_READY)]
        [TestCase(SceneState.NOTHING_20S, SceneState.DOM_READY)]
        [TestCase(SceneState.NOTHING, SceneState.DOM_READY)]
        [TestCase(SceneState.SCENE_START, SceneState.SCENE_START)]
        [TestCase(SceneState.SCENE_READY, SceneState.SCENE_READY)]
        [TestCase(SceneState.UNITY_READY, SceneState.UNITY_READY)]
        [TestCase(SceneState.LOAD_FAILED, SceneState.LOAD_FAILED)]
        [TestCase(SceneState.USER_CANCELLED, SceneState.USER_CANCELLED)]
        public void DomReady_OnlyMovesForward(SceneState before, SceneState after) =>
            Assert.AreEqual(after, BSPipe.StateAtDomReady(before));

        [Test]
        public void ARecordedSpawn_SurvivesThePageResendingItsDefaults()
        {
            // Its constructor makes objects; only the spawn point matters here.
            var settings = (BSSceneSettings)FormatterServices.GetUninitializedObject(typeof(BSSceneSettings));
            typeof(BSSceneSettings).GetField("scene", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(settings, BSScene.Instance());
            Assert.IsFalse(settings.HasSpawnPoint);

            settings.SetPageSpawnPoint(BSSceneSettings.UnsetPageSpawnPoint);
            Assert.IsFalse(settings.HasSpawnPoint, "a page that never chose a spawn");

            settings.SpawnPoint = Vector4.zero;
            Assert.IsFalse(settings.HasSpawnPoint, "the origin leaves a later scripted teleport free to set it");

            var recorded = new Vector4(1, 2, 3, 90);
            settings.SpawnPoint = recorded;
            Assert.IsTrue(settings.HasSpawnPoint, "the first BS Spawn arrival");

            settings.SetPageSpawnPoint(BSSceneSettings.UnsetPageSpawnPoint);
            settings.SetPageSpawnPoint(Vector4.zero);
            Assert.AreEqual(recorded, settings.SpawnPoint, "re-sent settings keep the recorded spawn");

            var chosen = new Vector4(4, 5, 6, 0);
            settings.SetPageSpawnPoint(chosen);
            Assert.AreEqual(chosen, settings.SpawnPoint, "a page's real choice still wins");
        }
    }
}
