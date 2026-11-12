using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace BS.LocalMultiplayer.Tests
{
    /// <summary>
    /// The session's join as the relay drives it (RELAY.md 2 and 3): the welcome names who is here, and
    /// room.ready completes the join, announcing them in PacketPartyClient's order (ReconcileWelcomePeers,
    /// then RegisterPeerPresence). The messages go straight to the session's main-thread step, as Drain hands
    /// them over; no socket is involved.
    /// </summary>
    public class LocalSessionRosterTests
    {
        LocalSession _session;
        readonly List<string> _events = new List<string>();

        [SetUp]
        public void SetUp()
        {
            var identity = new LocalIdentity
            {
                Slot = "Main Editor",
                ClientId = "local:Main Editor",
                DisplayName = "Main Editor",
                MainProcessId = 1717,
                MainProjectRoot = "C:\\workspace\\TestSDK",
                Role = PlayerRole.Owner,
            };
            _session = new LocalSession(identity, null, "test", () => RelayProtocol.DefaultPort);
            _events.Clear();
            _session.PeerJoined += peer => _events.Add("joined " + peer.RoomSessionId);
            _session.PeerLeft += peer => _events.Add("left " + peer.RoomSessionId);
            _session.RoomJoined += () => _events.Add("room joined");
        }

        [TearDown]
        public void TearDown()
        {
            _session.Shutdown(immediate: true);
        }

        // ---------------------------------------------------------------- relay messages (RELAY.md 2)

        static JObject Member(string roomSessionId, string slot)
        {
            return new JObject
            {
                ["peerId"] = "peer_" + roomSessionId,
                ["userId"] = "local:" + slot,
                ["displayName"] = slot,
                ["roomSessionId"] = roomSessionId,
                ["presenceMetadata"] = new JObject
                {
                    ["slot"] = slot,
                    ["role"] = "member",
                    ["editorPid"] = 1717,
                    ["manifest"] = new JObject { ["ids"] = new JArray(), ["runtimeIds"] = new JArray() },
                },
            };
        }

        // No hub block: it would update HubProbe.Last, which the other tests share.
        static JObject Welcome(bool resumed, params JObject[] existingPeers)
        {
            return new JObject
            {
                ["type"] = "welcome",
                ["protocolVersion"] = 1,
                ["roomId"] = "s-untitled",
                ["roomKey"] = "local:s-untitled",
                ["sessionId"] = "rsess_me",
                ["peerId"] = "peer_me",
                ["userId"] = "local:Main Editor",
                ["resumed"] = resumed,
                ["resumeToken"] = "token-1",
                ["roomClock"] = new JObject { ["roomCreatedAtEpochMs"] = 0, ["serverTimeEpochMs"] = 0, ["roomTimeMs"] = 0 },
                ["existingPeers"] = new JArray(existingPeers.Cast<object>().ToArray()),
            };
        }

        // Exactly what the relay sends (engines.ts buildRoomReady).
        static JObject RoomReady()
        {
            return JObject.Parse("{\"type\":\"room.ready\",\"roomStateReady\":true,\"roomStateRevision\":0," +
                                 "\"userStateReady\":true,\"userStateRevision\":0,\"objectsReady\":true,\"objectsRevision\":0," +
                                 "\"transformsReady\":true,\"transformGraphRevision\":0}");
        }

        void Receive(JObject message)
        {
            _session.Handle(new RelayInbound
            {
                Kind = RelayInboundKind.Message,
                Type = (string)message["type"],
                Body = message,
            });
        }

        void JoinWith(params JObject[] existingPeers)
        {
            Receive(Welcome(false, existingPeers));
            Receive(RoomReady());
            Assert.AreEqual(SessionState.Joined, _session.State);
        }

        string[] PeerIds() => _session.Peers.Select(p => p.RoomSessionId).OrderBy(id => id, System.StringComparer.Ordinal).ToArray();

        // ---------------------------------------------------------------- the roster

        [Test]
        public void AFreshJoin_AnnouncesTheRoster_AtRoomReady_BeforeTheRoom()
        {
            Receive(Welcome(false, Member("rsess_a", "Player 2"), Member("rsess_b", "Player 3")));

            Assert.IsEmpty(_events, "nobody is announced before room.ready");

            Receive(RoomReady());

            Assert.AreEqual(3, _events.Count);
            CollectionAssert.AreEquivalent(new[] { "joined rsess_a", "joined rsess_b" }, _events.Take(2).ToArray());
            Assert.AreEqual("room joined", _events[2]);
            CollectionAssert.AreEqual(new[] { "rsess_a", "rsess_b" }, PeerIds());
        }

        [Test]
        public void AResumedRoster_RaisesTheLeavesFirst_ThenTheJoins()
        {
            JoinWith(Member("rsess_a", "Player 2"), Member("rsess_b", "Player 3"));
            _events.Clear();

            // Back after a drop: Player 2 went away meanwhile and Player 4 arrived; Player 3 is still here.
            Receive(Welcome(true, Member("rsess_b", "Player 3"), Member("rsess_c", "Player 4")));
            Receive(RoomReady());

            CollectionAssert.AreEqual(new[] { "left rsess_a", "joined rsess_c", "room joined" }, _events);
            CollectionAssert.AreEqual(new[] { "rsess_b", "rsess_c" }, PeerIds());
        }

        [Test]
        public void APlayerBackUnderANewSession_LeavesBeforeJoiningAgain()
        {
            JoinWith(Member("rsess_a", "Player 2"));
            _events.Clear();
            var peersDuringTheLeave = new List<string>();
            _session.PeerLeft += _ => peersDuringTheLeave.AddRange(_session.Peers.Select(p => p.RoomSessionId));

            // Player 2 rejoined under a new session while we were reconnecting (same clientId, so the same uid).
            Receive(Welcome(true, Member("rsess_a2", "Player 2")));
            Receive(RoomReady());

            CollectionAssert.AreEqual(new[] { "left rsess_a", "joined rsess_a2", "room joined" }, _events);
            // Removed before its leave is raised (Room.RemovePeer), and the welcome registers nobody before the
            // leaves: neither session is a peer while the leave runs.
            CollectionAssert.IsEmpty(peersDuringTheLeave);
            CollectionAssert.AreEqual(new[] { "rsess_a2" }, PeerIds());
        }

        [Test]
        public void ARoomReadyWithoutTheTransforms_DoesNotCompleteTheJoin()
        {
            var roomReady = RoomReady();
            roomReady.Remove("transformsReady");

            Receive(Welcome(false, Member("rsess_a", "Player 2")));
            Receive(roomReady);

            Assert.IsEmpty(_events);
            Assert.AreNotEqual(SessionState.Joined, _session.State);
            Assert.IsEmpty(_session.Peers);
        }

        // ---------------------------------------------------------------- room.ready (PacketPartyClient.cs:3433-3450)

        [Test]
        public void TheRelaysRoomReady_IsHydrated()
        {
            Assert.IsTrue(LocalSession.IsRoomHydrated(RoomReady()));
        }

        [TestCase("roomStateReady")]
        [TestCase("userStateReady")]
        [TestCase("objectsReady")]
        [TestCase("transformsReady")]
        public void RoomReady_NeedsEveryFlag(string flag)
        {
            var missing = RoomReady();
            missing.Remove(flag);
            var unready = RoomReady();
            unready[flag] = false;

            Assert.IsFalse(LocalSession.IsRoomHydrated(missing), "missing");
            Assert.IsFalse(LocalSession.IsRoomHydrated(unready), "false");
        }
    }
}
