using BS;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BS.SDKEditor.Tests
{
    public class TeleporterLiveRenderingTests
    {
        static readonly Vector3 RedPosition = new Vector3(-.4f, 1.8f, 2f);
        static readonly Vector3 BluePosition = new Vector3(.4f, 1.4f, 2f);
        Scene scene;
        BSTeleporter portal;
        Camera viewer;
        RenderTexture output;
        Material red, blue, green;

        [SetUp]
        public void SetUp()
        {
            scene = EditorSceneManager.NewPreviewScene();
            var root = New("Perspective portal");
            root.AddComponent<MeshFilter>().sharedMesh = Resources.Load<Mesh>("CreatorConveniences/Models/TeleporterPortal");
            root.AddComponent<MeshRenderer>().sharedMaterial = Resources.Load<Material>("CreatorConveniences/Materials/TeleporterPortal");
            portal = root.AddComponent<BSTeleporter>();
            portal.DestinationPosition = new Vector3(10f, 0f, 0f);
            portal.DestinationYaw = 90f;
            viewer = New("Portal viewer").AddComponent<Camera>();
            viewer.scene = scene;
            viewer.enabled = false;
            viewer.clearFlags = CameraClearFlags.SolidColor;
            viewer.backgroundColor = Color.black;
            viewer.fieldOfView = 60f;
            viewer.aspect = 1f;
            viewer.allowHDR = false;
            viewer.allowMSAA = false;
            viewer.transform.position = new Vector3(0f, 1.6f, -3f);
            output = new RenderTexture(256, 256, 24);
            viewer.targetTexture = output;
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            red = new Material(shader) { color = Color.red };
            blue = new Material(shader) { color = Color.blue };
            green = new Material(shader) { color = Color.green };
            Marker(RedPosition, new Vector3(.25f, .3f, .1f), red);
            Marker(BluePosition, new Vector3(.25f, .3f, .1f), blue);
        }

        [TearDown]
        public void TearDown()
        {
            if (portal != null) portal.ReleaseEditorPortalPreview();
            viewer.targetTexture = null;
            viewer.ResetStereoViewMatrices();
            viewer.ResetStereoProjectionMatrices();
            output.Release();
            Object.DestroyImmediate(output);
            EditorSceneManager.ClosePreviewScene(scene);
            Object.DestroyImmediate(red);
            Object.DestroyImmediate(blue);
            Object.DestroyImmediate(green);
        }

        [Test]
        public void ProjectiveImage_MatchesTheDestinationGeometry_WhileTheViewerMoves()
        {
            var firstBlue = Vector2.zero;
            foreach (var x in new[] { -.4f, .4f })
            {
                viewer.transform.position = new Vector3(x, 1.6f, -3f);
                portal.CapturePortalImage(portal.DestinationPose, 0f, viewer);
                viewer.Render();
                var pixels = Read(output);
                var redCenter = MarkerCenter(pixels, 256, 0);
                var blueCenter = MarkerCenter(pixels, 256, 2);
                Assert.That(Vector2.Distance(redCenter, ExpectedPixel(RedPosition)), Is.LessThan(2.5f), "Red destination geometry must retain its position and vertical orientation through the actual portal shader.");
                Assert.That(Vector2.Distance(blueCenter, ExpectedPixel(BluePosition)), Is.LessThan(2.5f));
                Assert.That(redCenter.x, Is.LessThan(blueCenter.x), "The rotated destination must not mirror left and right.");
                if (x < 0f) firstBlue = blueCenter;
                else Assert.That(firstBlue.x - blueCenter.x, Is.GreaterThan(25f), "The image responds to the viewer's position instead of remaining a fixed photograph.");
            }
            Assert.That(Properties().GetFloat("_PerspectiveImage"), Is.EqualTo(1f));
        }

        [Test]
        public void ExitPlane_ClipsGeometryBetweenTheMappedViewerAndTheDestination()
        {
            Marker(new Vector3(0f, 1.6f, -1f), new Vector3(4f, 4f, .1f), green);
            var position = viewer.transform.position;
            var rotation = viewer.transform.rotation;
            var destination = portal.DestinationPose;
            viewer.transform.SetPositionAndRotation(destination.position + destination.rotation * position, destination.rotation * rotation);
            viewer.Render();
            Assert.That(Count(Read(output), 1), Is.GreaterThan(10000), "The green wall really occludes the ordinary mapped camera before exit clipping.");
            viewer.transform.SetPositionAndRotation(position, rotation);

            portal.CapturePortalImage(destination, 0f, viewer);
            viewer.Render();
            var pixels = Read(output);
            Assert.That(Count(pixels, 1), Is.Zero, "Geometry on the viewer side of the exit must not leak into the portal.");
            Assert.That(Vector2.Distance(MarkerCenter(pixels, 256, 0), ExpectedPixel(RedPosition)), Is.LessThan(2.5f));
            Assert.That(Vector2.Distance(MarkerCenter(pixels, 256, 2), ExpectedPixel(BluePosition)), Is.LessThan(2.5f));
        }

        [Test]
        public void PerspectiveStereo_UsesBothSourceEyeViews_AndCanReturnToMono()
        {
            var projection = viewer.projectionMatrix;
            var leftView = EyeView(-.06f);
            var rightView = EyeView(.06f);
            viewer.SetStereoViewMatrix(Camera.StereoscopicEye.Left, leftView);
            viewer.SetStereoViewMatrix(Camera.StereoscopicEye.Right, rightView);
            viewer.SetStereoProjectionMatrix(Camera.StereoscopicEye.Left, projection);
            viewer.SetStereoProjectionMatrix(Camera.StereoscopicEye.Right, projection);
            portal.CapturePortalImage(portal.DestinationPose, .12f, viewer);
            var properties = Properties();
            var atlas = (RenderTexture)properties.GetTexture("_MainTex");
            Assert.That(atlas.width, Is.EqualTo(512));
            Assert.That(properties.GetFloat("_StereoImage"), Is.EqualTo(1f));
            var pixels = Read(atlas);
            var left = MarkerCenter(pixels, 512, 2, 0);
            var right = MarkerCenter(pixels, 512, 2, 256);
            Assert.That(left.x - right.x, Is.GreaterThan(3f), "Each atlas half must render its own source eye, not duplicate a mono camera.");
            Assert.That(Vector2.Distance(left, ExpectedPixel(BluePosition, leftView, projection)), Is.LessThan(2.5f));
            Assert.That(Vector2.Distance(right, ExpectedPixel(BluePosition, rightView, projection)), Is.LessThan(2.5f));
            Assert.That(properties.GetMatrix("_PortalViewProjectionL"), Is.Not.EqualTo(properties.GetMatrix("_PortalViewProjectionR")));
            portal.CapturePortalImage(portal.DestinationPose, 0f, viewer);
            properties = Properties();
            Assert.That(((RenderTexture)properties.GetTexture("_MainTex")).width, Is.EqualTo(256));
            Assert.That(properties.GetFloat("_PerspectiveImage"), Is.EqualTo(1f));
            Assert.That(properties.GetFloat("_StereoImage"), Is.Zero);
            Assert.That(atlas == null, Is.True);
        }

        Matrix4x4 EyeView(float offset) => Matrix4x4.Scale(new Vector3(1f, 1f, -1f))
            * Matrix4x4.TRS(viewer.transform.position + Vector3.right * offset, viewer.transform.rotation, Vector3.one).inverse;
        Vector2 ExpectedPixel(Vector3 point) => ExpectedPixel(point, viewer.worldToCameraMatrix, viewer.projectionMatrix);
        static Vector2 ExpectedPixel(Vector3 point, Matrix4x4 view, Matrix4x4 projection)
        {
            var clip = projection * view * new Vector4(point.x, point.y, point.z, 1f);
            return new Vector2((clip.x / clip.w * .5f + .5f) * 256f - .5f, (clip.y / clip.w * .5f + .5f) * 256f - .5f);
        }
        MaterialPropertyBlock Properties()
        {
            var properties = new MaterialPropertyBlock();
            portal.GetComponent<MeshRenderer>().GetPropertyBlock(properties);
            return properties;
        }
        void Marker(Vector3 position, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            SceneManager.MoveGameObjectToScene(go, scene);
            var destination = portal.DestinationPose;
            go.transform.SetPositionAndRotation(destination.position + destination.rotation * position, destination.rotation);
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
        }
        GameObject New(string name)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            return go;
        }
        static bool Matches(Color32 pixel, int channel) => channel == 0 ? pixel.r > 150 && pixel.g < 80 && pixel.b < 80
            : channel == 1 ? pixel.g > 150 && pixel.r < 80 && pixel.b < 80 : pixel.b > 150 && pixel.r < 80 && pixel.g < 80;
        static int Count(Color32[] pixels, int channel)
        {
            var count = 0;
            foreach (var pixel in pixels) if (Matches(pixel, channel)) count++;
            return count;
        }
        static Vector2 MarkerCenter(Color32[] pixels, int width, int channel, int start = 0)
        {
            var sum = Vector2.zero;
            var count = 0;
            for (var y = 0; y < 256; y++)
                for (var x = 0; x < 256; x++)
                    if (Matches(pixels[y * width + start + x], channel)) { sum += new Vector2(x, y); count++; }
            Assert.That(count, Is.GreaterThan(20), "The real GPU image must contain the destination marker.");
            return sum / count;
        }
        static Color32[] Read(RenderTexture texture)
        {
            var previous = RenderTexture.active;
            var image = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = texture;
                image.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                image.Apply();
                return image.GetPixels32();
            }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(image); }
        }
    }
}
