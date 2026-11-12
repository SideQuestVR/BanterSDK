using NUnit.Framework;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// Which urls a world's browser may load (BSBrowser.UrlToLoad): web pages and host names only, and what is sent.
    /// A local file would let a page read the app's files (the session token among them) back with a runscript
    /// action. The url is read as URL parsing reads it, so control characters cannot hide a scheme, and a host name is
    /// sent as http:// so no host has to guess whether "x:0..." is a scheme or a port.
    /// </summary>
    public class BrowserUrlPolicyTests
    {
        [TestCase("https://urpfire.worldspace.host/", "https://urpfire.worldspace.host/")]
        [TestCase("http://localhost:42068/index.html", "http://localhost:42068/index.html")]
        [TestCase("HTTPS://www.youtube.com/watch?v=x", "https://www.youtube.com/watch?v=x")]
        [TestCase("HtTp://A.example/", "http://A.example/")]
        [TestCase("http:example.com", "http:example.com")]
        [TestCase("https:\\\\a.example\\x", "https:\\\\a.example\\x")]
        [TestCase("about:blank", "about:blank")]
        [TestCase(" https://a.example/ ", "https://a.example/")]
        // Host names: sent as http://.
        [TestCase("www.youtube.com/watch?v=x", "http://www.youtube.com/watch?v=x")]
        [TestCase("youtube.com", "http://youtube.com")]
        [TestCase("localhost:8080/x", "http://localhost:8080/x")]
        [TestCase("localhost:8080", "http://localhost:8080")]
        [TestCase("LOCALHOST:8080?x=1", "http://LOCALHOST:8080?x=1")]
        [TestCase("www.example.com:443/x", "http://www.example.com:443/x")]
        [TestCase("www.example.com:443#top", "http://www.example.com:443#top")]
        [TestCase("192.168.1.5:8080/x", "http://192.168.1.5:8080/x")]
        [TestCase("[::1]:8080/x", "http://[::1]:8080/x")]
        public void WebPagesAndHostNamesAreLoaded(string url, string sent)
        {
            Assert.AreEqual(sent, BSBrowser.UrlToLoad(url), url);
        }

        [TestCase("file:///C:/Users/me/AppData/LocalLow/SideQuest/AltspaceVR/SideQuest/sq-session.json")]
        [TestCase("file:///storage/emulated/0/Android/data/com.SideQuest.AltspaceVR/files/SideQuest/sq-session.json")]
        [TestCase("FILE:///C:/x")]
        [TestCase("file:C:/x")]
        [TestCase("file://evil.example/share/StreamingAssets/OraDefault/x.html")]
        [TestCase("file:///android_asset/OraDefault/index.html")]
        [TestCase(" file:///C:/Users/me/sq-session.json")]
        [TestCase("javascript:alert(1)")]
        [TestCase("data:text/html,<script>1</script>")]
        [TestCase("blob:https://a.example/uuid")]
        [TestCase("filesystem:https://a.example/temporary/x")]
        [TestCase("content://com.example.provider/x")]
        [TestCase("intent://x#Intent;scheme=http;end")]
        [TestCase("chrome://gpu")]
        [TestCase("devtools://devtools/bundled/inspector.html")]
        [TestCase("view-source:https://a.example/")]
        [TestCase("app://local/C:/Game/Game_Data/StreamingAssets/OraDefault/index.html")]
        [TestCase("about:srcdoc")]
        [TestCase("about:blank#x")]
        [TestCase("mailto:a@b.example")]
        [TestCase("localhost:")]
        // A digit after the colon is no port unless what precedes it looks like a host (WHATWG reads these as schemes).
        [TestCase("file:0/../data/data/com.SideQuest.AltspaceVR/files/x")]
        [TestCase("file:8080")]
        [TestCase("javascript:0;alert(1)")]
        [TestCase("JaVaScRiPt:0,alert(1)")]
        [TestCase("javascript:1/alert(1)")]
        [TestCase("data:0,x")]
        [TestCase("content:0@com.x/y")]
        [TestCase("intent:0#Intent;end")]
        [TestCase("c:0/x")]
        [TestCase("a:0")]
        [TestCase("www.example.com:443x")]
        [TestCase("localhost:80a/x")]
        public void EverythingElseIsRefused(string url)
        {
            Assert.IsNull(BSBrowser.UrlToLoad(url), url);
        }

        // Not TestCases: control characters would end up in the test names and the results XML.
        [Test]
        public void ControlCharactersCannotHideAScheme()
        {
            // What URL parsing drops: leading C0 controls and spaces, tabs and line breaks anywhere.
            foreach (var url in new[]
            {
                "\u0001file:///C:/Users/me/sq-session.json",
                " \u0000\u001ffile:///C:/Users/me/sq-session.json",
                "fi\tle:///C:/Users/me/sq-session.json",
                "f\ri\nle:///C:/Users/me/sq-session.json",
                "\u0001javascript:alert(1)",
                "java\tscript:alert(1)",
                "java\tscript:0;alert(1)",
                "\u0001data:text/html,x",
            })
                Assert.IsNull(BSBrowser.UrlToLoad(url), BSBrowser.ShownUrl(url));
            Assert.AreEqual("https://a.example/", BSBrowser.UrlToLoad("\u0001ht\ttps://a.example/\r\n"));
        }

        [Test]
        public void WhatIsSentIsAlwaysAWebUrl()
        {
            // Whatever comes in, what goes out starts with http:// or https://, or is about:blank: nothing a host could
            // read as another scheme.
            foreach (var url in new[]
            {
                "www.youtube.com/x", "localhost:8080", "x.y:1/z", "//a.example/x", "\\\\server\\share\\x", ":8080",
                "\u00a0file:///C:/x", "file\uff1a///C:/x", "not a url", "HTTP://A/", "about:blank", "ABOUT:BLANK",
            })
            {
                var sent = BSBrowser.UrlToLoad(url);
                Assert.IsTrue(sent == null || sent.StartsWith("http://", System.StringComparison.OrdinalIgnoreCase)
                    || sent.StartsWith("https://", System.StringComparison.OrdinalIgnoreCase)
                    || sent.Equals("about:blank", System.StringComparison.OrdinalIgnoreCase), BSBrowser.ShownUrl(url) + " -> " + sent);
            }
        }

        [Test]
        public void NormalizeUrl_DropsWhatUrlParsingDrops()
        {
            Assert.AreEqual("https://a.example/x y", BSBrowser.NormalizeUrl(" \u0001https://a.\texample/x y\r\n\u0000 "));
            Assert.AreEqual("file:///x", BSBrowser.NormalizeUrl("\u0001fi\tle:///x"));
            // Only ASCII controls and space: a no-break space is part of the url (and then no scheme at all).
            Assert.AreEqual("\u00a0file:///x", BSBrowser.NormalizeUrl("\u00a0file:///x"));
            Assert.AreEqual("", BSBrowser.NormalizeUrl(" \t\u0001 "));
            Assert.AreEqual("", BSBrowser.NormalizeUrl(null));
        }

        [Test]
        public void EmptyUrlsLoadNothing()
        {
            Assert.IsNull(BSBrowser.UrlToLoad(""));
            Assert.IsNull(BSBrowser.UrlToLoad(" \t "));
            Assert.IsNull(BSBrowser.UrlToLoad(null));
        }

        [Test]
        public void ShownUrl_IsCappedAndOnOneLine()
        {
            Assert.AreEqual("fi?le:///x?[BS] fake", BSBrowser.ShownUrl("fi\tle:///x\n[BS] fake"));
            Assert.AreEqual(new string('x', 120) + "...", BSBrowser.ShownUrl(new string('x', 200)));
        }
    }
}
