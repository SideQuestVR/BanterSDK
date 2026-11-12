using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// BSGLTF destroys what its import made, each asset once, and nothing else: glTF primitives share a material
    /// (destroying it a second time was the teardown NRE), and objects a page parents under the model may use
    /// BSMaterial's scene-wide materials. A model's URL resolves on the page as a browser's would, and an empty or
    /// unusable one never reaches UnityWebRequest (its "Malformed URL" error).
    /// </summary>
    public class GltfTeardownAndUrlTests
    {
        const string Page = "https://example.worldspace.host/space/index.html";

        [TestCase("https://cdn.example.com/a.glb", "https://cdn.example.com/a.glb")]
        [TestCase("  https://cdn.example.com/a.glb ", "https://cdn.example.com/a.glb")]
        [TestCase("models/a.glb", "https://example.worldspace.host/space/models/a.glb")]
        [TestCase("a.glb", "https://example.worldspace.host/space/a.glb")]
        [TestCase("../a.glb", "https://example.worldspace.host/a.glb")]
        [TestCase("/a.glb", "https://example.worldspace.host/a.glb")]
        [TestCase("//cdn.example.com/a.glb", "https://cdn.example.com/a.glb")]
        [TestCase("models/my model.glb", "https://example.worldspace.host/space/models/my%20model.glb")]
        // No scheme but a host and a path: UnityWebRequest always loaded that over https, so it still does.
        [TestCase("cdn.example.com/a.glb", "https://cdn.example.com/a.glb")]
        public void Urls_ResolveLikeABrowser(string url, string expected)
        {
            Assert.IsTrue(BSGLTF.TryResolveUrl(url, Page, false, out var absolute));
            Assert.AreEqual(expected, absolute);
        }

        // Uri.AbsoluteUri would turn %7E into ~ and drop "/%2e%2e/", and a signed URL's signature covers the path as
        // written.
        [TestCase("https://cdn.example.com/a%7Eb.glb?X-Amz-Signature=a%2Fb%3D")]
        [TestCase("https://cdn.example.com/a/%2e%2e/b.glb")]
        public void AbsoluteUrls_AreKeptAsWritten(string url)
        {
            Assert.IsTrue(BSGLTF.TryResolveUrl(url, Page, false, out var absolute));
            Assert.AreEqual(url, absolute);
        }

        [Test]
        public void ProtocolRelativeUrls_TakeAnHttpPagesScheme_AndHttpsOtherwise()
        {
            Assert.IsTrue(BSGLTF.TryResolveUrl("//cdn.example.com/a.glb", "http://example.worldspace.host/", false, out var onHttp));
            Assert.AreEqual("http://cdn.example.com/a.glb", onHttp);
            Assert.IsTrue(BSGLTF.TryResolveUrl("//cdn.example.com/a.glb", null, false, out var withoutPage));
            Assert.AreEqual("https://cdn.example.com/a.glb", withoutPage);
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase("javascript:alert(1)")]
        [TestCase("data:model/gltf-binary;base64,AAAA")]
        [TestCase("ftp://x/a.glb")]
        public void UnusableUrls_AreRejected(string url) => Assert.IsFalse(BSGLTF.TryResolveUrl(url, Page, false, out _));

        [Test]
        public void NullUrl_AndPathsWithoutAPage_AreRejected()
        {
            Assert.IsFalse(BSGLTF.TryResolveUrl(null, Page, false, out _));
            Assert.IsFalse(BSGLTF.TryResolveUrl("models/a.glb", null, false, out _));
            Assert.IsFalse(BSGLTF.TryResolveUrl("models/a.glb", "", false, out _));
        }

        [TestCase("file:///C:/models/a.glb")]
        [TestCase(@"C:\models\a.glb")]
        public void LocalFiles_OnlyFromAFilePage_OrWhereAllowed(string url)
        {
            Assert.IsFalse(BSGLTF.TryResolveUrl(url, Page, false, out _), "a page on the web can't read the player's files");
            Assert.IsTrue(BSGLTF.TryResolveUrl(url, Page, true, out var allowed), "the editor can");
            Assert.AreEqual(url, allowed);
            Assert.IsTrue(BSGLTF.TryResolveUrl(url, "file:///C:/space/index.html", false, out _), "so can a file: page");
        }

        [Test]
        public void RelativeUrls_OnAFilePage_AreFiles()
        {
            Assert.IsTrue(BSGLTF.TryResolveUrl("models/a.glb", "file:///C:/space/index.html", false, out var absolute));
            Assert.AreEqual("file:///C:/space/models/a.glb", absolute);
        }

        [TestCase("https://cdn.example.com/models/chair.glb?v=2", "chair")]
        [TestCase("https://cdn.example.com/my%20chair.glb", "my chair")]
        [TestCase("https://cdn.example.com/a|b<c>.glb", "a|b<c>")] // Path.GetFileNameWithoutExtension threw on these
        [TestCase("https://cdn.example.com/", "GLTF")]
        [TestCase(@"C:\models\chair.glb", "chair")]
        public void ModelName_IsTheFileNameInThePath(string url, string expected) => Assert.AreEqual(expected, BSGLTF.ModelName(url));

        [Test]
        public void SharedMaterialIsDestroyedOnce_CreatorAssetsAreNotTouched()
        {
            var root = new GameObject("model");
            var creatorMaterial = NewMaterial();
            try
            {
                var texture = new Texture2D(2, 2);
                var mesh = new Mesh();
                var shared = NewMaterial();
                shared.mainTexture = texture;
                for (var i = 0; i < 2; i++) // two primitives sharing one material and mesh, as glTF does
                {
                    var part = new GameObject("primitive" + i, typeof(MeshFilter), typeof(MeshRenderer));
                    part.transform.SetParent(root.transform);
                    part.GetComponent<MeshFilter>().sharedMesh = mesh;
                    part.GetComponent<MeshRenderer>().sharedMaterial = shared;
                }
                var assets = new HashSet<Object>();
                BSGLTF.CollectImportedAssets(root, assets);
                Assert.AreEqual(3, assets.Count, "mesh, material and texture, once each");

                var creator = new GameObject("creator child", typeof(MeshRenderer)); // parented by the page later
                creator.transform.SetParent(root.transform);
                creator.GetComponent<MeshRenderer>().sharedMaterial = creatorMaterial;
                Assert.DoesNotThrow(() => BSGLTF.DestroyAssets(assets));
                Assert.IsTrue(shared == null && texture == null && mesh == null, "the import's assets are gone");
                Assert.IsTrue(creatorMaterial != null, "the creator's material is untouched");
                Assert.AreEqual(0, assets.Count);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(creatorMaterial);
            }
        }

        [Test]
        public void Collect_FindsInactiveChildren_SkinnedMeshes_AndEveryMaterialSlot()
        {
            var root = new GameObject("model");
            var mesh = new Mesh();
            var first = NewMaterial();
            var second = NewMaterial();
            try
            {
                var skinnedObject = new GameObject("skinned", typeof(SkinnedMeshRenderer));
                skinnedObject.transform.SetParent(root.transform);
                skinnedObject.SetActive(false);
                var skinned = skinnedObject.GetComponent<SkinnedMeshRenderer>();
                skinned.sharedMesh = mesh;
                skinned.sharedMaterials = new[] { first, second, null };

                var assets = new HashSet<Object>();
                BSGLTF.CollectImportedAssets(root, assets);
                // Also pins that an unset texture slot reads null rather than a shared default texture,
                // which teardown would otherwise destroy.
                CollectionAssert.AreEquivalent(new Object[] { mesh, first, second }, assets);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
            }
        }

        [Test]
        public void DestroyAssets_SkipsWhatIsAlreadyGone()
        {
            var mesh = new Mesh();
            var assets = new HashSet<Object> { mesh };
            Object.DestroyImmediate(mesh);
            Assert.DoesNotThrow(() => BSGLTF.DestroyAssets(assets));
            Assert.AreEqual(0, assets.Count);
        }

        [Test]
        public void Bytes_RejectsAnEmptyUrl_BeforeAnyRequest()
        {
            var download = Get.Bytes(" ");
            Assert.IsTrue(download.IsFaulted);
            Assert.IsInstanceOf<ArgumentException>(download.Exception.InnerException);
        }

        static Material NewMaterial()
        {
            var shader = Shader.Find("Sprites/Default");
            Assert.IsNotNull(shader, "Sprites/Default is built in");
            return new Material(shader);
        }
    }
}
