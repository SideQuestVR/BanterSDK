using NUnit.Framework;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// VerifyHostCheck's comparison: both sides have to end up spelled the way <c>location.host</c> spells an address,
    /// or a world at its real address would throw its own players out.
    /// </summary>
    public class VerifyHostTests
    {
        [TestCase("https://my-world.worldspace.host", "my-world.worldspace.host")]
        [TestCase("https://my-world.worldspace.host/", "my-world.worldspace.host")]
        [TestCase("https://my-world.worldspace.host/index.html?a=1#b", "my-world.worldspace.host")]
        [TestCase("my-world.worldspace.host", "my-world.worldspace.host")]
        [TestCase("  HTTPS://My-World.WorldSpace.Host/  ", "my-world.worldspace.host")]
        [TestCase("https://example.com:8443/world", "example.com:8443")]
        [TestCase("example.com:8443", "example.com:8443")]
        [TestCase("http://localhost:42068", "localhost:42068")]
        public void HostOf_SpellsAnAddressAsLocationHostDoes(string address, string expected)
        {
            Assert.AreEqual(expected, VerifyHostCheck.HostOf(address));
        }

        [TestCase("https://example.com:443", "example.com")]
        [TestCase("http://example.com:80", "example.com")]
        [TestCase("https://example.com:80", "example.com:80")]
        public void HostOf_LeavesOutOnlyTheSchemesDefaultPort(string address, string expected)
        {
            Assert.AreEqual(expected, VerifyHostCheck.HostOf(address));
        }

        [Test]
        public void HostOf_SpellsAnInternationalHostInPunycode()
        {
            Assert.AreEqual("xn--bcher-kva.example", VerifyHostCheck.HostOf("https://bücher.example"));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("not an address")]
        [TestCase("https://")]
        public void HostOf_IsEmptyForAnythingThatIsNotAnAddress(string address)
        {
            Assert.AreEqual("", VerifyHostCheck.HostOf(address));
        }

        [TestCase("https://my-world.worldspace.host/a?b=1", "https://my-world.worldspace.host/a?b=1")]
        [TestCase("http://example.com", "http://example.com")]
        [TestCase("  my-world.worldspace.host  ", "https://my-world.worldspace.host")]
        public void WorldAddressOf_GivesAnAddressGoToUrlAccepts(string address, string expected)
        {
            Assert.AreEqual(expected, VerifyHostCheck.WorldAddressOf(address));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("not an address")]
        [TestCase("ftp://example.com")]
        public void WorldAddressOf_IsEmptyForWhatGoToUrlWouldRefuse(string address)
        {
            Assert.AreEqual("", VerifyHostCheck.WorldAddressOf(address));
        }

        [TestCase("\"my-world.worldspace.host\"", "my-world.worldspace.host")]
        [TestCase("\"Example.COM:8443\"", "example.com:8443")]
        [TestCase("my-world.worldspace.host", "my-world.worldspace.host")]
        public void PageHostFrom_ReadsTheJsonValue(string result, string expected)
        {
            Assert.AreEqual(expected, VerifyHostCheck.PageHostFrom(result));
        }

        // Each of these has to read as "can't tell", never as a host that differs: that would move the player.
        [TestCase(null)]
        [TestCase("")]
        [TestCase("\"\"")]
        [TestCase("error: the view has no OraManager")]
        public void PageHostFrom_IsEmptyWhenThereIsNoHost(string result)
        {
            Assert.AreEqual("", VerifyHostCheck.PageHostFrom(result));
        }

        [TestCase("localhost:42068", true)]
        [TestCase("localhost", true)]
        [TestCase("127.0.0.1:42068", true)]
        [TestCase("my-world.worldspace.host", false)]
        [TestCase("localhost.example.com", false)]
        public void IsLocalPage_KnowsAPageServedFromThisMachine(string pageHost, bool expected)
        {
            Assert.AreEqual(expected, VerifyHostCheck.IsLocalPage(pageHost));
        }

        [Test]
        public void TheSwitches_KeepTheirSavedValues()
        {
            // Saved in scenes, prefabs and graphs as numbers.
            Assert.AreEqual(0, (int)VerifyHostOutcome.TeleportToPosition);
            Assert.AreEqual(1, (int)VerifyHostOutcome.SendToWorld);
            Assert.AreEqual(0, (int)VerifyHostMatch.Domain);
            Assert.AreEqual(1, (int)VerifyHostMatch.RegularExpression);
        }

        const string Host = "my-world.worldspace.host";
        const string PageUrl = "https://my-world.worldspace.host/world?a=1#b";

        [TestCase("https://my-world.worldspace.host", true)]
        [TestCase("https://my-world.worldspace.host/some/other/path?x=1", true)]
        [TestCase("MY-WORLD.worldspace.host", true)]
        [TestCase("https://other.worldspace.host", false)]
        [TestCase("https://my-world.worldspace.host:8443", false)]
        public void Domain_ComparesOnlyTheHost(string url, bool expected)
        {
            Assert.AreEqual(expected, VerifyHostCheck.IsRightPage(VerifyHostMatch.Domain, url, Host));
        }

        // SideQuest serves each world at <name>.worldspace.host and <name>.bant.ing alike: both are the world's own address.
        [TestCase("https://my-world.worldspace.host", "my-world.bant.ing", true)]
        [TestCase("https://my-world.bant.ing", "my-world.worldspace.host", true)]
        [TestCase("https://wip.my-world.worldspace.host", "wip.my-world.bant.ing", true)]
        [TestCase("https://my-world.worldspace.host", "other.bant.ing", false)]
        [TestCase("https://my-world.worldspace.host", "my-world.bant.ing:8443", false)]
        [TestCase("https://my-world.worldspace.host:8443", "my-world.bant.ing:8443", true)]
        [TestCase("https://my-world.worldspace.host", "bant.ing", false)]
        [TestCase("https://worldspace.host", "bant.ing", false)]
        [TestCase("https://my-world.worldspace.host", "my-world.bant.ing.example.com", false)]
        [TestCase("https://my-world.worldspace.host", "my-world.notbant.ing", false)]
        [TestCase("https://my-world.example.com", "my-world.bant.ing", false)]
        [TestCase("https://example.com", "example.com", true)]
        public void Domain_TakesAWorldsOtherSideQuestDomainAsTheSameWorld(string url, string page, bool expected)
        {
            Assert.AreEqual(expected, VerifyHostCheck.IsRightPage(VerifyHostMatch.Domain, url, page));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(@"^https://my-world\.worldspace\.host/")]
        public void Domain_CantTellWithoutAnAddress(string url)
        {
            Assert.IsNull(VerifyHostCheck.IsRightPage(VerifyHostMatch.Domain, url, Host));
        }

        [TestCase(@"^https://my-world\.worldspace\.host/", true)]
        [TestCase(@"^https://my-world\.worldspace\.host/world\?a=1#b$", true)]
        [TestCase(@"/world\?a=1", true)]
        [TestCase(@"#b$", true)]
        [TestCase(@"^https://other\.worldspace\.host/", false)]
        [TestCase(@"^https://my-world\.worldspace\.host/$", false)]
        [TestCase(@"^https://MY-WORLD", false)]
        public void AnExpression_RunsOnTheEntireUrl(string expression, bool expected)
        {
            Assert.AreEqual(expected, VerifyHostCheck.IsRightPage(VerifyHostMatch.RegularExpression, expression, PageUrl));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("(unclosed")]
        [TestCase("[")]
        public void AnUnusableExpression_CantTell(string expression)
        {
            Assert.IsNull(VerifyHostCheck.IsRightPage(VerifyHostMatch.RegularExpression, expression, PageUrl));
        }

        [Test]
        public void ARunawayExpression_CantTell_AndDoesntHang()
        {
            // The page picks its URL, so it can pick one this backtracks on for years.
            var page = "https://x/" + new string('a', 40) + "!";
            Assert.IsNull(VerifyHostCheck.IsRightPage(VerifyHostMatch.RegularExpression, @"^https://x/(a+)+$", page));
        }

        [TestCase("\"https://my-world.worldspace.host/\"", "https://my-world.worldspace.host/")]
        [TestCase("\"https://x.example/Path?Q=1\"", "https://x.example/Path?Q=1")]
        [TestCase("\"https://x.example/?a=b\\\\c\"", @"https://x.example/?a=b\c")]
        public void PageUrlFrom_ReadsTheJsonValue_KeepingItsCase(string result, string expected)
        {
            Assert.AreEqual(expected, VerifyHostCheck.PageUrlFrom(result));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("\"\"")]
        [TestCase("error: the view has no OraManager")]
        public void PageUrlFrom_IsEmptyWhenThereIsNoUrl(string result)
        {
            Assert.AreEqual("", VerifyHostCheck.PageUrlFrom(result));
        }
    }
}
