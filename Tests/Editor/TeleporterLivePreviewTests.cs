using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BS.Utilities.Async;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace BS.SDKEditor.Tests
{
    public class TeleporterLivePreviewTests
    {
        Scene scene;
        BSTeleporter portal;
        Camera viewer;

        [SetUp]
        public void SetUp()
        {
            scene = EditorSceneManager.NewPreviewScene();
            var root = New("Live portal test");
            root.AddComponent<MeshFilter>().sharedMesh = Resources.Load<Mesh>("CreatorConveniences/Models/TeleporterPortal");
            root.AddComponent<MeshRenderer>().sharedMaterial = Resources.Load<Material>("CreatorConveniences/Materials/TeleporterPortal");
            portal = root.AddComponent<BSTeleporter>();
            portal.DestinationPosition = Vector3.forward * 5f;
            portal.portalPreviewEnabled = true;
            viewer = New("Portal viewer").AddComponent<Camera>();
            viewer.enabled = false;
            viewer.scene = scene;
            viewer.transform.SetPositionAndRotation(new Vector3(0f, 1.6f, -3f), Quaternion.identity);
            viewer.clearFlags = CameraClearFlags.SolidColor;
            viewer.backgroundColor = Color.black;
            viewer.aspect = 1f;
            viewer.fieldOfView = 60f;
        }

        [TearDown]
        public void TearDown()
        {
            portal.ReleaseEditorPortalPreview();
            EditorSceneManager.ClosePreviewScene(scene);
        }

        [Test]
        public void LiveGate_UsesRangeFrustumLayerAndRendererState()
        {
            Assert.That(portal.ShowRuntimeVisual, Is.True);
            Assert.That(portal.RealtimeWhenNearby, Is.False);
            Assert.That(portal.LivePortalVisible(viewer), Is.True);
            viewer.transform.position = new Vector3(0f, 1.6f, -20f);
            Assert.That(portal.LivePortalVisible(viewer), Is.False);
            viewer.transform.position = new Vector3(0f, 1.6f, -3f);
            viewer.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            Assert.That(portal.LivePortalVisible(viewer), Is.False);
            viewer.transform.rotation = Quaternion.identity;
            viewer.cullingMask = 0;
            Assert.That(portal.LivePortalVisible(viewer), Is.False);
            viewer.cullingMask = ~0;
            portal.GetComponent<MeshRenderer>().enabled = false;
            Assert.That(portal.LivePortalVisible(viewer), Is.False);
            portal.GetComponent<MeshRenderer>().enabled = true;
            viewer.transform.position = Vector3.up * 1.6f;
            Assert.That(portal.LivePortalVisible(viewer), Is.False, "A viewer on the clipping plane must not produce a degenerate oblique projection.");
            portal.portalResolution = 9000;
            Assert.That(portal.PortalResolution, Is.EqualTo(1024));
            portal.portalResolution = -2;
            Assert.That(portal.PortalResolution, Is.EqualTo(64));
            Assert.That(portal.LiveCaptureInterval, Is.Zero);
            portal.realtimeRefreshRate = 10f;
            Assert.That(portal.LiveCaptureInterval, Is.EqualTo(.1).Within(.00001));
        }

        [TestCase(-.032f)]
        [TestCase(0f)]
        [TestCase(.032f)]
        public void PortalMapping_PreservesEachEyeViewAcrossRotatedDestinations(float eyeOffset)
        {
            var entrance = new Pose(new Vector3(3f, .2f, -4f), Quaternion.Euler(0f, 35f, 0f));
            var destination = new Pose(new Vector3(-7f, 2f, 6f), Quaternion.Euler(0f, 130f, 0f));
            var mapping = BSTeleporter.PortalMapping(entrance, destination);
            var eye = Matrix4x4.TRS(entrance.position + entrance.rotation * new Vector3(eyeOffset, 1.6f, -2f), entrance.rotation, Vector3.one);
            var sourceView = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * eye.inverse;
            var virtualView = sourceView * mapping.inverse;
            var virtualEye = virtualView.inverse.GetColumn(3);
            var expectedEye = destination.position + destination.rotation * new Vector3(eyeOffset, 1.6f, -2f);
            Assert.That(Vector3.Distance(virtualEye, expectedEye), Is.LessThan(.0001f));
            var sourcePoint = entrance.position + entrance.rotation * new Vector3(.4f, 1.3f, 1f);
            Assert.That(Vector3.Distance(virtualView.MultiplyPoint(mapping.MultiplyPoint(sourcePoint)), sourceView.MultiplyPoint(sourcePoint)), Is.LessThan(.0001f));
        }

        [Test]
        public void RuntimeVisualOff_LeavesEditorPhotoAndAuthoredRendererStateAvailable()
        {
            portal.ShowRuntimeVisual = false;
            portal.UpdateEditorPortalPreview();
            var properties = new MaterialPropertyBlock();
            var renderer = portal.GetComponent<MeshRenderer>();
            renderer.GetPropertyBlock(properties);
            Assert.That(properties.GetFloat("_HasImage"), Is.EqualTo(1f));
            Assert.That(properties.GetTexture("_MainTex"), Is.InstanceOf<RenderTexture>());
            Assert.That(renderer.forceRenderingOff, Is.False);
        }

        GameObject New(string name) { var go = new GameObject(name); SceneManager.MoveGameObjectToScene(go, scene); return go; }

    }

#if !GREENFIELD_PROJECT && !BANTER_FLEX
    public class TeleporterLivePlayModeTests
    {
        GameObject root, cameraObject, linkObject;
        BSTeleporter portal;
        RenderTexture target;
        Camera viewer;
        int renderedFrames;
        readonly List<Camera> pausedCameras = new List<Camera>();

        [UnityTest]
        public IEnumerator LiveTransitions_ReuseCamera_Throttle_ReturnToPhoto_ReenterImmediately_AndReleaseOnHide()
        {
            UnityEditor.SessionState.SetBool("BS_SEAT_PLAY_TEST_SUPPRESS_BROWSER", true);
            yield return new EnterPlayMode();
            UnityEditor.SessionState.SetBool("BS_SEAT_PLAY_TEST_SUPPRESS_BROWSER", false);
            UnityGame.SetMainThread();
            var scene = BSScene.Instance();
            scene.state = SceneState.UNITY_READY;
            scene.settings = new BSSceneSettings(scene.InstanceID.ToString());
            linkObject = new GameObject("Runtime portal scene link");
            scene.link = linkObject.AddComponent<BSLink>();
            scene.link.scene = scene;
            root = new GameObject("Runtime portal test");
            root.AddComponent<MeshFilter>().sharedMesh = Resources.Load<Mesh>("CreatorConveniences/Models/TeleporterPortal");
            var renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = Resources.Load<Material>("CreatorConveniences/Materials/TeleporterPortal");
            var collider = root.AddComponent<BoxCollider>(); collider.isTrigger = true;
            portal = root.AddComponent<BSTeleporter>();
            portal.DestinationPosition = Vector3.forward * 5f;
            portal.portalPreviewEnabled = true;
            portal.realtimeRefreshRate = .1f;
            portal.RealtimeWhenNearby = true;
            foreach (var other in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                if (!other.enabled || !other.CompareTag("MainCamera")) continue;
                pausedCameras.Add(other);
                other.enabled = false;
            }
            cameraObject = new GameObject("Runtime portal viewer");
            cameraObject.tag = "MainCamera";
            viewer = cameraObject.AddComponent<Camera>();
            viewer.transform.SetPositionAndRotation(new Vector3(0f, 1.6f, -3f), Quaternion.identity);
            target = new RenderTexture(128, 128, 24);
            viewer.targetTexture = target;
            renderedFrames = 0;
            RenderPipelineManager.endCameraRendering += ViewerRendered;
            Assert.That(Camera.main, Is.SameAs(viewer), "This fixture must own the active local camera.");
            Assert.That(portal.LivePortalVisible(viewer), Is.True);
            Assert.That(portal.enabled && portal.RealtimeWhenNearby && portal.ShowRuntimeVisual, Is.True);
            var properties = new MaterialPropertyBlock();
            var deadline = Time.realtimeSinceStartupAsDouble + 3;
            do
            {
                yield return null;
                renderer.GetPropertyBlock(properties);
            } while (properties.GetFloat("_PerspectiveImage") < .5f && Time.realtimeSinceStartupAsDouble < deadline);
            Assert.That(renderedFrames, Is.GreaterThan(0), "The real viewer must render through URP's camera callbacks in batch mode.");
            var image = (RenderTexture)properties.GetTexture("_MainTex");
            Assert.That(image, Is.Not.Null);
            Assert.That(properties.GetFloat("_PerspectiveImage"), Is.EqualTo(1f));
            var capture = (Camera)Field("liveCaptureCamera");
            Assert.That(capture, Is.Not.Null);
            var nextCapture = (double)Field("nextPortalCapture");
            portal.UpdateEditorPortalPreview();
            Assert.That((double)Field("nextPortalCapture"), Is.EqualTo(nextCapture), "A second update before the configured interval cannot render again.");
            var previousRenders = renderedFrames;
            yield return null; yield return null;
            Assert.That(renderedFrames, Is.GreaterThan(previousRenders));
            Assert.That((double)Field("nextPortalCapture"), Is.EqualTo(nextCapture), "Real source-camera callbacks respect the configured FPS cap.");
            viewer.transform.position = new Vector3(0f, 1.6f, -20f);
            yield return null;
            Assert.That((Camera)Field("liveCaptureCamera") == null, Is.True, "Range exit immediately releases the live camera.");
            renderer.GetPropertyBlock(properties);
            Assert.That(properties.GetFloat("_PerspectiveImage"), Is.Zero, "Range exit immediately returns to a cached photo even with a ten-second live interval.");
            portal.realtimeRefreshRate = 0f;
            viewer.transform.position = new Vector3(0f, 1.6f, -3f);
            yield return null;
            renderer.GetPropertyBlock(properties);
            Assert.That(properties.GetFloat("_PerspectiveImage"), Is.EqualTo(1f), "Reentry refreshes immediately despite the previous rate cap.");
            capture = (Camera)Field("liveCaptureCamera");
            nextCapture = (double)Field("nextPortalCapture");
            yield return null; yield return null;
            Assert.That(Field("liveCaptureCamera"), Is.SameAs(capture), "Every-frame mode must reuse its hidden camera.");
            Assert.That((double)Field("nextPortalCapture"), Is.GreaterThan(nextCapture), "Every-frame mode captures again on following frames.");
            viewer.Render();
            nextCapture = (double)Field("nextPortalCapture");
            viewer.Render();
            Assert.That((double)Field("nextPortalCapture"), Is.EqualTo(nextCapture), "Repeated real viewer renders in one frame reuse the same mono/SPI capture.");
            viewer.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            yield return null;
            Assert.That((Camera)Field("liveCaptureCamera") == null, Is.True, "Turning away releases the camera too.");
            viewer.transform.rotation = Quaternion.identity;
            yield return null;
            capture = (Camera)Field("liveCaptureCamera");
            portal.ShowRuntimeVisual = false;
            Assert.That(renderer.forceRenderingOff, Is.True);
            Assert.That(collider.enabled && portal.enabled && portal.HasDestination, Is.True);
            yield return null;
            Assert.That(image == null && capture == null, Is.True, "Hiding releases owned CPU/GPU capture objects.");
            renderer.GetPropertyBlock(properties);
            Assert.That(properties.GetFloat("_HasImage"), Is.Zero);
            portal.ShowRuntimeVisual = true;
            Assert.That(renderer.forceRenderingOff, Is.False);
            yield return null;
            capture = (Camera)Field("liveCaptureCamera");
            Assert.That(capture, Is.Not.Null);
            portal.enabled = false;
            yield return null;
            Assert.That(capture == null, Is.True);
            Assert.That(renderer.forceRenderingOff, Is.False);
            renderer.forceRenderingOff = true;
            portal.enabled = true;
            portal.ShowRuntimeVisual = false;
            portal.ShowRuntimeVisual = true;
            Assert.That(renderer.forceRenderingOff, Is.True, "Toggling the visual preserves an independently hidden renderer's authored state.");
        }

        object Field(string name) => typeof(BSTeleporter).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(portal);
        void ViewerRendered(ScriptableRenderContext context, Camera camera) { if (camera == viewer) renderedFrames++; }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            UnityEditor.SessionState.SetBool("BS_SEAT_PLAY_TEST_SUPPRESS_BROWSER", false);
            RenderPipelineManager.endCameraRendering -= ViewerRendered;
            if (root != null) Object.Destroy(root);
            if (cameraObject != null) Object.Destroy(cameraObject);
            if (linkObject != null) Object.Destroy(linkObject);
            foreach (var camera in pausedCameras) if (camera != null) camera.enabled = true;
            pausedCameras.Clear();
            yield return null;
            if (target != null) { target.Release(); Object.Destroy(target); target = null; }
            if (Application.isPlaying) yield return new ExitPlayMode();
        }
    }
#endif
}
