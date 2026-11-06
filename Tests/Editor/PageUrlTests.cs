using NUnit.Framework;

namespace BS.SDKEditor.Tests
{
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
    }
}
