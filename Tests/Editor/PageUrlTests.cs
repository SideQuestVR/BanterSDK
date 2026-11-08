using NUnit.Framework;

namespace BS.SDKEditor.Tests
{
    /// <summary>What a page or world may have the app fetch.</summary>
    public class PageUrlTests
    {
        [TestCase("https://cdn.example/a.png", true)]
        [TestCase("http://cdn.example/a.mp3", true)]
        [TestCase("HTTPS://cdn.example/a.png", true)]
        [TestCase("file:///c:/a.png", false)]
        [TestCase("jar:file:///a.apk!/a.png", false)]
        [TestCase("ftp://cdn.example/a.png", false)]
        [TestCase("a.png", false)]
        [TestCase("/a.png", false)]
        [TestCase("", false)]
        public void OnlyWebAddressesLoad(string url, bool loads) => Assert.AreEqual(loads, PageUrl.MayLoad(url, false));

        [Test]
        public void TheEditorTakesLocalFilesForTesting() => Assert.IsTrue(PageUrl.MayLoad("file:///c:/a.png", true));

        [TestCase("https://cdn.example/a.apk", true)]
        [TestCase("https://cdn.example/A.APK", true)]
        [TestCase("file:///sdcard/a.apk", false)]
        [TestCase("a.apk", false)]
        [TestCase("https://cdn.example/a.zip", false)]
        public void OnlyAWebAddressCountsAsADirectApk(string url, bool direct) =>
            Assert.AreEqual(direct, SideQuestUrlResolver.IsDirectApkUrl(url));

        // ------------------------------------------------------------------------------------- as a player's build decides

        const string PublicWorld = "https://world.example/";

        static string InBuild(string url, string world = PublicWorld) => PageUrl.Problem(url, world, editor: false);

        [TestCase("https://example.com/song.mp3")]
        [TestCase("http://cdn.example.org/live/master.m3u8")]
        [TestCase("https://172.32.0.1/")]
        [TestCase("https://192.169.0.1/")]
        [TestCase("https://11.0.0.1/")]
        [TestCase("https://[2001:db8::1]/")]
        public void AnAddressOnTheInternetLoads(string url) => Assert.IsNull(InBuild(url));

        [TestCase("http://localhost/")]
        [TestCase("http://localhost:8080/a.png")]
        [TestCase("http://app.localhost/")]
        [TestCase("http://printer.local/")]
        [TestCase("http://127.0.0.1/")]
        [TestCase("http://127.8.9.10/")]
        [TestCase("http://10.0.0.5/")]
        [TestCase("http://172.16.0.1/")]
        [TestCase("http://172.31.255.255/")]
        [TestCase("http://192.168.1.1/")]
        [TestCase("http://169.254.169.254/")]
        [TestCase("http://0.0.0.0/")]
        [TestCase("http://[::1]/")]
        [TestCase("http://[fe80::1]/")]
        [TestCase("http://[fd12:3456::1]/")]
        [TestCase("http://[::ffff:192.168.1.1]/")]
        [TestCase("http://2130706433/")]
        [TestCase("http://LOCALHOST./")]
        public void AnAddressOnThePlayersOwnMachineOrNetworkDoesNot(string url) => Assert.AreEqual(PageUrl.OwnNetworkOnly, InBuild(url), url);

        [Test]
        public void AWorldServedFromThePlayersNetworkMayUseIt() =>
            Assert.IsNull(InBuild("http://192.168.1.5/model.glb", "http://192.168.1.2:8000/world.html"));

        [Test]
        public void TheEditorLoadsEverythingForTesting() => Assert.IsNull(PageUrl.Problem("http://192.168.1.5/model.glb", PublicWorld, editor: true));

        [TestCase("file:///C:/audio/song.wav")]
        [TestCase("ftp://example.com/song.mp3")]
        [TestCase("not an address")]
        public void OnlyWebAddressesLoadAtAll(string url) => Assert.AreEqual(BSAssetKey.WebAddressesOnly, InBuild(url));
    }
}
