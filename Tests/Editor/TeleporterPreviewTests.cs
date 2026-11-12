using System.Collections;
using System.IO;
using BS;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BS.SDKEditor.Tests
{
    public class TeleporterPreviewTests
    {
        Scene scene;
        BSTeleporter portal;
        Material red, blue;
        Object previousSelection;

        [SetUp]
        public void SetUp()
        {
            previousSelection = Selection.activeObject;
            scene = EditorSceneManager.NewPreviewScene();
            var go = New("Portal snapshot test");
            go.AddComponent<MeshFilter>().sharedMesh = Resources.Load<Mesh>("CreatorConveniences/Models/TeleporterPortal");
            go.AddComponent<MeshRenderer>().sharedMaterial = Resources.Load<Material>("CreatorConveniences/Materials/TeleporterPortal");
            portal = go.AddComponent<BSTeleporter>();
            portal.portalPreviewEnabled = true;
            portal.DestinationPosition = Vector3.forward * 5f;
            red = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = Color.red };
            blue = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = Color.blue };
            Wall(new Vector3(0f, 1.6f, 8f), red);
            Wall(new Vector3(3f, 1.6f, 5f), blue);
        }

        [TearDown]
        public void TearDown()
        {
            Selection.activeObject = previousSelection;
            if (portal != null) portal.ReleaseEditorPortalPreview();
            EditorSceneManager.ClosePreviewScene(scene);
            Object.DestroyImmediate(red);
            Object.DestroyImmediate(blue);
        }

        [UnityTest]
        public IEnumerator Snapshot_CapturesExitFacing_Throttles_AndPreservesRendererProperties()
        {
            var renderer = portal.GetComponent<MeshRenderer>();
            var material = renderer.sharedMaterial;
            var materialImage = material.GetTexture("_MainTex");
            var properties = new MaterialPropertyBlock();
            properties.SetFloat("_Unrelated", 7f);
            renderer.SetPropertyBlock(properties);
            renderer.forceRenderingOff = true;
            var previewScenes = EditorSceneManager.previewSceneCount;
            portal.UpdateEditorPortalPreview();
            renderer.GetPropertyBlock(properties);
            var image = (RenderTexture)properties.GetTexture("_MainTex");
            Assert.That(image, Is.Not.Null);
            Assert.That(image.width, Is.EqualTo(256));
            Assert.That(properties.GetFloat("_HasImage"), Is.EqualTo(1f));
            Assert.That(Center(image).r, Is.GreaterThan(.8f), "The camera renders the destination world, not its empty temporary scene.");
            Assert.That(renderer.forceRenderingOff, Is.True);
            Assert.That(EditorSceneManager.previewSceneCount, Is.EqualTo(previewScenes));
            Assert.That(material.GetTexture("_MainTex"), Is.SameAs(materialImage));
            portal.DestinationYaw = 90f;
            portal.UpdateEditorPortalPreview();
            Assert.That(Center(image).r, Is.GreaterThan(.8f), "Updates are limited to two captures per second.");
            var deadline = Time.realtimeSinceStartupAsDouble + .55;
            while (Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            portal.UpdateEditorPortalPreview();
            Assert.That(Center(image).b, Is.GreaterThan(.8f), "Exit yaw changes which part of the destination is photographed.");
            Save(image, "teleporter-destination.png");
            renderer.GetPropertyBlock(properties);
            properties.SetFloat("_Unrelated", 19f);
            renderer.SetPropertyBlock(properties);
            portal.ReleaseEditorPortalPreview();
            renderer.GetPropertyBlock(properties);
            Assert.That(properties.GetFloat("_Unrelated"), Is.EqualTo(19f), "Release preserves other scripts' intervening property edits.");
            Assert.That(properties.GetFloat("_HasImage"), Is.EqualTo(0f));
            Assert.That(image == null, Is.True, "Owned GPU texture is destroyed on release.");
            Assert.That(renderer.forceRenderingOff, Is.True);
        }

        [Test]
        public void Snapshot_StereoAtlasHasCorrectEyeDisparity_AndReturnsToMono()
        {
            var renderer = portal.GetComponent<MeshRenderer>();
            var properties = new MaterialPropertyBlock();
            properties.SetFloat("_StereoImage", .25f);
            properties.SetFloat("_Unrelated", 7f);
            renderer.SetPropertyBlock(properties);
            foreach (var root in scene.GetRootGameObjects())
            {
                var wall = root.GetComponent<MeshRenderer>();
                if (wall == null || wall.sharedMaterial != red) continue;
                root.transform.position = new Vector3(0f, 1.6f, 15f);
                root.transform.localScale = new Vector3(20f, 20f, .1f);
            }
            var foreground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            SceneManager.MoveGameObjectToScene(foreground, scene);
            foreground.transform.position = new Vector3(0f, 1.6f, 6.05f);
            foreground.transform.localScale = new Vector3(.2f, .6f, .1f);
            foreground.GetComponent<Renderer>().sharedMaterial = blue;

            portal.CapturePortalImage(portal.DestinationPose, .12f);
            renderer.GetPropertyBlock(properties);
            var stereo = (RenderTexture)properties.GetTexture("_MainTex");
            Assert.That(stereo, Is.Not.Null);
            Assert.That(stereo.width, Is.EqualTo(512));
            Assert.That(stereo.height, Is.EqualTo(256));
            Assert.That(properties.GetFloat("_StereoImage"), Is.EqualTo(1f));
            var left = BlueCenter(stereo, 0);
            var right = BlueCenter(stereo, 256);
            Assert.That(left, Is.GreaterThan(132f), "Left eye occupies the first half; a centered nearby object appears to its right.");
            Assert.That(right, Is.LessThan(123f), "Right eye occupies the second half; the same object appears to its left.");
            Assert.That(left - right, Is.GreaterThan(10f), "A near object has crossed disparity in front of the portal plane.");
            Assert.That(Sample(stereo, 32, 128).r, Is.GreaterThan(.8f));
            Assert.That(Sample(stereo, 288, 128).r, Is.GreaterThan(.8f));
            Save(stereo, "teleporter-stereo-destination.png");

            var convergence = portal.GetComponent<MeshFilter>().sharedMesh.bounds.extents.x / Mathf.Tan(30f * Mathf.Deg2Rad);
            foreground.transform.position = new Vector3(0f, 1.6f, 5f + convergence + .05f);
            foreground.transform.localScale = new Vector3(.2f * convergence, .6f * convergence, .1f);
            portal.CapturePortalImage(portal.DestinationPose, .12f);
            Assert.That(BlueCenter(stereo, 0) - BlueCenter(stereo, 256), Is.EqualTo(0f).Within(1.5f), "An object on the portal plane has zero disparity.");
            foreground.transform.position = new Vector3(0f, 1.6f, 10.05f);
            foreground.transform.localScale = new Vector3(1f, 3f, .1f);
            portal.CapturePortalImage(portal.DestinationPose, .12f);
            Assert.That(BlueCenter(stereo, 0) - BlueCenter(stereo, 256), Is.LessThan(-6f), "A far object has uncrossed disparity behind the portal plane.");

            portal.CapturePortalImage(portal.DestinationPose, 0f);
            renderer.GetPropertyBlock(properties);
            var mono = (RenderTexture)properties.GetTexture("_MainTex");
            Assert.That(mono.width, Is.EqualTo(256));
            Assert.That(mono.height, Is.EqualTo(256));
            Assert.That(stereo == null, Is.True, "Changing capture layout releases the old GPU atlas.");
            Assert.That(properties.GetFloat("_StereoImage"), Is.EqualTo(0f));
            Assert.That(BlueCenter(mono, 0), Is.EqualTo(127.5f).Within(2f));
            properties.SetFloat("_Unrelated", 19f);
            renderer.SetPropertyBlock(properties);
            portal.ReleaseEditorPortalPreview();
            renderer.GetPropertyBlock(properties);
            Assert.That(mono == null, Is.True);
            Assert.That(properties.GetFloat("_StereoImage"), Is.EqualTo(.25f), "Release restores the original stereo property across texture resizing.");
            Assert.That(properties.GetFloat("_Unrelated"), Is.EqualTo(19f));
        }

        [Test]
        public void Snapshot_ShaderDrawsSameImageFromBothSides()
        {
            portal.UpdateEditorPortalPreview();
            var camera = New("Portal inspection camera").AddComponent<Camera>();
            camera.scene = scene;
            camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.orthographic = true;
            camera.orthographicSize = 1.35f;
            var target = new RenderTexture(256, 256, 24);
            try
            {
                camera.targetTexture = target;
                foreach (var z in new[] { -3f, 3f })
                {
                    camera.transform.SetPositionAndRotation(new Vector3(0f, 1.6f, z), Quaternion.LookRotation(Vector3.forward * -Mathf.Sign(z)));
                    camera.Render();
                    Assert.That(Center(target).r, Is.GreaterThan(.7f), "Photo is visible from both sides.");
                    Save(target, z < 0 ? "teleporter-front.png" : "teleporter-back.png");
                }
            }
            finally { target.Release(); Object.DestroyImmediate(target); }
        }

        [UnityTest]
        public IEnumerator EditorSelection_ReleasesCachedTextureAfterDeletingPortal()
        {
            Selection.activeGameObject = portal.gameObject;
            yield return null;
            yield return null;
            var properties = new MaterialPropertyBlock();
            portal.GetComponent<MeshRenderer>().GetPropertyBlock(properties);
            var image = properties.GetTexture("_MainTex");
            Assert.That(image, Is.Not.Null);
            Object.DestroyImmediate(portal.gameObject);
            portal = null;
            Selection.activeObject = null;
            var deadline = Time.realtimeSinceStartupAsDouble + 1;
            while (image != null && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            Assert.That(image == null, Is.True, "The editor registry releases textures from destroyed components on its next update.");
        }

        [TestCase(-3f)]
        [TestCase(3f)]
        public void Snapshot_MatchesDestinationLeftAndRightOnTheAuthoredMesh(float approachZ)
        {
            var photo = new Texture2D(2, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            photo.SetPixels(new[] { Color.red, Color.blue });
            photo.Apply();
            var properties = new MaterialPropertyBlock();
            properties.SetTexture("_MainTex", photo);
            properties.SetFloat("_HasImage", 1f);
            portal.GetComponent<MeshRenderer>().SetPropertyBlock(properties);
            var camera = New("Portal orientation camera").AddComponent<Camera>();
            camera.scene = scene;
            camera.enabled = false;
            camera.transform.SetPositionAndRotation(new Vector3(0f, 1.6f, approachZ), Quaternion.LookRotation(Vector3.forward * -Mathf.Sign(approachZ)));
            camera.orthographic = true;
            camera.orthographicSize = 1.35f;
            var target = new RenderTexture(256, 256, 24);
            try
            {
                camera.targetTexture = target;
                camera.Render();
                var left = Sample(target, 64, 128);
                var right = Sample(target, 192, 128);
                Assert.That(left.r - left.b, Is.GreaterThan(.7f), "Photo left remains on the player's left from either approach direction.");
                Assert.That(right.b - right.r, Is.GreaterThan(.7f), "The imported FBX UVs must not mirror the destination image.");
            }
            finally { target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(photo); }
        }

        [Test]
        public void DestinationOverride_WinsAndCanBeSwappedOrClearedAtRuntime()
        {
            portal.transform.SetPositionAndRotation(new Vector3(2f, 1f, 3f), Quaternion.Euler(0f, 45f, 0f));
            var destination = New("Authored override").transform;
            destination.SetPositionAndRotation(new Vector3(9f, 2f, -4f), Quaternion.Euler(20f, 170f, 30f));
            portal.Destination = destination;
            portal.DestinationPosition = new Vector3(0f, 0f, 7f);
            portal.DestinationYaw = 30f;
            Assert.That(portal.DestinationPose.position, Is.EqualTo(destination.position));
            Assert.That(Quaternion.Angle(portal.DestinationPose.rotation, Quaternion.Euler(0f, 170f, 0f)), Is.LessThan(.01f));
            portal.Destination = null;
            Assert.That(portal.HasDestination, Is.True);
            Assert.That(portal.DestinationPose.position, Is.EqualTo(portal.transform.TransformPoint(new Vector3(0f, 0f, 7f))));
            Assert.That(Quaternion.Angle(portal.DestinationPose.rotation, Quaternion.Euler(0f, 75f, 0f)), Is.LessThan(.01f));
            portal.UseDestinationPosition = false;
            Assert.That(portal.HasDestination, Is.False, "Legacy null destination remains disabled until a local landing is set.");
        }

        GameObject New(string name)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            return go;
        }

        void Wall(Vector3 position, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.position = position;
            go.transform.localScale = new Vector3(2f, 3f, 2f);
            go.GetComponent<Renderer>().sharedMaterial = material;
        }

        static Color Center(RenderTexture target) => Sample(target, target.width / 2, target.height / 2);

        static float BlueCenter(RenderTexture target, int eyeStart)
        {
            var previous = RenderTexture.active;
            var image = new Texture2D(256, 1, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = target;
                image.ReadPixels(new Rect(eyeStart, target.height / 2, 256, 1), 0, 0);
                image.Apply();
                var row = image.GetPixels32();
                var count = 0;
                var sum = 0f;
                for (var x = 0; x < row.Length; x++)
                    if (row[x].b > 150 && row[x].b > row[x].r + 80) { sum += x; count++; }
                Assert.That(count, Is.GreaterThan(20), "Each eye must actually render the nearby blue object.");
                return sum / count;
            }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(image); }
        }

        static Color Sample(RenderTexture target, int x, int y)
        {
            var previous = RenderTexture.active;
            var image = new Texture2D(1, 1, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = target;
                image.ReadPixels(new Rect(x, y, 1, 1), 0, 0);
                image.Apply();
                return image.GetPixel(0, 0);
            }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(image); }
        }

        static void Save(RenderTexture target, string name)
        {
            var previous = RenderTexture.active;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                image.Apply();
                var directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../TestArtifacts"));
                Directory.CreateDirectory(directory);
                File.WriteAllBytes(Path.Combine(directory, name), image.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(image); }
        }
    }
}
