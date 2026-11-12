using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace BS.LocalMultiplayer.Tests
{
    /// <summary>The host's side of RELAY.md (v1): the room id, the hello, and how a hub answer reads.</summary>
    public class RelayProtocolTests
    {
        static readonly Regex RoomIdGrammar = new Regex("^[A-Za-z0-9_-]{1,96}$");

        static LocalIdentity Identity(string slot = "Player 2", PlayerRole role = PlayerRole.Member)
        {
            return new LocalIdentity
            {
                Slot = slot,
                SlotIndex = 2,
                IsClone = true,
                MainProcessId = 1717,
                MainProjectRoot = "C:\\workspace\\TestSDK",
                ClientId = "local:" + slot,
                // The hello carries no uid (the relay never sees it); any value will do.
                Uid = "uid-" + slot,
                DisplayName = slot,
                Role = role,
                WorldOwnerClientId = "local:Main Editor",
            };
        }

        // ---------------------------------------------------------------- room id

        // FNV-1a 64's published vectors, and scene paths hashed independently (Node BigInt).
        [TestCase("", 0xcbf29ce484222325UL)]
        [TestCase("a", 0xaf63dc4c8601ec8cUL)]
        [TestCase("foobar", 0x85944171f73967e8UL)]
        [TestCase("assets/scenes/networking.unity", 0xde4a90668e768db4UL)]
        [TestCase("assets/scènes/ünïcode.unity", 0x82e77a5de2f92e7aUL)]
        public void Fnv1a64_MatchesTheReference(string text, ulong expected)
        {
            Assert.AreEqual(expected, LocalRoomId.Fnv1a64(text));
        }

        [Test]
        public void RoomId_IsSPlusTheLowerCasedPathsHash()
        {
            Assert.AreEqual("s-de4a90668e768db4", LocalRoomId.ForScenePath("Assets/Scenes/Networking.unity"));
            Assert.AreEqual("s-1fbd3bdc8bdf5135", LocalRoomId.ForScenePath("Assets/Samples/GettingStarted/Networking/Networking.unity"));
        }

        [Test]
        public void RoomId_IgnoresCase()
        {
            Assert.AreEqual(LocalRoomId.ForScenePath("assets/scenes/networking.unity"),
                LocalRoomId.ForScenePath("ASSETS/Scenes/NETWORKING.unity"));
        }

        [Test]
        public void RoomId_OfAnUnsavedScene_IsUntitled()
        {
            Assert.AreEqual("s-untitled", LocalRoomId.ForScenePath(""));
            Assert.AreEqual("s-untitled", LocalRoomId.ForScenePath(null));
        }

        [TestCase("Assets/Scenes/Main.unity")]
        [TestCase("Assets/A very long folder name with spaces and ünïcode/And Another/Scene (1).unity")]
        [TestCase("")]
        public void RoomId_PassesTheRelaysGrammar(string scenePath)
        {
            var roomId = LocalRoomId.ForScenePath(scenePath);

            StringAssert.IsMatch(RoomIdGrammar.ToString(), roomId);
            Assert.IsTrue(roomId == "s-untitled" || roomId.Length == 18, roomId);
        }

        [Test]
        public void RoomIds_OfDifferentScenesDiffer()
        {
            Assert.AreNotEqual(LocalRoomId.ForScenePath("Assets/A.unity"), LocalRoomId.ForScenePath("Assets/B.unity"));
        }

        // ---------------------------------------------------------------- hello

        [Test]
        public void Hello_HasExactlyTheFieldsOfRelayMd()
        {
            var manifest = new ObjectManifest { Ids = { "kQx3", "b" }, RuntimeIds = { "-1234" } };

            var hello = RelayProtocol.BuildHello(Identity(), "s-0f3a9c1d2e4b5a67", "4.1.0", manifest);

            CollectionAssert.AreEquivalent(new[]
            {
                "type", "protocolVersion", "projectRoot", "roomId", "clientId", "displayName", "slot", "role",
                "editorPid", "sdkVersion", "manifest",
            }, hello.Properties().Select(p => p.Name).ToArray());
            Assert.AreEqual("hello", (string)hello["type"]);
            Assert.AreEqual(JTokenType.Integer, hello["protocolVersion"].Type);
            Assert.AreEqual(1, (int)hello["protocolVersion"]);
            Assert.AreEqual("C:\\workspace\\TestSDK", (string)hello["projectRoot"]);
            Assert.AreEqual("s-0f3a9c1d2e4b5a67", (string)hello["roomId"]);
            Assert.AreEqual("local:Player 2", (string)hello["clientId"]);
            Assert.AreEqual("Player 2", (string)hello["displayName"]);
            Assert.AreEqual("Player 2", (string)hello["slot"]);
            Assert.AreEqual("member", (string)hello["role"]);
            Assert.AreEqual(JTokenType.Integer, hello["editorPid"].Type);
            Assert.AreEqual(1717, (int)hello["editorPid"]);
            Assert.AreEqual("4.1.0", (string)hello["sdkVersion"]);
            Assert.IsTrue(JToken.DeepEquals(
                JObject.Parse("{\"ids\":[\"kQx3\",\"b\"],\"runtimeIds\":[\"-1234\"]}"), hello["manifest"]));
        }

        [Test]
        public void Hello_WithoutAManifest_SendsAnEmptyOne()
        {
            var hello = RelayProtocol.BuildHello(Identity(), "s-untitled", "4.1.0", null);

            Assert.IsTrue(JToken.DeepEquals(JObject.Parse("{\"ids\":[],\"runtimeIds\":[]}"), hello["manifest"]));
        }

        [Test]
        public void Hello_ResumesOnlyWithBothIdAndToken()
        {
            var resuming = RelayProtocol.BuildHello(Identity(), "s-untitled", "4.1.0", null, "rsess_abc", "token-1");
            var halfResume = RelayProtocol.BuildHello(Identity(), "s-untitled", "4.1.0", null, "rsess_abc", null);

            Assert.IsTrue(JToken.DeepEquals(JObject.Parse("{\"sessionId\":\"rsess_abc\",\"resumeToken\":\"token-1\"}"), resuming["resume"]));
            Assert.IsNull(halfResume["resume"]);
        }

        [TestCase(PlayerRole.Owner, "owner")]
        [TestCase(PlayerRole.Moderator, "moderator")]
        [TestCase(PlayerRole.Member, "member")]
        public void Roles_RoundTrip(PlayerRole role, string wire)
        {
            Assert.AreEqual(wire, RelayProtocol.RoleWire(role));
            Assert.AreEqual(role, RelayProtocol.ParseRole(wire));
            Assert.AreEqual(wire, (string)RelayProtocol.BuildHello(Identity(role: role), "s-untitled", "", null)["role"]);
        }

        [Test]
        public void UnknownRole_IsAMember()
        {
            Assert.AreEqual(PlayerRole.Member, RelayProtocol.ParseRole("admin"));
            Assert.AreEqual(PlayerRole.Member, RelayProtocol.ParseRole(null));
        }

        [Test]
        public void Hello_TrimsWhatTheRelayWouldRefuse()
        {
            var longSlot = new string('x', 40);
            var identity = Identity(longSlot);
            identity.DisplayName = new string('y', 70);

            var hello = RelayProtocol.BuildHello(identity, "s-untitled", "", null);

            Assert.AreEqual(32, ((string)hello["slot"]).Length);
            Assert.AreEqual(64, ((string)hello["displayName"]).Length);
        }

        // ---------------------------------------------------------------- manifests and paths

        [Test]
        public void Manifest_ParsesAndCompares()
        {
            var parsed = RelayProtocol.ParseManifest(JObject.Parse("{\"ids\":[\"a\",\"b\",7],\"runtimeIds\":[\"c\"]}"));

            CollectionAssert.AreEqual(new[] { "a", "b" }, parsed.Ids);
            CollectionAssert.AreEqual(new[] { "c" }, parsed.RuntimeIds);
            Assert.IsTrue(RelayProtocol.SameManifest(parsed, new ObjectManifest { Ids = { "a", "b" }, RuntimeIds = { "c" } }));
            Assert.IsFalse(RelayProtocol.SameManifest(parsed, new ObjectManifest { Ids = { "b", "a" }, RuntimeIds = { "c" } }));
            Assert.AreEqual(0, RelayProtocol.ParseManifest(null).Ids.Count);
        }

        [TestCase("attachment_kQx3", true)]
        [TestCase("pilot", true)]
        [TestCase("avatar.url", true)]
        [TestCase("props.prot.score", false)]
        [TestCase("props.pub.x", false)]
        [TestCase("props", false)]
        [TestCase("propsx", true)]
        [TestCase("", false)]
        public void EnginePaths_AreEverythingButTheContentProps(string path, bool engine)
        {
            Assert.AreEqual(engine, RelayProtocol.IsEnginePath(path));
        }

        // ---------------------------------------------------------------- hub probe answers

        [Test]
        public void Hub_ForThisProject_IsReady()
        {
            var body = "{\"relay\":true,\"protocolVersion\":1,\"impl\":\"1.0.0\",\"projectRoot\":\"C:\\\\workspace\\\\TestSDK\",\"pid\":4242," +
                       "\"parentPid\":1717,\"persistence\":{\"enabled\":true,\"dir\":\"C:\\\\workspace\\\\TestSDK\\\\Library\\\\SideQuestLocalMultiplayer\\\\space-state\"},\"rooms\":[]}";

            var info = HubProbe.Classify(200, body, "C:/workspace/TestSDK/", 42068);

            Assert.AreEqual(HubState.Ready, info.State);
            Assert.AreEqual(4242, info.Pid);
            Assert.AreEqual(1717, info.ParentPid);
            Assert.IsTrue(info.PersistenceEnabled);
            StringAssert.EndsWith("space-state", info.PersistenceDir);
        }

        [Test]
        public void Hub_OfAnotherProject_IsAMismatch()
        {
            var info = HubProbe.Classify(200, "{\"relay\":true,\"projectRoot\":\"C:\\\\other\",\"pid\":9}", "C:/workspace/TestSDK", 42068);

            Assert.AreEqual(HubState.ProjectMismatch, info.State);
            StringAssert.Contains("C:\\other", info.Message);
        }

        [TestCase(404, "Not found")]
        [TestCase(200, "<html></html>")]
        [TestCase(200, "{\"relay\":false}")]
        [TestCase(500, "")]
        public void Hub_WithoutARelay_IsNoRelay(int status, string body)
        {
            Assert.AreEqual(HubState.NoRelay, HubProbe.Classify(status, body, "C:/workspace/TestSDK", 42068).State);
        }

        [Test]
        public void QuotedArguments_NeverEndInABackslash()
        {
            Assert.AreEqual("\"C:\\workspace\\TestSDK\"", LocalMultiplayerBootstrap.QuoteArgument("C:\\workspace\\TestSDK\\"));
            Assert.AreEqual("\"C:/a b/c\"", LocalMultiplayerBootstrap.QuoteArgument("C:/a b/c/"));
            StringAssert.EndsWith(System.IO.Path.Combine("Library", "SideQuestLocalMultiplayer", "space-state"),
                LocalMultiplayerBootstrap.SpaceStateDirectory("C:\\workspace\\TestSDK"));
        }
    }
}
