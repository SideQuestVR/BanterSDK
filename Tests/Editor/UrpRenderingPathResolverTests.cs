using NUnit.Framework;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// The rendering path a bundle section records, from the light loops its renderers use. The client
    /// only switches to Forward on an explicit "Forward"; everything else renders with Forward+.
    /// </summary>
    public class UrpRenderingPathResolverTests
    {
        [Test]
        public void OnlyClusterRenderersRecordForwardPlus() =>
            Assert.That(UrpRenderingPathResolver.Classify(true, false, true), Is.EqualTo("ForwardPlus"));

        [Test]
        public void NoClusterRenderersRecordForward() =>
            Assert.That(UrpRenderingPathResolver.Classify(false, true, true), Is.EqualTo("Forward"));

        [Test]
        public void RenderersThatDisagreeRecordMixed() =>
            Assert.That(UrpRenderingPathResolver.Classify(true, true, true), Is.EqualTo("Mixed"));

        [Test]
        public void WithoutStrippingBothVariantSetsShipSoItIsMixed()
        {
            Assert.That(UrpRenderingPathResolver.Classify(false, true, false), Is.EqualTo("Mixed"));
            Assert.That(UrpRenderingPathResolver.Classify(true, false, false), Is.EqualTo("Mixed"));
        }

        [Test]
        public void NoRenderersRecordNothing() =>
            Assert.That(UrpRenderingPathResolver.Classify(false, false, true), Is.Null);
    }
}
