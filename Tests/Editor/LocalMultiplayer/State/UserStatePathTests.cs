using System.Collections.Generic;
using System.Threading;
using BS.LocalMultiplayer.State;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace BS.LocalMultiplayer.Tests
{
    /// <summary>
    /// Where page user props live in user state (production UserStateService.cs:135-237): under
    /// <c>props.prot.&lt;key&gt;</c> (owner-only) or <c>props.pub.&lt;key&gt;</c>, never colliding with the engine's
    /// root keys, and capped at 64 flattened leaves per player.
    /// </summary>
    public class UserStatePathTests
    {
        [TestCase("score", UserPropScope.OwnerOnly, "props.prot.score")]
        [TestCase("score", UserPropScope.ModeratorWritable, "props.pub.score")]
        [TestCase("hat.color", UserPropScope.OwnerOnly, "props.prot.hat.color")]
        [TestCase("props.x", UserPropScope.OwnerOnly, "props.prot.props.x")]
        public void Build_NamespacesTheKey(string key, UserPropScope scope, string expected)
        {
            Assert.IsTrue(UserStateService.TryBuildPath(key, scope, out var path, out var reason), reason);
            Assert.AreEqual(expected, path);
        }

        [Test]
        public void Build_EscapesKeysOutsideTheServerGrammar()
        {
            Assert.IsTrue(UserStateService.TryBuildPath("my key", UserPropScope.OwnerOnly, out var path, out _));
            Assert.IsTrue(StatePathCodec.TryEncodeSegment("my key", out var escaped, out _));
            Assert.AreEqual("props.prot." + escaped, path);
            StringAssert.StartsWith("props.prot.:", path);
        }

        [TestCase("score", UserPropScope.OwnerOnly)]
        [TestCase("score", UserPropScope.ModeratorWritable)]
        [TestCase("a.b.c", UserPropScope.OwnerOnly)]
        [TestCase("my key", UserPropScope.ModeratorWritable)]
        [TestCase("émoji🎉", UserPropScope.OwnerOnly)]
        public void Parse_RoundTripsTheKeyAndScope(string key, UserPropScope scope)
        {
            Assert.IsTrue(UserStateService.TryBuildPath(key, scope, out var path, out _));
            Assert.IsTrue(UserStateService.TryParsePath(path, out var back, out var backScope));
            Assert.AreEqual(key, back);
            Assert.AreEqual(scope, backScope);
        }

        [TestCase("attachment_kQx3")]
        [TestCase("pilot")]
        [TestCase("avatar")]
        [TestCase("props")]
        [TestCase("props.prot")]
        [TestCase("props.other.x")]
        [TestCase("xprops.prot.a")]
        [TestCase("")]
        [TestCase(null)]
        public void Parse_IgnoresEngineAndForeignPaths(string path)
        {
            // Engine root keys ride the session's own channel and must never reach the page.
            Assert.IsFalse(UserStateService.TryParsePath(path, out _, out _), path ?? "<null>");
        }

        [Test]
        public void Build_RefusesAKeyTooLongOnceNamespaced()
        {
            // 248 characters encode to a legal 248-char path, but "props.prot." makes it 259.
            var parts = new List<string>();
            for (int i = 0; i < 4; i++) parts.Add(new string('a', 60));
            parts.Add("abcd");
            string key = string.Join(".", parts);
            Assert.AreEqual(248, key.Length);
            Assert.IsTrue(StatePathCodec.TryEncodePath(key, out _, out _));

            Assert.IsFalse(UserStateService.TryBuildPath(key, UserPropScope.OwnerOnly, out _, out var reason));
            StringAssert.Contains("too long once namespaced", reason);
        }

        [Test]
        public void CountLeaves_CountsWhatTheServerStores()
        {
            Assert.AreEqual(1, UserStateService.CountLeaves(null));
            Assert.AreEqual(1, UserStateService.CountLeaves(JValue.CreateString("x")));
            Assert.AreEqual(1, UserStateService.CountLeaves(new JValue(3)));
            Assert.AreEqual(1, UserStateService.CountLeaves(JArray.Parse("[1,2,3]")), "arrays are one leaf");
            Assert.AreEqual(1, UserStateService.CountLeaves(new JObject()), "an empty object is one leaf");
            Assert.AreEqual(3, UserStateService.CountLeaves(JObject.Parse("{\"a\":1,\"b\":{\"c\":2,\"d\":[1]}}")));
        }

        static UserStateService NewService(out List<string> failures)
        {
            var session = new FakeLocalSession();
            var client = new UserStateClient(session);
            client.Attach();
            var service = new UserStateService(client, new MainThreadQueue().Post, CancellationToken.None, () => 0f);
            var codes = new List<string>();
            service.WriteFailed += (key, code, message) => codes.Add(key + ":" + code);
            failures = codes;
            return service;
        }

        [Test]
        public void LeafCap_Allows64_AndRefusesThe65th()
        {
            var service = NewService(out var failures);
            for (int i = 0; i < UserStateService.MaxPropLeaves; i++)
            {
                Assert.IsTrue(service.TrySetOwnProp("k" + i, JValue.CreateString("v"), UserPropScope.OwnerOnly, out var error), error);
            }
            Assert.AreEqual(64, service.OwnLeaves);

            Assert.IsFalse(service.TrySetOwnProp("k64", JValue.CreateString("v"), UserPropScope.OwnerOnly, out var refused));
            Assert.AreEqual(StateErrors.TooManyKeys, refused);
            CollectionAssert.AreEqual(new[] { "k64:" + StateErrors.TooManyKeys }, failures);

            // Rewriting a key replaces its leaves rather than adding to them.
            Assert.IsTrue(service.TrySetOwnProp("k0", JValue.CreateString("again"), UserPropScope.OwnerOnly, out _));
            Assert.AreEqual(64, service.OwnLeaves);
        }

        [Test]
        public void LeafCap_CountsNestedLeaves_AndDeletesFreeTheSubtree()
        {
            var service = NewService(out _);
            for (int i = 0; i < 62; i++) service.SetOwnProp("k" + i, JValue.CreateString("v"));

            // Three leaves do not fit in the two that are left.
            var three = JObject.Parse("{\"a\":1,\"b\":2,\"c\":3}");
            Assert.IsFalse(service.TrySetOwnProp("tree", three, UserPropScope.OwnerOnly, out var error));
            Assert.AreEqual(StateErrors.TooManyKeys, error);

            service.RemoveOwnProp("k0");
            service.RemoveOwnProp("k1");
            Assert.AreEqual(60, service.OwnLeaves);
            Assert.IsTrue(service.TrySetOwnProp("tree", three, UserPropScope.OwnerOnly, out _));
            Assert.IsTrue(service.TrySetOwnProp("tree.extra", JValue.CreateString("x"), UserPropScope.OwnerOnly, out _));
            Assert.AreEqual(64, service.OwnLeaves);

            // Removing a parent drops every prop under it, as the server's subtree delete does.
            service.RemoveOwnProp("tree");
            Assert.AreEqual(60, service.OwnLeaves);
        }

        [Test]
        public void InvalidKeys_AreRefusedBeforeTheyQueue()
        {
            var service = NewService(out var failures);
            Assert.IsFalse(service.TrySetOwnProp("a..b", JValue.CreateString("v"), UserPropScope.OwnerOnly, out var error));
            Assert.AreEqual(StateErrors.InvalidPath, error);
            Assert.IsFalse(service.TryRemoveOwnProp("", UserPropScope.OwnerOnly, out error));
            Assert.AreEqual(StateErrors.InvalidPath, error);
            Assert.AreEqual(2, failures.Count);
            Assert.AreEqual(0, service.OwnLeaves);
        }
    }
}
