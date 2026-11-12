using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BS.SDKEditor.Tests
{
    /// <summary>What an interrupted Builder operation leaves behind in a scene.</summary>
    public class BuilderWindowSceneMarkerTests
    {
        Scene _scene;

        [SetUp]
        public void SetUp() => _scene = EditorSceneManager.NewPreviewScene();

        [TearDown]
        public void TearDown() => EditorSceneManager.ClosePreviewScene(_scene);

        GameObject Create(string name)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, _scene);
            return go;
        }

        [Test]
        public void RemoveMarkers_TakesOnlyTheRootMarker()
        {
            Create(CustomSceneProcessor.SceneBeeBuildMarkerName);
            var kept = Create("kept");
            var nested = new GameObject(CustomSceneProcessor.SceneBeeBuildMarkerName);
            nested.transform.SetParent(kept.transform, false);

            Assert.AreEqual(1, SceneBundleMarkerCleanup.RemoveMarkers(_scene));
            Assert.AreEqual(1, _scene.rootCount);
            Assert.AreEqual(0, SceneBundleMarkerCleanup.RemoveMarkers(_scene));
            Assert.IsTrue(nested != null, "only the Builder's own root object is taken");
        }

        [Test]
        public void RemoveMarkers_WithNoLoadedScene_DoesNothing() =>
            Assert.AreEqual(0, SceneBundleMarkerCleanup.RemoveMarkers(default));

        [Test]
        public void Clean_LeavesTheMarkerDuringABuild()
        {
            Create(CustomSceneProcessor.SceneBeeBuildMarkerName);
            BuilderWindow.SceneBundleBuildInProgress = true;
            try
            {
                SceneBundleMarkerCleanup.Clean(_scene);
            }
            finally
            {
                BuilderWindow.SceneBundleBuildInProgress = false;
            }
            Assert.AreEqual(1, _scene.rootCount);
        }

        // ---- the screenshotter ----

        static GameObject Cube() => GameObject.CreatePrimitive(PrimitiveType.Cube);

        [Test]
        public void Screenshot_ThatCantBeSaved_PutsTheSceneBack()
        {
            var cube = Cube();
            var particles = new GameObject("particles");
            try
            {
                particles.transform.SetParent(cube.transform, false);
                particles.AddComponent<ParticleSystem>();
                var unwritable = Path.Combine(Path.GetTempPath(), "bs-no-such-folder", "shot.png");

                LogAssert.ignoreFailingMessages = true;
                Assert.Throws<DirectoryNotFoundException>(() =>
                    ObjectScreenshotter.CaptureGameObject(cube, unwritable, 16, true, 0f, 31));

                Assert.AreEqual(0, cube.layer);
                Assert.IsTrue(particles.activeSelf);
                // GameObject.Find can't see a hidden object.
                Assert.IsFalse(Resources.FindObjectsOfTypeAll<Camera>().Any(camera => camera.name == "TempScreenshotCamera"));
            }
            finally
            {
                Object.DestroyImmediate(cube);
            }
        }

        [Test]
        public void Screenshot_OfNothing_LeavesParticlesAlone()
        {
            var empty = new GameObject("no renderers");
            try
            {
                LogAssert.Expect(LogType.Error, "Target object has no renderers (excluding particles).");
                ObjectScreenshotter.CaptureGameObject(empty, "unused.png");
            }
            finally
            {
                Object.DestroyImmediate(empty);
            }
        }
    }
}
