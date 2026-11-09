using NUnit.Framework;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// Text keeps the builder's font and material when they can draw here, and takes the default font when they can't.
    /// The editor can't make a shader that reports itself unsupported, so that case needs a device.
    /// </summary>
    public class TextFontRepairTests
    {
        GameObject root;
        TMP_FontAsset own;
        Material material;
        Material preset;

        [SetUp]
        public void SetUp()
        {
            if (TMP_Settings.instance == null || TMP_Settings.defaultFontAsset == null)
                Assert.Ignore("This project has no TextMesh Pro default font.");
            root = new GameObject("text");
            own = Object.Instantiate(TMP_Settings.defaultFontAsset);
            material = new Material(TMP_Settings.defaultFontAsset.material);
            own.material = material;
            preset = new Material(material);
        }

        [TearDown]
        public void TearDown()
        {
            if (root != null) Object.DestroyImmediate(root);
            if (preset != null) Object.DestroyImmediate(preset);
            if (own == null) return;
            // A font asset destroys its atlases and its material with it. This copy made the material, and shares the
            // real font's atlases, which aren't the test's to destroy.
            own.atlasTextures = new Texture2D[0];
            Object.DestroyImmediate(own);
        }

        // The copy shares the default font's atlas, so TMP keeps the material the text already had when the font changes:
        // the builder's material has to be set as well, as a world's text has it saved.
        [Test]
        public void AFontThatDraws_IsKept_WithTheBuildersMaterial()
        {
            var text = root.AddComponent<TextMeshPro>();
            text.font = own;
            text.fontSharedMaterial = preset;
            Assert.AreSame(preset, text.fontSharedMaterial);

            BSText.RepairFont(text);

            Assert.AreSame(own, text.font);
            Assert.AreSame(preset, text.fontSharedMaterial);
        }

        [Test]
        public void AFontWithNothingToMakeGlyphsFrom_GetsTheDefault()
        {
            var text = root.AddComponent<TextMeshPro>();
            text.font = own;
            own.atlasPopulationMode = AtlasPopulationMode.DynamicOS;
            BSText.RepairFont(text);
            Assert.AreSame(TMP_Settings.defaultFontAsset, text.font);
            Assert.AreSame(TMP_Settings.defaultFontAsset.material, text.fontSharedMaterial);
        }

        [Test]
        public void NoFontOrMaterial_CannotDraw()
        {
            Assert.IsFalse(BSText.CanDraw(null, material));
            Assert.IsFalse(BSText.CanDraw(own, null));
            Assert.IsTrue(BSText.CanDraw(own, material));
        }

        [Test]
        public void AMaterialThatLostItsShader_CannotDraw()
        {
            var broken = new Material(Shader.Find("Hidden/InternalErrorShader"));
            Assert.IsFalse(BSText.CanDraw(own, broken));
            Object.DestroyImmediate(broken);
        }

        // Bob's world: URP Unlit runs everywhere, so the shader check alone kept it, and the text drew nothing.
        [Test]
        public void AShaderThatIsNotForText_GetsTheDefault()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) Assert.Ignore("This project has no URP Unlit shader.");
            var unlit = new Material(shader);
            var text = root.AddComponent<TextMeshPro>();
            text.font = own;
            text.fontSharedMaterial = unlit;
            Assert.IsFalse(BSText.CanDraw(own, unlit));
            BSText.RepairFont(text);
            Assert.AreSame(TMP_Settings.defaultFontAsset, text.font);
            Assert.AreSame(TMP_Settings.defaultFontAsset.material, text.fontSharedMaterial);
            Object.DestroyImmediate(unlit);
        }

        [Test]
        public void ATextMaterialWithoutThisFontsAtlas_CannotDraw()
        {
            var other = new Texture2D(4, 4);
            preset.SetTexture("_MainTex", other);
            Assert.IsFalse(BSText.CanDraw(own, preset), "another font's atlas");
            preset.SetTexture("_MainTex", null);
            Assert.IsFalse(BSText.CanDraw(own, preset), "no atlas");
            Object.DestroyImmediate(other);
        }

        [Test]
        public void AFontThatLostItsOwnMaterial_GetsTheDefault()
        {
            var text = root.AddComponent<TextMeshPro>();
            text.font = own;
            text.fontSharedMaterial = preset;
            own.material = null;
            Assert.IsFalse(BSText.CanDraw(own, preset), "TMP reads the font's own material on every rebuild");
            BSText.RepairFont(text);
            Assert.AreSame(TMP_Settings.defaultFontAsset, text.font);
        }

        [Test]
        public void FallbacksWithNoMaterial_AreDropped_AndTheFontKept()
        {
            var lost = Object.Instantiate(TMP_Settings.defaultFontAsset);
            lost.atlasTextures = new Texture2D[0];
            lost.material = null;
            try
            {
                own.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset> { lost, null };
                var text = root.AddComponent<TextMeshPro>();
                text.font = own;
                text.fontSharedMaterial = preset;
                BSText.RepairFont(text);
                Assert.AreSame(own, text.font);
                CollectionAssert.IsEmpty(own.fallbackFontAssetTable);
            }
            finally
            {
                Object.DestroyImmediate(lost);
            }
        }

        [Test]
        public void NothingToRepair_IsFine()
        {
            Assert.DoesNotThrow(() => BSText.RepairFont(null));
        }
    }
}
