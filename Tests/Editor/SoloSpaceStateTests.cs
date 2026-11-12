using System.Collections.Generic;
using NUnit.Framework;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// Play mode's in-memory space state answers as a room does: objects are stored as their leaves, set replaces and
    /// merge keeps, delete takes the subtree, and a value that didn't change raises nothing. A network host takes over.
    /// </summary>
    public class SoloSpaceStateTests
    {
        const string Ok = "{\"ok\":true}";

        SoloSpaceState state;

        [SetUp]
        public void SetUp() => state = new SoloSpaceState();

        [Test]
        public void Set_ThenGet_ReturnsTheValue()
        {
            Assert.AreEqual(Ok, state.Serve("set", "{\"path\":\"score\",\"value\":3}"));
            Assert.AreEqual("{\"ok\":true,\"value\":3}", state.Serve("get", "{\"path\":\"score\"}"));
        }

        [Test]
        public void Get_OfAnUnknownPath_IsOkWithNoValue()
        {
            Assert.AreEqual(Ok, state.Serve("get", "{\"path\":\"nothing\"}"));
        }

        [Test]
        public void Objects_AreStoredAsTheirLeaves()
        {
            state.Serve("set", "{\"path\":\"game\",\"value\":{\"round\":2,\"board\":[1,2]}}");
            Assert.AreEqual("{\"ok\":true,\"value\":2}", state.Serve("get", "{\"path\":\"game.round\"}"));
            Assert.AreEqual("{\"ok\":true,\"value\":[1,2]}", state.Serve("get", "{\"path\":\"game.board\"}"));
            Assert.AreEqual(Ok, state.Serve("get", "{\"path\":\"game\"}"));
        }

        [Test]
        public void Set_ReplacesTheSubtree_AndMergeKeepsIt()
        {
            state.Serve("set", "{\"path\":\"game\",\"value\":{\"round\":2,\"mode\":\"pvp\"}}");
            state.Serve("merge", "{\"path\":\"game\",\"value\":{\"round\":3}}");
            Assert.AreEqual("{\"ok\":true,\"value\":\"pvp\"}", state.Serve("get", "{\"path\":\"game.mode\"}"));

            state.Serve("set", "{\"path\":\"game\",\"value\":{\"round\":4}}");
            Assert.AreEqual(Ok, state.Serve("get", "{\"path\":\"game.mode\"}"));
            Assert.AreEqual("{\"ok\":true,\"value\":4}", state.Serve("get", "{\"path\":\"game.round\"}"));
        }

        [Test]
        public void Delete_TakesTheSubtree_AndAMissingPathIsNotFound()
        {
            state.Serve("set", "{\"path\":\"game\",\"value\":{\"round\":2,\"mode\":\"pvp\"}}");
            state.Serve("set", "{\"path\":\"gamer\",\"value\":1}");

            var changes = Record();
            Assert.AreEqual(Ok, state.Serve("delete", "{\"path\":\"game\"}"));
            Assert.AreEqual(2, changes.Count);
            Assert.IsTrue(changes.TrueForAll(change => change.Deleted));
            Assert.AreEqual("{\"ok\":true,\"value\":1}", state.Serve("get", "{\"path\":\"gamer\"}"));
            Assert.AreEqual("{\"ok\":false,\"error\":\"not_found\"}", state.Serve("delete", "{\"path\":\"game\"}"));
        }

        [TestCase("set", "{\"value\":1}", "invalid_path")]
        [TestCase("set", "{\"path\":\"a\"}", "invalid_value")]
        [TestCase("delete", "{}", "invalid_path")]
        [TestCase("set", "not json", "invalid_value")]
        [TestCase("increment", "{\"path\":\"a\"}", "unknown_message_type")]
        public void BadRequests_ReturnTheRoomsErrorCode(string op, string json, string code)
        {
            Assert.AreEqual("{\"ok\":false,\"error\":\"" + code + "\"}", state.Serve(op, json));
        }

        [Test]
        public void GetAll_SplitsPublicFromProtected()
        {
            state.Serve("set", "{\"path\":\"a\",\"value\":1}");
            state.Serve("set", "{\"path\":\"b\",\"value\":2,\"protected\":true}");
            Assert.AreEqual("{\"ok\":true,\"revision\":2,\"public\":{\"a\":1},\"protected\":{\"b\":2}}", state.Serve("getAll", "{}"));
        }

        [Test]
        public void Props_AreStrings_AndVisualScriptingReadsThem()
        {
            state.WriteProp("score", "3", false);
            Assert.AreEqual("{\"exists\":true,\"value\":\"3\",\"json\":\"\\\"3\\\"\",\"isPublic\":true}", state.Read("score"));
            Assert.AreEqual("{\"exists\":false}", state.Read("other"));
        }

        [Test]
        public void WritingTheSameValue_RaisesNothing()
        {
            state.WriteProp("score", "3", false);
            var changes = Record();
            state.WriteProp("score", "3", false);
            Assert.IsEmpty(changes);
            state.WriteProp("score", "4", false);
            Assert.AreEqual(1, changes.Count);
            Assert.IsFalse(changes[0].Deleted);
        }

        // SpaceStateSet in the app sends a delete and a set in one batch, and the room passes on both changes.
        [Test]
        public void Set_OverAnExistingKey_IsADeleteThenAWrite_AsInARoom()
        {
            state.Serve("set", "{\"path\":\"score\",\"value\":3}");
            var changes = Record();

            state.Serve("set", "{\"path\":\"score\",\"value\":3}");

            Assert.AreEqual(2, changes.Count);
            Assert.IsTrue(changes[0].Deleted);
            Assert.IsFalse(changes[1].Deleted);
            Assert.AreEqual("{\"ok\":true,\"value\":3}", state.Serve("get", "{\"path\":\"score\"}"));
        }

        [Test]
        public void Set_OverAnObject_DropsTheLeavesItNoLongerNames()
        {
            state.Serve("set", "{\"path\":\"game\",\"value\":{\"round\":2,\"mode\":\"pvp\"}}");
            var changes = Record();

            state.Serve("set", "{\"path\":\"game\",\"value\":{\"round\":2}}");

            Assert.AreEqual(3, changes.Count);
            Assert.IsTrue(changes[0].Deleted && changes[1].Deleted);
            Assert.AreEqual("game.round", changes[2].Key);
            Assert.AreEqual(Ok, state.Serve("get", "{\"path\":\"game.mode\"}"));
        }

        [Test]
        public void Merge_OverAnExistingKey_IsOneWrite_AndTheSameValueNone()
        {
            state.Serve("set", "{\"path\":\"score\",\"value\":3}");
            var changes = Record();

            state.Serve("merge", "{\"path\":\"score\",\"value\":3}");
            Assert.IsEmpty(changes);

            state.Serve("merge", "{\"path\":\"score\",\"value\":4}");
            Assert.AreEqual(1, changes.Count);
            Assert.IsFalse(changes[0].Deleted);
        }

        [Test]
        public void Attached_ItAnswersSpaceRequestsOnly()
        {
            var events = new BSSceneEvents();
            SoloSpaceState.Attach(events, () => null);

            string reply = null;
            var space = new BSStateRequest(json => reply = json) { Target = BSStateTarget.Space, Op = "set", Json = "{\"path\":\"a\",\"value\":1}" };
            events.OnStateRequest.Invoke(space);
            Assert.IsTrue(space.Handled);
            Assert.AreEqual(Ok, reply);
            StringAssert.Contains("\"exists\":true", events.GetSpaceStateValue("a"));

            var user = new BSStateRequest(_ => { }) { Target = BSStateTarget.User, Op = "get", Json = "{}" };
            events.OnStateRequest.Invoke(user);
            Assert.IsFalse(user.Handled);
        }

        [Test]
        public void Attached_ItLeavesRequestsToANetworkHost()
        {
            var events = new BSSceneEvents();
            SoloSpaceState.Attach(events, () => null);
            BSNetworkHost.Active = true;
            try
            {
                var request = new BSStateRequest(_ => { }) { Target = BSStateTarget.Space, Op = "get", Json = "{}" };
                events.OnStateRequest.Invoke(request);
                Assert.IsFalse(request.Handled);
            }
            finally
            {
                BSNetworkHost.Active = false;
            }
        }

        List<SoloSpaceState.Change> Record()
        {
            var changes = new List<SoloSpaceState.Change>();
            state.Changed += changes.Add;
            return changes;
        }
    }
}
