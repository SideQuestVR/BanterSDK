using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace BS.SDKEditor.Tests
{
    public sealed class MirrorJiggleViewTests
    {
        GameObject _host;
        VRPortalRenderer _mirror;

        [SetUp]
        public void SetUp()
        {
            _host = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _host.SetActive(false);
            _mirror = _host.AddComponent<VRPortalRenderer>();
            _mirror.renderTargetSize = 16;
            _host.SetActive(true);
            if (typeof(VRPortalRenderer).GetField("renderCam", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_mirror) == null)
                typeof(VRPortalRenderer).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_mirror, null);
        }

        [TearDown]
        public void TearDown()
        {
            ReleaseTestResources();
            Object.DestroyImmediate(_host);
        }

        void ReleaseTestResources()
        {
            // The runtime component uses delayed Destroy. Keep this Edit Mode fixture self-contained.
            foreach (string name in new[] { "renderCamObj", "eyeDebugObjL", "eyeDebugObjR", "renderTexL", "renderTexR" })
            {
                var field = typeof(VRPortalRenderer).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
                var value = field.GetValue(_mirror) as Object;
                field.SetValue(_mirror, null);
                if (value) Object.DestroyImmediate(value);
            }
        }

        [Test]
        public void ReflectedViewDoesNotRenderOrMoveItsCamera()
        {
            var camera = (Camera)typeof(VRPortalRenderer).GetField("renderCam", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_mirror);
            var oldPosition = camera.transform.position;
            Assert.IsTrue(_mirror.TryGetJiggleView(new Vector3(0, 2, -4), Quaternion.identity, 0.1f, 100f,
                out var clip, out var eye, out _));
            Assert.AreEqual(new Vector3(0, -2, -4), eye);
            Assert.IsFalse(camera.enabled);
            Assert.IsNull(camera.targetTexture);
            Assert.AreEqual(oldPosition, camera.transform.position);
            Assert.IsTrue(float.IsFinite(clip.determinant));
        }

        [Test]
        public void NonXrSourceOverrideUsesMainPoseAndKeepsTheFirstViewConservative()
        {
            if (UnityEngine.XR.XRSettings.isDeviceActive) Assert.Ignore("This assertion covers the non-XR renderer path.");
            GameObject mainHost = null;
            var overrideHost = new GameObject("Isolated mirror source override");
            try
            {
                var main = Camera.main;
                if (!main)
                {
                    mainHost = new GameObject("Isolated mirror main camera") { tag = "MainCamera" };
                    main = mainHost.AddComponent<Camera>();
                }
                var source = overrideHost.AddComponent<Camera>();
                source.transform.position = main.transform.position + Vector3.up * 10f;
                source.nearClipPlane = 0.2f;
                source.farClipPlane = 50f;
                typeof(VRPortalRenderer).GetField("sourceCamOverride", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_mirror, source);
                var internalCamera = (Camera)typeof(VRPortalRenderer).GetField("renderCam", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_mirror);
                internalCamera.aspect = 2f;
                Assert.IsTrue(_mirror.TryGetJiggleView(false, out var actual, out var actualEye, out _));
                Assert.IsTrue(_mirror.TryGetJiggleView(main.transform.position, main.transform.rotation,
                    source.nearClipPlane, source.farClipPlane, out var expected, out var expectedEye, out _));
                Assert.AreEqual(expectedEye, actualEye);
                Assert.AreEqual(expected, actual);
                Assert.AreEqual(Vector3.Reflect(main.transform.position, Vector3.up), actualEye);
                Assert.AreEqual(2f, internalCamera.aspect, "The collector must not change camera projection state.");
            }
            finally
            {
                Object.DestroyImmediate(overrideHost);
                if (mainHost) Object.DestroyImmediate(mainHost);
            }
        }

        [Test]
        public void SurfaceDemandHonorsFrustumLayerAndRendererVisibility()
        {
            var projection = Matrix4x4.Perspective(60f, 1f, 0.1f, 100f);
            var planes = GeometryUtility.CalculateFrustumPlanes(projection * Matrix4x4.Scale(new Vector3(1, 1, -1))
                * Matrix4x4.TRS(new Vector3(0, 0, -4), Quaternion.identity, Vector3.one).inverse);
            Assert.IsTrue(_mirror.SurfaceInView(planes, ~0));
            Assert.IsFalse(_mirror.SurfaceInView(planes, 0));
            _host.GetComponent<Renderer>().enabled = false;
            Assert.IsFalse(_mirror.SurfaceInView(planes, ~0));
        }

        [Test]
        public void DisabledMirrorLeavesTheActiveRegistry()
        {
            CollectionAssert.Contains(VRPortalRenderer.ActiveMirrors, _mirror);
            ReleaseTestResources();
            _host.SetActive(false);
            if (((System.Collections.Generic.ICollection<VRPortalRenderer>)VRPortalRenderer.ActiveMirrors).Contains(_mirror))
                typeof(VRPortalRenderer).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_mirror, null);
            CollectionAssert.DoesNotContain(VRPortalRenderer.ActiveMirrors, _mirror);
        }
    }
}
