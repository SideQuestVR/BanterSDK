using NUnit.Framework;

namespace BS.SDKEditor.Tests
{
    /// <summary>Only the SideQuest CDN understands the resize parameter, so it must never go on anyone else's url.</summary>
    public class PortalImageTests
    {
        const string Cdn = "https://cdn.sidequestvr.com/file/567302/portalplaceholder_7.png";

        [Test]
        public void TheCdn_GetsTheSize() => Assert.AreEqual(Cdn + "?size=512", BSPortal.Sized(Cdn, 512));

        [Test]
        public void TheCdn_KeepsAQueryItAlreadyHas() => Assert.AreEqual(Cdn + "?v=2&size=512", BSPortal.Sized(Cdn + "?v=2", 512));

        [Test]
        public void ASizeAlreadyThere_IsLeftAlone() => Assert.AreEqual(Cdn + "?size=64", BSPortal.Sized(Cdn + "?size=64", 512));

        [TestCase("https://example.com/world.png")]
        [TestCase("https://cdn.sidequestvr.com.example.com/world.png")]
        [TestCase("not a url")]
        [TestCase("")]
        public void AnythingElse_IsLeftAlone(string url) => Assert.AreEqual(url, BSPortal.Sized(url, 512));
    }
}
