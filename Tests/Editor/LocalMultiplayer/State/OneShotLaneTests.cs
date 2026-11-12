using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace BS.LocalMultiplayer.Tests
{
    /// <summary>
    /// The one-shot lane against a fake relay session (production OneShotService.cs:77-206 and
    /// BanterSceneEventHandler.cs:838-847): sends only while joined and only valid frames, receives with the
    /// relay's sender stamps, never consumes its own echo, and fromAdmin is the world owner alone.
    /// </summary>
    public class OneShotLaneTests
    {
        FakeLocalSession _session;
        OneShotLane _lane;
        List<OneShotMessage> _received;

        [SetUp]
        public void SetUp()
        {
            _session = new FakeLocalSession { OwnRoomSessionId = "rsess_me" };
            _lane = new OneShotLane(_session);
            _lane.Attach();
            _received = new List<OneShotMessage>();
            _lane.Received += message => _received.Add(message);
        }

        [TearDown]
        public void TearDown()
        {
            _lane.Detach();
        }

        void Receive(string topic, string data, string from = "rsess_2", string userId = "local:Player 2")
        {
            var message = new JObject
            {
                ["fromRoomSessionId"] = from,
                ["fromPeerId"] = "peer_2",
                ["fromUserId"] = userId
            };
            if (topic != null) message["topic"] = topic;
            if (data != null) message["data"] = data;
            _session.Emit(OneShotLane.MessageType, message);
        }

        [Test]
        public void Send_GoesOutAsTheRelaysOneshotMessage()
        {
            Assert.IsTrue(_lane.Send("banter", "hello"));
            Assert.AreEqual(1, _session.Sent.Count);
            Assert.AreEqual("oneshot", _session.Sent[0].Type);
            Assert.AreEqual("banter", (string)_session.Sent[0].Body["topic"]);
            Assert.AreEqual("hello", (string)_session.Sent[0].Body["data"]);
        }

        [Test]
        public void Send_NullDataTravelsAsEmpty()
        {
            Assert.IsTrue(_lane.Send("banter", null));
            Assert.AreEqual("", (string)_session.Sent[0].Body["data"]);
        }

        [TestCase(SessionState.Idle)]
        [TestCase(SessionState.Connecting)]
        [TestCase(SessionState.Joining)]
        [TestCase(SessionState.Reconnecting)]
        [TestCase(SessionState.Failed)]
        public void Send_IsRefusedUnlessJoined(SessionState state)
        {
            _session.State = state;
            Assert.IsFalse(_lane.IsOpen);
            Assert.IsFalse(_lane.Send("banter", "hello"));
            Assert.AreEqual(0, _session.Sent.Count, "nothing is queued for later");
        }

        [Test]
        public void Send_HoldsTheFrameToProductionsLimits()
        {
            Assert.IsTrue(_lane.Send("banter", new string('x', 4089)));
            Assert.IsFalse(_lane.Send("banter", new string('x', 4090)), "a 5 KB payload is refused");
            Assert.IsFalse(_lane.Send("bad topic", "x"));
            Assert.AreEqual(1, _session.Sent.Count);
        }

        [Test]
        public void Receive_CarriesTheRelaysSenderStamps()
        {
            Receive("banter", "x");
            Assert.AreEqual(1, _received.Count);
            Assert.AreEqual("rsess_2", _received[0].RoomSessionId, "fromId is the sender's room session id");
            Assert.AreEqual("local:Player 2", _received[0].UserId);
            Assert.AreEqual("banter", _received[0].Topic);
            Assert.AreEqual("x", _received[0].Data);
        }

        [Test]
        public void Receive_NeverConsumesItsOwnEcho()
        {
            Receive("banter", "x", from: "rsess_me");
            CollectionAssert.IsEmpty(_received);
        }

        [Test]
        public void Receive_HoldsFramesToTheCodecRules()
        {
            Receive("bad topic", "x");
            Receive("banter", new string('x', 4090));
            Receive(null, "x");
            _session.Emit(OneShotLane.MessageType, new JObject { ["topic"] = 7, ["data"] = "x", ["fromRoomSessionId"] = "rsess_2" });
            CollectionAssert.IsEmpty(_received);

            Receive("banter", new string('x', 4089));
            Receive("banter", null);
            Assert.AreEqual(2, _received.Count);
            Assert.AreEqual("", _received[1].Data, "a missing payload is an empty one");
        }

        [Test]
        public void Receive_PassesEveryTopic_TheModuleKeepsBanter()
        {
            // The lane is topic-addressed; only the SDK bridge narrows to "banter".
            Receive("emoji", "{}");
            Assert.AreEqual(1, _received.Count);
            Assert.AreEqual("banter", OneShotModule.SdkOneShotTopic);
        }

        [Test]
        public void Detach_StopsListening()
        {
            _lane.Detach();
            Assert.AreEqual(0, _session.HandlerCount(OneShotLane.MessageType));
            Receive("banter", "x");
            CollectionAssert.IsEmpty(_received);
        }

        [TestCase("local:Main Editor", "local:Main Editor", true)]
        [TestCase("local:Player 2", "local:Main Editor", false)]
        [TestCase("local:main editor", "local:Main Editor", false)]
        [TestCase("", "", false)]
        [TestCase(null, null, false)]
        [TestCase("local:Main Editor", null, false)]
        public void FromAdmin_IsTheWorldOwnerOnly(string fromUserId, string worldOwnerClientId, bool expected)
        {
            Assert.AreEqual(expected, OneShotModule.IsWorldOwner(fromUserId, worldOwnerClientId));
        }
    }
}
