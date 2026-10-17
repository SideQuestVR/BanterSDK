using BS;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BS.SDKEditor.Tests
{
    public class GripHologramRenderingTests
    {
        [Test]
        public void Ghost_TransmitsBackground_HidesRearFaces_FadesOut_AndRespectsWorldDepth()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var previous = RenderTexture.active;
            RenderTexture target = null;
            Material backgroundMaterial = null;
            Mesh frontMesh = null, layeredMesh = null;
            try
            {
                var camera = New("Ghost GPU regression camera", scene).AddComponent<Camera>();
                camera.scene = scene;
                camera.transform.position = new Vector3(0f, 0f, -1f);
                camera.orthographic = true;
                camera.orthographicSize = 1f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                target = new RenderTexture(128, 128, 24);
                camera.targetTexture = target;
                var background = GameObject.CreatePrimitive(PrimitiveType.Cube);
                SceneManager.MoveGameObjectToScene(background, scene);
                background.transform.position = Vector3.forward;
                background.transform.localScale = new Vector3(4f, 4f, .01f);
                backgroundMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                backgroundMaterial.color = new Color(.18f, .08f, .04f);
                background.GetComponent<Renderer>().sharedMaterial = backgroundMaterial;
                var ghost = New("Layered ghost shader regression", scene);
                var filter = ghost.AddComponent<MeshFilter>();
                var renderer = ghost.AddComponent<MeshRenderer>();
                Assert.That(BSGripHologram.SharedDepthMaterial, Is.Not.Null);
                Assert.That(BSGripHologram.SharedMaterial, Is.Not.Null);
                renderer.sharedMaterials = new[] { BSGripHologram.SharedDepthMaterial, BSGripHologram.SharedMaterial };
                frontMesh = Quad(false);
                layeredMesh = Quad(true);
                filter.sharedMesh = frontMesh;
                ghost.SetActive(false);
                var backgroundOnly = Capture(camera, target);
                ghost.SetActive(true);
                var front = Capture(camera, target);
                Assert.That(MaxDifference(front, backgroundOnly), Is.GreaterThan(.02f), "The real ghost shader must draw visible color.");
                filter.sharedMesh = layeredMesh;
                var layered = Capture(camera, target);
                Assert.That(MaxDifference(front, layered), Is.LessThan(.015f), "Hidden rear faces must not accumulate ghost color.");
                backgroundMaterial.color = new Color(.04f, .1f, .3f);
                var changed = Capture(camera, target);
                var center = target.height / 2 * target.width + target.width / 2;
                Assert.That(Vector3.Distance(RGB(changed[center]), RGB(layered[center])), Is.GreaterThan(.05f), "The scene remains visible through the full hand.");
                var properties = new MaterialPropertyBlock();
                properties.SetFloat("_Opacity", 0f);
                renderer.SetPropertyBlock(properties);
                var faded = Capture(camera, target);
                ghost.SetActive(false);
                var empty = Capture(camera, target);
                Assert.That(MaxDifference(faded, empty), Is.LessThan(.01f), "Zero opacity matches a scene without the hand.");
                ghost.SetActive(true);
                renderer.SetPropertyBlock(null);
                background.transform.position = new Vector3(0f, 0f, -.1f);
                var occluded = Capture(camera, target);
                ghost.SetActive(false);
                Assert.That(MaxDifference(occluded, Capture(camera, target)), Is.LessThan(.01f), "An opaque object in front blocks the ghost.");
            }
            finally
            {
                RenderTexture.active = previous;
                EditorSceneManager.ClosePreviewScene(scene);
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
                if (backgroundMaterial != null) Object.DestroyImmediate(backgroundMaterial);
                if (frontMesh != null) Object.DestroyImmediate(frontMesh);
                if (layeredMesh != null) Object.DestroyImmediate(layeredMesh);
            }
        }

        static GameObject New(string name, Scene scene)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            return go;
        }

        static Mesh Quad(bool back)
        {
            var mesh = new Mesh();
            var vertices = new Vector3[back ? 8 : 4];
            var normals = new Vector3[vertices.Length];
            var uv = new Vector2[vertices.Length];
            for (var layer = 0; layer < (back ? 2 : 1); layer++)
                for (var i = 0; i < 4; i++)
                {
                    var vertex = layer * 4 + i;
                    vertices[vertex] = new Vector3(i == 1 || i == 2 ? .5f : -.5f, i >= 2 ? .5f : -.5f, layer * .05f);
                    normals[vertex] = Vector3.back;
                    uv[vertex] = Vector2.one * .5f;
                }
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv2 = uv;
            mesh.triangles = back ? new[] { 0, 2, 1, 0, 3, 2, 4, 6, 5, 4, 7, 6 } : new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateBounds();
            return mesh;
        }

        static Color[] Capture(Camera camera, RenderTexture target)
        {
            var previous = RenderTexture.active;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try
            {
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0);
                image.Apply();
                return image.GetPixels();
            }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(image); }
        }

        static Vector3 RGB(Color color) => new Vector3(color.r, color.g, color.b);
        static float MaxDifference(Color[] first, Color[] second)
        {
            var maximum = 0f;
            for (var i = 0; i < first.Length; i++)
                maximum = Mathf.Max(maximum, Mathf.Abs(first[i].r - second[i].r), Mathf.Abs(first[i].g - second[i].g), Mathf.Abs(first[i].b - second[i].b));
            return maximum;
        }
    }
}
