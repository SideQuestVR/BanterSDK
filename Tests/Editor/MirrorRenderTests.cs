using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BS.SDKEditor.Tests
{
    // VRPortalRenderer (BS Mirror): each render builds its own oblique projection, nothing renders where the eye can't
    // see a reflection, no texture is released while its camera still targets it, and a copy never frees the original's.
    public sealed class MirrorRenderTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        GameObject _host;
        Camera _source;
        VRPortalRenderer _mirror;

        [SetUp]
        public void SetUp()
        {
            // BanterMirror3D's shape: Unity's Plane, drawn on its local +Y. Here that is world up, with the glass at y = 0.
            _host = GameObject.CreatePrimitive(PrimitiveType.Plane);
            _host.SetActive(false);
            _mirror = _host.AddComponent<VRPortalRenderer>();
            _mirror.renderTargetSize = 32;
            _host.SetActive(true);
            if (Get<Camera>(_mirror, "renderCam") == null) Invoke(_mirror, "OnEnable");
            _source = new GameObject("Mirror test source").AddComponent<Camera>();
            _source.enabled = false;
            _source.nearClipPlane = 0.05f;
            _source.farClipPlane = 100f;
            _source.aspect = 1.5f;
            Set(_mirror, "sourceCamOverride", _source);
        }

        [TearDown]
        public void TearDown()
        {
            Release(_mirror);
            Object.DestroyImmediate(_host);
            Object.DestroyImmediate(_source.gameObject);
        }

        // The runtime component uses delayed Destroy. Keep this Edit Mode fixture self-contained.
        static void Release(VRPortalRenderer mirror)
        {
            foreach (string name in new[] { "renderCamObj", "eyeDebugObjL", "eyeDebugObjR", "renderTexL", "renderTexR" })
            {
                var value = Get<Object>(mirror, name);
                Set(mirror, name, null);
                if (value) Object.DestroyImmediate(value);
            }
            if (((ICollection<VRPortalRenderer>)VRPortalRenderer.ActiveMirrors).Contains(mirror)) Invoke(mirror, "OnDisable");
        }

        static T Get<T>(VRPortalRenderer mirror, string field) where T : class =>
            typeof(VRPortalRenderer).GetField(field, Private).GetValue(mirror) as T;
        static Matrix4x4 GetMatrix(VRPortalRenderer mirror, string field) =>
            (Matrix4x4)typeof(VRPortalRenderer).GetField(field, Private).GetValue(mirror);
        static void Set(VRPortalRenderer mirror, string field, object value) =>
            typeof(VRPortalRenderer).GetField(field, Private).SetValue(mirror, value);
        static void Invoke(VRPortalRenderer mirror, string method) =>
            typeof(VRPortalRenderer).GetMethod(method, Private).Invoke(mirror, null);

        bool RenderEye(Vector3 eye, Quaternion rotation, out Matrix4x4 projection)
        {
            var args = new object[] { Get<RenderTexture>(_mirror, "renderTexL"), new Pose(eye, rotation), null, null };
            bool rendered = (bool)typeof(VRPortalRenderer).GetMethod("renderToTexture", Private).Invoke(_mirror, args);
            projection = (Matrix4x4)args[3];
            return rendered;
        }

        static Quaternion Toward(Vector3 eye, Vector3 target) => Quaternion.LookRotation(target - eye);

        static void AssertRow(Matrix4x4 expected, Matrix4x4 actual, int row, string message)
        {
            Vector4 e = expected.GetRow(row), a = actual.GetRow(row);
            Assert.That((e - a).magnitude, Is.LessThan(1e-4f), $"{message}: row {row} {a} != {e}");
        }

        // Camera.main must be the test's own, whatever scene is open: switch the open scene's main cameras off meanwhile
        // (as ClientSeatPointerTests does) and give them back afterwards.
        static List<Camera> DisableMainCameras()
        {
            var disabled = new List<Camera>();
            for (var main = Camera.main; main != null && disabled.Count < 32; main = Camera.main)
            {
                main.enabled = false;
                disabled.Add(main);
            }
            return disabled;
        }

        static void Restore(List<Camera> cameras)
        {
            foreach (var camera in cameras)
                if (camera) camera.enabled = true;
        }

        [Test]
        public void EachRenderBuildsItsProjection_InsteadOfCuttingThePreviousOne()
        {
            var camera = Get<Camera>(_mirror, "renderCam");
            // stereoTargetEye only exists for the built-in renderer; under a scriptable pipeline (this project's URP) the
            // camera stays out of XR by only being set up and rendered while it targets a texture.
            if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline == null)
                Assert.AreEqual(StereoTargetEyeMask.None, camera.stereoTargetEye, "never an XR camera, which would ignore fieldOfView");
            else
                Assert.IsNull(camera.targetTexture, "between renders it targets nothing, and sets up only while targeting a texture");
            var left = new Vector3(-0.032f, 2f, -1f);
            var right = new Vector3(0.032f, 2f, -1f);
            Assert.IsTrue(RenderEye(left, Toward(left, Vector3.zero), out var first));
            Assert.IsTrue(RenderEye(right, Toward(right, Vector3.zero), out _), "the other eye on the same camera");
            Assert.IsTrue(RenderEye(left, Toward(left, Vector3.zero), out var again));
            for (int row = 0; row < 4; row++)
                AssertRow(first, again, row, "the same eye pose gives the same matrix, whatever rendered before it");

            // The cut only rewrites row 2, so rows 0, 1 and 3 must still be the camera's fov and aspect (the source's,
            // which is wider than the square textures) rather than whatever the first frame had.
            _source.aspect = 2f;
            Assert.IsTrue(RenderEye(left, Toward(left, Vector3.zero), out var wider));
            var unflipped = wider * Matrix4x4.Scale(new Vector3(-1f, 1f, 1f)); // mirror mode flips X for the shader
            var plain = Matrix4x4.Perspective(120f, 2f, _source.nearClipPlane, _source.farClipPlane);
            foreach (int row in new[] { 0, 1, 3 }) AssertRow(plain, unflipped, row, "fov/aspect follow the source each render");
            Assert.IsFalse(camera.enabled);
            Assert.IsNull(camera.targetTexture, "the camera does not keep targeting a texture between renders");

            // Portal mode (no reflection, no cut) keeps the square textures' aspect.
            _mirror.SetMirrorMode(false);
            Assert.IsTrue(RenderEye(left, Toward(left, Vector3.zero), out var portal));
            var square = Matrix4x4.Perspective(120f, 1f, _source.nearClipPlane, _source.farClipPlane);
            for (int row = 0; row < 4; row++) AssertRow(square, portal, row, "a portal stays square");
        }

        [Test]
        public void NothingRenders_FromBehind_TooClose_OrWithTheMirrorOutOfView()
        {
            var behind = new Vector3(0f, -1f, -1f);
            Assert.IsFalse(RenderEye(behind, Toward(behind, Vector3.zero), out _), "behind the glass");
            var touching = new Vector3(0f, 0.005f, -1f);
            Assert.IsFalse(RenderEye(touching, Toward(touching, Vector3.zero), out _), "5 mm from the glass");
            Assert.IsFalse(RenderEye(new Vector3(0f, 2f, -1f), Quaternion.LookRotation(Vector3.up, Vector3.forward), out _),
                "the whole mirror is behind the eye");
            // 0.5 m above the 10 m plane, 10 degrees off straight up: part of the plane is ahead of the eye, but the view
            // (120 degrees, aspect 1.5) never dips below the horizon, so the oblique cut would flip.
            var tilt = Quaternion.LookRotation(new Vector3(0f, Mathf.Cos(10f * Mathf.Deg2Rad), Mathf.Sin(10f * Mathf.Deg2Rad)));
            Assert.IsFalse(RenderEye(new Vector3(0f, 0.5f, 0f), tilt, out _), "looking away from the glass");
            var front = new Vector3(0f, 2f, -1f);
            Assert.IsTrue(RenderEye(front, Toward(front, Vector3.zero), out _), "in front and looking at it");
            // The cut replaces the camera's near plane, so a world's large near clip must not freeze a leaning viewer.
            _source.nearClipPlane = 0.3f;
            var leaning = new Vector3(0f, 0.05f, -1f);
            Assert.IsTrue(RenderEye(leaning, Toward(leaning, Vector3.zero), out _), "5 cm from the glass with a 0.3 m near clip");
            Assert.IsFalse(Get<Camera>(_mirror, "renderCam").enabled);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void AMirrorTurnedOverByANegativeScale_ReflectsTheSideItIsDrawnOn()
        {
            _host.transform.localScale = new Vector3(1f, -1f, 1f);
            var below = new Vector3(0f, -2f, -1f);
            Assert.IsTrue(RenderEye(below, Toward(below, Vector3.zero), out _), "the drawn side now faces down");
            var above = new Vector3(0f, 2f, -1f);
            Assert.IsFalse(RenderEye(above, Toward(above, Vector3.zero), out _), "above is now behind it");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Resize_DestroysTheOldPair_AndNeverReleasesATargetedTexture()
        {
            var oldL = Get<RenderTexture>(_mirror, "renderTexL");
            var oldR = Get<RenderTexture>(_mirror, "renderTexR");
            Get<Camera>(_mirror, "renderCam").targetTexture = oldR; // as the old code left it after every frame
            _mirror.SetRenderTextureSize(0);
            Assert.AreEqual(16, _mirror.renderTargetSize, "0 from JS/VS is clamped instead of throwing");
            Assert.IsTrue(oldL == null && oldR == null, "destroyed, not only released");
            var newL = Get<RenderTexture>(_mirror, "renderTexL");
            Assert.AreEqual(16, newL.width);
            Assert.IsNull(Get<Camera>(_mirror, "renderCam").targetTexture);
            _mirror.SetRenderTextureSize(16);
            Assert.AreSame(newL, Get<RenderTexture>(_mirror, "renderTexL"), "an unchanged size keeps the textures");
            LogAssert.NoUnexpectedReceived(); // no "Releasing render texture that is set as Camera.targetTexture!"
        }

        [Test]
        public void ADisabledMirror_DropsItsTextures_AndClampsTheSizeForOnEnable()
        {
            // OnDisable is not guaranteed in Edit Mode; release the camera objects so it is harmless if it runs.
            foreach (string name in new[] { "renderCamObj", "eyeDebugObjL", "eyeDebugObjR" })
            {
                var value = Get<Object>(_mirror, name);
                Set(_mirror, name, null);
                if (value) Object.DestroyImmediate(value);
            }
            Set(_mirror, "renderCam", null);
            var oldL = Get<RenderTexture>(_mirror, "renderTexL");
            _mirror.enabled = false;
            _mirror.SetRenderTextureSize(99999);
            Assert.AreEqual(4096, _mirror.renderTargetSize);
            Assert.IsNull(Get<RenderTexture>(_mirror, "renderTexL"), "nothing is allocated while disabled");
            Assert.IsNull(Get<RenderTexture>(_mirror, "renderTexR"));
            Assert.IsTrue(oldL == null, "the previous pair is destroyed");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ACopyOfALiveMirror_LeavesTheOriginalsTexturesAndCamera()
        {
            var originalL = Get<RenderTexture>(_mirror, "renderTexL");
            var originalR = Get<RenderTexture>(_mirror, "renderTexR");
            var originalCamera = Get<Camera>(_mirror, "renderCam");
            // JS instantiate and spawner copies clone a live mirror, serialized internals included.
            var copyHost = Object.Instantiate(_host);
            var copy = copyHost.GetComponent<VRPortalRenderer>();
            try
            {
                // Play Mode always enables the copy; Edit Mode may not.
                if (Get<RenderTexture>(copy, "renderTexL") == originalL) Invoke(copy, "OnEnable");
                Assert.IsTrue(originalL != null && originalR != null, "the original's textures survive the copy");
                Assert.AreSame(originalL, Get<RenderTexture>(_mirror, "renderTexL"));
                Assert.IsTrue(originalCamera != null, "and so does its camera");
                var copyL = Get<RenderTexture>(copy, "renderTexL");
                Assert.IsTrue(copyL != null && copyL != originalL, "the copy makes its own textures");
                var copyCamera = Get<Camera>(copy, "renderCam");
                Assert.IsTrue(copyCamera != null && copyCamera != originalCamera, "and its own camera");
                int cameras = 0;
                foreach (Transform child in copyHost.transform)
                    if (child.name == "Render Camera") cameras++;
                Assert.AreEqual(1, cameras, "a copied render camera child is removed, not left behind");

                copy.SetRenderTextureSize(64);
                Assert.IsTrue(originalL != null, "resizing the copy leaves the original's textures alone");
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                Release(copy);
                Object.DestroyImmediate(copyHost);
            }
        }

        [Test]
        public void LateUpdate_WithoutACamera_DoesNothing()
        {
            if (UnityEngine.XR.XRSettings.isDeviceActive) Assert.Ignore("This covers the non-XR path.");
            var disabled = DisableMainCameras();
            try
            {
                Set(_mirror, "sourceCamOverride", null);
                Assert.DoesNotThrow(() => Invoke(_mirror, "LateUpdate"), "no main camera between worlds");
                Set(_mirror, "sourceCamOverride", _source);
                Assert.DoesNotThrow(() => Invoke(_mirror, "LateUpdate"), "a source override, but no main camera for the non-XR eye pose");
            }
            finally { Restore(disabled); }
        }

        [Test]
        public void LateUpdate_KeepsTheLastImageWhileUnseen_AndLeavesOtherMirrorsGlobalsAlone()
        {
            if (UnityEngine.XR.XRSettings.isDeviceActive) Assert.Ignore("This covers the non-XR path.");
            var disabled = DisableMainCameras();
            var main = new GameObject("Mirror test main camera") { tag = "MainCamera" }.AddComponent<Camera>();
            var other = new RenderTexture(16, 16, 0);
            other.Create(); // a texture another mirror really published; an uncreated one may not read back as the global
            try
            {
                Assert.IsTrue(Camera.main == main, "the test's own main camera");
                var front = new Vector3(0f, 2f, -1f);
                main.transform.SetPositionAndRotation(front, Toward(front, Vector3.zero));
                Invoke(_mirror, "LateUpdate");
                var left = GetMatrix(_mirror, "eyeProjL");
                var right = GetMatrix(_mirror, "eyeProjR");
                Assert.AreNotEqual(Matrix4x4.identity, left, "in front: both eyes render");
                Assert.AreNotEqual(Matrix4x4.identity, right);
                Assert.IsTrue(Shader.GetGlobalTexture("EyeTexL") == Get<RenderTexture>(_mirror, "renderTexL"), "and are published");
                Assert.IsTrue(Shader.GetGlobalTexture("EyeTexR") == Get<RenderTexture>(_mirror, "renderTexR"));

                Shader.SetGlobalTexture("EyeTexL", other); // as another mirror that rendered later this frame
                var behind = new Vector3(0f, -1f, -1f);
                main.transform.SetPositionAndRotation(behind, Toward(behind, Vector3.zero));
                Invoke(_mirror, "LateUpdate");
                Assert.AreEqual(left, GetMatrix(_mirror, "eyeProjL"), "behind the glass: no render, the last image stays");
                Assert.AreEqual(right, GetMatrix(_mirror, "eyeProjR"));
                Assert.IsTrue(Shader.GetGlobalTexture("EyeTexL") == other, "a mirror that drew nothing publishes nothing");
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                Object.DestroyImmediate(main.gameObject);
                Restore(disabled);
                Shader.SetGlobalTexture("EyeTexL", null);
                Shader.SetGlobalTexture("EyeTexR", null);
                other.Release();
                Object.DestroyImmediate(other);
            }
        }
    }
}
