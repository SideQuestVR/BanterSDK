using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// BSMaterials with the same properties draw with one cached material, so SetColor and SetTexture on one of them leave
    /// the others as they are, also while a texture is loading. A texture that never comes leaves the object as it was and
    /// can be asked for again. A creator's own material isn't cached and is written directly. Inactive objects, so Awake
    /// doesn't run.
    /// </summary>
    public class BSMaterialSharedWriteTests
    {
        const string Scheme = "heldtexture";
        const string MainTex = "_MainTex";

        /// <summary>A texture source whose loads finish when the test says so.</summary>
        sealed class HeldSource : IBSTextureSource
        {
            public readonly List<TaskCompletionSource<Texture2D>> Requests = new List<TaskCompletionSource<Texture2D>>();

            public string Scheme => BSMaterialSharedWriteTests.Scheme;

            public Task<Texture2D> Resolve(string path, bool linear, bool mipmaps, Action<Texture2D> preview)
            {
                var request = new TaskCompletionSource<Texture2D>();
                Requests.Add(request);
                return request.Task;
            }
        }

        readonly List<Object> made = new List<Object>();
        HeldSource source;
        SynchronizationContext context;

        [SetUp]
        public void SetUp()
        {
            // With no context a finished load carries on where it finishes, so the next line of a test sees the result.
            context = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);
            source = new HeldSource();
            BSMaterial.RegisterTextureSource(source);
        }

        [TearDown]
        public void TearDown()
        {
            BSMaterial.UnregisterTextureSource(Scheme);
            SynchronizationContext.SetSynchronizationContext(context);
            foreach (var item in made) Object.DestroyImmediate(item);
            made.Clear();
        }

        BSMaterial Make(string name, Vector4 color)
        {
            var material = Bare(name);
            material.Color = color;
            return material;
        }

        BSMaterial Bare(string name)
        {
            var go = new GameObject(name);
            go.SetActive(false);
            made.Add(go);
            go.AddComponent<MeshRenderer>();
            return go.AddComponent<BSMaterial>();
        }

        Texture2D Texture()
        {
            var texture = new Texture2D(1, 1);
            made.Add(texture);
            return texture;
        }

        // A new reference each time: the material cache lasts as long as the domain.
        static string Reference() => Scheme + ":" + Guid.NewGuid().ToString("N");

        static MeshRenderer RendererOf(BSMaterial material) => material.GetComponent<MeshRenderer>();

        static Material Drawn(BSMaterial material) => RendererOf(material).sharedMaterial;

        (BSMaterial first, BSMaterial second, Material shared) Pair(Vector4 look)
        {
            var first = Make("first", look);
            var second = Make("second", look);
            var shared = Drawn(first);
            Assert.IsNotNull(shared);
            Assert.AreSame(shared, Drawn(second));
            return (first, second, shared);
        }

        [Test]
        public void SetColor_LeavesTheSharedMaterialAndOtherObjectsAlone()
        {
            var (first, second, shared) = Pair(new Vector4(0.21f, 0.43f, 0.65f, 1f));
            var before = shared.GetColor("_Color");

            first.SetColor(Color.red);

            Assert.IsTrue(RendererOf(first).HasPropertyBlock());
            Assert.IsFalse(RendererOf(second).HasPropertyBlock());
            Assert.AreSame(shared, Drawn(first));
            Assert.AreEqual(before, shared.GetColor("_Color"));
        }

        [Test]
        public void TheColorProperty_ReplacesAColourFromSetColor()
        {
            var first = Make("first", new Vector4(0.22f, 0.44f, 0.66f, 1f));
            first.SetColor(Color.red);
            first.Color = new Vector4(0f, 1f, 0f, 1f);

            var block = new MaterialPropertyBlock();
            RendererOf(first).GetPropertyBlock(block);
            Assert.AreEqual(Color.green, block.GetColor("_Color"));
        }

        [Test]
        public void SetColor_WritesACreatorsOwnMaterial()
        {
            var own = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            made.Add(own);
            var material = Bare("own");
            RendererOf(material).sharedMaterial = own;
            material.UpdateCallback(new List<PropertyName>());

            material.SetColor(Color.green);

            Assert.IsFalse(RendererOf(material).HasPropertyBlock());
            Assert.AreSame(own, Drawn(material));
            Assert.AreEqual(Color.green, own.GetColor("_BaseColor"));
        }

        [Test]
        public void BeforeARenderer_SetColorDoesNothing()
        {
            var material = Bare("none");
            Assert.DoesNotThrow(() => material.SetColor(Color.red));
        }

        [Test]
        public void SetTexture_MovesThisObjectOnly_OnceTheTextureHasCome()
        {
            var (first, second, shared) = Pair(new Vector4(0.23f, 0.45f, 0.67f, 1f));
            var reference = Reference();
            var texture = Texture();

            var task = first.SetTexture(reference);

            Assert.IsFalse(task.IsCompleted);
            Assert.AreSame(shared, Drawn(first));

            source.Requests[0].SetResult(texture);

            Assert.IsTrue(task.IsCompleted);
            Assert.AreSame(texture, Drawn(first).GetTexture(MainTex));
            Assert.AreEqual(reference, first.Texture);
            Assert.AreSame(shared, Drawn(second));
        }

        [Test]
        public void WhileATextureLoads_SetColorStaysOnThisObject()
        {
            var (first, second, shared) = Pair(new Vector4(0.24f, 0.46f, 0.68f, 1f));
            var before = shared.GetColor("_Color");

            _ = first.SetTexture(Reference());
            first.SetColor(Color.red);

            Assert.AreEqual(before, shared.GetColor("_Color"));
            Assert.IsTrue(RendererOf(first).HasPropertyBlock());
            Assert.IsFalse(RendererOf(second).HasPropertyBlock());
        }

        [Test]
        public void WhileATextureLoads_ASecondSetTextureLeavesTheSharedMaterialAlone_AndTheLatestWins()
        {
            var (first, second, shared) = Pair(new Vector4(0.25f, 0.47f, 0.69f, 1f));
            var before = shared.GetTexture(MainTex);
            var latest = Reference();
            var latestTexture = Texture();

            _ = first.SetTexture(Reference());
            _ = first.SetTexture(latest);

            Assert.AreEqual(2, source.Requests.Count);
            Assert.AreSame(before, shared.GetTexture(MainTex));

            source.Requests[1].SetResult(latestTexture);
            source.Requests[0].SetResult(Texture());

            Assert.AreEqual(latest, first.Texture);
            Assert.AreSame(latestTexture, Drawn(first).GetTexture(MainTex));
            Assert.AreSame(shared, Drawn(second));
            Assert.AreSame(before, shared.GetTexture(MainTex));
        }

        [Test]
        public void TwoObjectsAskingForOneTexture_ShareTheLoad()
        {
            var (first, second, shared) = Pair(new Vector4(0.26f, 0.48f, 0.7f, 1f));
            var reference = Reference();
            var texture = Texture();

            _ = first.SetTexture(reference);
            _ = second.SetTexture(reference);

            Assert.AreEqual(1, source.Requests.Count);
            Assert.AreSame(shared, Drawn(second));

            source.Requests[0].SetResult(texture);

            Assert.AreSame(texture, Drawn(second).GetTexture(MainTex));
            Assert.AreSame(Drawn(first), Drawn(second));
        }

        [Test]
        public void AFailedLoad_LeavesTheObjectsAsTheyWere_AndCanBeTriedAgain()
        {
            var (first, second, shared) = Pair(new Vector4(0.27f, 0.49f, 0.71f, 1f));
            var reference = Reference();
            var texture = Texture();

            var task = first.SetTexture(reference);
            _ = second.SetTexture(reference);
            source.Requests[0].SetResult(null);

            Assert.IsTrue(task.IsCompleted);
            Assert.AreSame(shared, Drawn(first));
            Assert.AreSame(shared, Drawn(second));
            Assert.AreEqual("", first.Texture);

            _ = first.SetTexture(reference);
            Assert.AreEqual(2, source.Requests.Count);
            source.Requests[1].SetResult(texture);

            Assert.AreSame(texture, Drawn(first).GetTexture(MainTex));
            Assert.AreEqual(reference, first.Texture);
        }

        [Test]
        public void AnUnusableReference_LeavesTheObjectAsItWas()
        {
            var first = Make("first", new Vector4(0.28f, 0.5f, 0.72f, 1f));
            var shared = Drawn(first);

            var task = first.SetTexture("no-such-texture");

            Assert.IsTrue(task.IsCompleted);
            Assert.AreSame(shared, Drawn(first));
            Assert.AreEqual("", first.Texture);
        }

        [Test]
        public void APropertyChangedDuringALoad_IsKept()
        {
            var first = Make("first", new Vector4(0.29f, 0.51f, 0.73f, 1f));
            var reference = Reference();

            _ = first.SetTexture(reference);
            first.Side = MaterialSide.Double;
            var afterSide = Drawn(first);
            source.Requests[0].SetResult(Texture());

            Assert.AreEqual(reference, first.Texture);
            Assert.AreEqual(MaterialSide.Double, first.Side);
            Assert.AreNotSame(afterSide, Drawn(first));
        }
    }
}
