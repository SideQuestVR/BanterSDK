using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BS.LocalMultiplayer.Tests
{
    using BS.LocalMultiplayer.Objects;

    /// <summary>
    /// The object registry the synced objects run on (PacketPartyClient's object half + the scene binder), against a
    /// fake session: the record mirror, request replies, the obj.payload lane and its receive gate, and what a dropped
    /// connection keeps (PacketPartyClient's partial teardown) versus what an ended session clears.
    /// </summary>
    public class LocalObjectBinderTests
    {
        const string Me = "rsess_me";
        const string Other = "rsess_other";

        FakeSession _session;
        FakeHost _host;
        LocalObjectBinder _binder;
        SynchronizationContext _savedContext;

        [SetUp]
        public void SetUp()
        {
            // Replies complete inline, as they would once the session drains them on the main thread.
            _savedContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);
            _session = new FakeSession();
            _host = new FakeHost(_session);
            _binder = new LocalObjectBinder(_host);
        }

        [TearDown]
        public void TearDown()
        {
            _binder.Dispose();
            SynchronizationContext.SetSynchronizationContext(_savedContext);
        }

        internal static JObject Record(string objectId, string owner, uint generation, long revision, bool locked = false)
        {
            return new JObject
            {
                ["objectId"] = objectId,
                ["typeKey"] = "object",
                ["origin"] = "scene",
                ["ownerRoomSessionId"] = owner,
                ["ownerDisconnectPolicy"] = "transfer",
                ["ownershipGeneration"] = generation,
                ["locked"] = locked,
                ["components"] = new JObject(),
                ["revision"] = revision,
                ["createdAtEpochMs"] = 0
            };
        }

        void JoinWith(string ownRoomSessionId, params JObject[] records)
        {
            _session.Deliver(ObjectMessages.Snapshot, new JObject { ["revision"] = 5, ["objects"] = new JArray(records) });
            _session.JoinRoom(ownRoomSessionId);
        }

        static JObject Payload(string objectId, uint generation, int seq, string from)
        {
            return new JObject
            {
                ["type"] = ObjectLaneMessages.Payload,
                ["objectId"] = objectId,
                ["gen"] = generation,
                ["seq"] = seq,
                ["key"] = HeldObjectCodec.ComponentKey,
                ["body"] = Convert.ToBase64String(new byte[] { 1, 2, 3 }),
                ["reliable"] = true,
                ["from"] = from
            };
        }

        // ─── Mirror ─────────────────────────────────────────────────────────────

        [Test]
        public void Snapshot_FillsTheMirror_AndOwnershipFollowsTheRoomSession()
        {
            JoinWith(Me, Record("a", Me, 0, 1), Record("b", Other, 2, 3));
            Assert.AreEqual(2, _binder.Objects.Count);
            Assert.AreEqual(5, _binder.ObjectsRevision);
            Assert.IsTrue(_binder.IsJoined);
            Assert.IsTrue(_binder.IsOwnedBySelf(_binder.GetRecord("a")));
            Assert.IsFalse(_binder.IsOwnedBySelf(_binder.GetRecord("b")));
            Assert.AreEqual(2u, _binder.GetRecord("b").OwnershipGeneration);
        }

        [Test]
        public void Broadcasts_UpdateTheMirror()
        {
            JoinWith(Me, Record("a", Me, 0, 1));
            _session.Deliver(ObjectMessages.Created, new JObject { ["object"] = Record("b", Other, 0, 1), ["revision"] = 6 });
            Assert.AreEqual(Other, _binder.GetRecord("b").OwnerRoomSessionId);
            _session.Deliver(ObjectMessages.OwnerChanged, new JObject
            {
                ["object"] = Record("a", Other, 1, 2), ["previousOwnerRoomSessionId"] = Me, ["reason"] = "acquired", ["revision"] = 7
            });
            Assert.IsFalse(_binder.IsOwnedBySelf(_binder.GetRecord("a")));
            _session.Deliver(ObjectMessages.Updated, new JObject { ["object"] = Record("a", Other, 1, 3, locked: true), ["reason"] = "locked", ["revision"] = 8 });
            Assert.IsTrue(_binder.GetRecord("a").Locked);
            _session.Deliver(ObjectMessages.Deleted, new JObject { ["objectId"] = "b", ["reason"] = "deleted", ["revision"] = 9 });
            Assert.IsNull(_binder.GetRecord("b"));
            Assert.AreEqual(9, _binder.ObjectsRevision);
        }

        [Test]
        public void ADroppedConnection_KeepsTheMirror_AnEndedSessionClearsIt()
        {
            JoinWith(Me, Record("a", Me, 0, 1), Record("b", Other, 2, 3));
            _session.Drop();
            Assert.IsFalse(_binder.IsJoined);
            Assert.AreEqual(2, _binder.Objects.Count, "PacketPartyClient's partial teardown keeps the object mirror");
            Assert.IsTrue(_binder.IsOwnedBySelf(_binder.GetRecord("a")));
            _session.EndSession();
            Assert.AreEqual(0, _binder.Objects.Count);
        }

        // ─── Requests ───────────────────────────────────────────────────────────

        [Test]
        public async Task Create_IsAnIdempotentSceneDeclare()
        {
            JoinWith(Me);
            _session.Reply = (type, body) => Task.FromResult(new JObject
            {
                ["type"] = ObjectMessages.Result, ["ok"] = true, ["revision"] = 1, ["object"] = Record("kQx3", Other, 0, 1)
            });
            var record = await _binder.CreateObjectAsync("object", SyncedObjectOrigin.Scene, "kQx3", SyncedObjectDisconnectPolicy.Transfer);
            Assert.AreEqual("kQx3", record.ObjectId);
            var (type, request) = _session.Requests[0];
            Assert.AreEqual(ObjectMessages.Create, type);
            Assert.AreEqual("object", (string)request["typeKey"]);
            Assert.AreEqual("scene", (string)request["origin"]);
            Assert.AreEqual("kQx3", (string)request["objectId"]);
            Assert.AreEqual("transfer", (string)request["ownerDisconnectPolicy"]);
            Assert.IsNull(request["locked"]);
            Assert.AreEqual(Other, _binder.GetRecord("kQx3").OwnerRoomSessionId, "the reply's record is applied at once");
        }

        [Test]
        public async Task Acquire_And_CommitState_UseTheProtocolFields()
        {
            JoinWith(Me, Record("a", Me, 4, 1));
            _session.Reply = (type, body) => Task.FromResult(new JObject { ["ok"] = true, ["revision"] = 2, ["object"] = Record("a", Me, 5, 2) });
            await _binder.AcquireObjectAsync("a", locked: false);
            await _binder.CommitObjectStateAsync("a", new JObject { ["transform"] = new JObject() }, 5);
            Assert.AreEqual(ObjectMessages.Acquire, _session.Requests[0].Type);
            Assert.AreEqual("a", (string)_session.Requests[0].Body["objectId"]);
            Assert.AreEqual(false, (bool)_session.Requests[0].Body["lock"]);
            Assert.AreEqual(ObjectMessages.CommitState, _session.Requests[1].Type);
            Assert.AreEqual(5, (long)_session.Requests[1].Body["expectedOwnershipGeneration"]);
            Assert.IsNotNull(_session.Requests[1].Body["state"]["transform"]);
        }

        [Test]
        public void ADenial_AppliesTheAuthoritativeRecord_AndThrowsItsCode()
        {
            JoinWith(Me, Record("a", Me, 0, 1));
            _session.Reply = (type, body) => Task.FromResult(new JObject
            {
                ["ok"] = false, ["error"] = "object_locked", ["revision"] = 4, ["object"] = Record("a", Other, 3, 4, locked: true)
            });
            var error = Assert.ThrowsAsync<LocalRelayException>(() => _binder.AcquireObjectAsync("a", locked: true));
            Assert.AreEqual("object_locked", error.Code);
            Assert.AreEqual(Other, _binder.GetRecord("a").OwnerRoomSessionId);
            Assert.IsTrue(_binder.GetRecord("a").Locked);
        }

        [Test]
        public async Task AStaleReply_DoesNotRollBackANewerBroadcast()
        {
            JoinWith(Me, Record("a", Other, 0, 1));
            var reply = new TaskCompletionSource<JObject>();
            _session.Reply = (type, body) => reply.Task;
            var acquire = _binder.AcquireObjectAsync("a", locked: false);
            // The broadcast of a later acquire by someone else lands before this reply is applied.
            _session.Deliver(ObjectMessages.OwnerChanged, new JObject { ["object"] = Record("a", Other, 2, 3), ["revision"] = 9 });
            reply.SetResult(new JObject { ["ok"] = true, ["revision"] = 8, ["object"] = Record("a", Me, 1, 2) });
            await acquire;
            Assert.AreEqual(Other, _binder.GetRecord("a").OwnerRoomSessionId);
            Assert.AreEqual(2u, _binder.GetRecord("a").OwnershipGeneration);
        }

        [Test]
        public void AReplyFromAnEndedSession_IsNotApplied()
        {
            JoinWith(Me, Record("a", Other, 0, 1));
            var reply = new TaskCompletionSource<JObject>();
            _session.Reply = (type, body) => reply.Task;
            var acquire = _binder.AcquireObjectAsync("a", locked: false);
            _session.EndSession();
            _session.Deliver(ObjectMessages.Snapshot, new JObject { ["revision"] = 1, ["objects"] = new JArray() });
            _session.JoinRoom("rsess_new");
            reply.SetResult(new JObject { ["ok"] = true, ["revision"] = 2, ["object"] = Record("a", Me, 1, 2) });
            var error = Assert.ThrowsAsync<LocalRelayException>(() => acquire);
            Assert.AreEqual("closed", error.Code);
            Assert.IsNull(_binder.GetRecord("a"));
        }

        [Test]
        public void Requests_OutsideARoom_AreRefused()
        {
            var error = Assert.ThrowsAsync<LocalRelayException>(() => _binder.AcquireObjectAsync("a", locked: false));
            Assert.AreEqual("not_connected", error.Code);
        }

        // ─── Payload lane ───────────────────────────────────────────────────────

        [Test]
        public void SendObjectPayload_CarriesTheRelayFields()
        {
            JoinWith(Me, Record("a", Me, 7, 1));
            _binder.SendObjectPayload("a", HeldObjectCodec.ComponentKey, new byte[] { 1, 2, 3 });
            _binder.SendObjectPayload("a", HeldObjectCodec.ComponentKey, new byte[] { 4 });
            var (type, first) = _session.Sent[0];
            Assert.AreEqual("obj.payload", type);
            Assert.AreEqual("a", (string)first["objectId"]);
            Assert.AreEqual(7, (long)first["gen"]);
            Assert.AreEqual(1, (long)first["seq"]);
            Assert.AreEqual("packetparty.attachment", (string)first["key"]);
            Assert.AreEqual("AQID", (string)first["body"]);
            Assert.AreEqual(true, (bool)first["reliable"]);
            Assert.AreEqual(2, (long)_session.Sent[1].Body["seq"]);
        }

        [Test]
        public void SendObjectPayload_RestartsItsSequenceForANewOwnership()
        {
            JoinWith(Me, Record("a", Me, 7, 1));
            _binder.SendObjectPayload("a", "k", new byte[0]);
            _binder.SendObjectPayload("a", "k", new byte[0]);
            // Grabbing an object already owned bumps the generation (acquire with a new lock state).
            _session.Deliver(ObjectMessages.OwnerChanged, new JObject { ["object"] = Record("a", Me, 8, 2), ["reason"] = "acquired", ["revision"] = 6 });
            _binder.SendObjectPayload("a", "k", new byte[0]);
            Assert.AreEqual(1, (long)_session.Sent[2].Body["seq"]);
            Assert.AreEqual(8, (long)_session.Sent[2].Body["gen"]);
        }

        [Test]
        public void SendObjectPayload_NeedsOwnership()
        {
            JoinWith(Me, Record("a", Other, 1, 1));
            Assert.AreEqual("not_owner", Assert.Throws<LocalRelayException>(() => _binder.SendObjectPayload("a", "k", new byte[0])).Code);
            Assert.AreEqual("unknown_object", Assert.Throws<LocalRelayException>(() => _binder.SendObjectPayload("zz", "k", new byte[0])).Code);
        }

        [Test]
        public void SendObjectPayload_WhileReconnecting_IsRefused()
        {
            JoinWith(Me, Record("a", Me, 1, 1));
            _session.Drop();
            Assert.AreEqual("not_connected", Assert.Throws<LocalRelayException>(() => _binder.SendObjectPayload("a", "k", new byte[0])).Code);
        }

        [Test]
        public void SequenceStreams_SurviveAResume_AndRestartWithANewSession()
        {
            JoinWith(Me, Record("a", Me, 1, 1));
            _binder.SendObjectPayload("a", "k", new byte[0]);
            _session.Drop();
            JoinWith(Me, Record("a", Me, 1, 1));
            _binder.SendObjectPayload("a", "k", new byte[0]);
            Assert.AreEqual(2, (long)_session.Sent[1].Body["seq"], "a resumed session continues its streams");
            _session.EndSession();
            JoinWith("rsess_new", Record("a", "rsess_new", 2, 2));
            _binder.SendObjectPayload("a", "k", new byte[0]);
            Assert.AreEqual(1, (long)_session.Sent[2].Body["seq"], "a new session starts its streams over");
        }

        [Test]
        public void PayloadGate_AcceptsOnlyTheOwnerAtTheRecordGenerationWithANewerSequence()
        {
            JoinWith(Me, Record("a", Other, 2, 1));
            Assert.IsTrue(_binder.AcceptObjectLaneMessage(Payload("a", 2, 5, Other)));
            Assert.IsFalse(_binder.AcceptObjectLaneMessage(Payload("a", 2, 5, Other)), "repeated sequence");
            Assert.IsFalse(_binder.AcceptObjectLaneMessage(Payload("a", 2, 4, Other)), "older sequence");
            Assert.IsTrue(_binder.AcceptObjectLaneMessage(Payload("a", 2, 6, Other)));
            Assert.IsFalse(_binder.AcceptObjectLaneMessage(Payload("a", 2, 7, "rsess_third")), "not the owner");
            Assert.IsFalse(_binder.AcceptObjectLaneMessage(Payload("a", 1, 7, Other)), "an older generation");
            Assert.IsFalse(_binder.AcceptObjectLaneMessage(Payload("zz", 2, 7, Other)), "an unknown object");
            var malformed = Payload("a", 2, 7, Other);
            malformed.Remove("gen");
            Assert.IsFalse(_binder.AcceptObjectLaneMessage(malformed));
            malformed = Payload("a", 2, 7, Other);
            malformed["body"] = "%%%";
            Assert.IsFalse(_binder.AcceptObjectLaneMessage(malformed));
        }

        [Test]
        public void PayloadGate_StartsOverForANewOwner()
        {
            JoinWith(Me, Record("a", Other, 2, 1));
            Assert.IsTrue(_binder.AcceptObjectLaneMessage(Payload("a", 2, 40, Other)));
            _session.Deliver(ObjectMessages.OwnerChanged, new JObject { ["object"] = Record("a", "rsess_third", 3, 2), ["revision"] = 6 });
            Assert.IsTrue(_binder.AcceptObjectLaneMessage(Payload("a", 3, 1, "rsess_third")));
        }

        [Test]
        public void PayloadGate_IgnoresAStreamRecordedForAnEarlierOwnership()
        {
            // The owner changed while this player was away (no ownerChanged seen): the old gate must not block.
            JoinWith(Me, Record("a", Other, 2, 1));
            Assert.IsTrue(_binder.AcceptObjectLaneMessage(Payload("a", 2, 40, Other)));
            _session.Drop();
            JoinWith(Me, Record("a", Other, 5, 3));
            Assert.IsTrue(_binder.AcceptObjectLaneMessage(Payload("a", 5, 1, Other)));
        }

        [Test]
        public void SceneLoads_AreCounted()
        {
            int before = _binder.SceneLoadSerial;
            _binder.NotifySceneLoad();
            Assert.AreEqual(before + 1, _binder.SceneLoadSerial);
        }

        // ─── Declares across room sessions ──────────────────────────────────────

        // A network object declared on the first join. The relay's scene create is idempotent: it answers with the
        // existing record, or creates one owned by the declarer.
        LocalNetworkObject DeclareOnFirstJoin(GameObject go, Dictionary<string, JObject> relay)
        {
            var networkObject = go.AddComponent<LocalNetworkObject>();
            networkObject.ConfigureSceneDeclare(_binder, "object", "a");
            _session.Reply = (type, body) =>
            {
                if (type != ObjectMessages.Create) return Task.FromResult(new JObject { ["ok"] = true, ["revision"] = 0 });
                string id = (string)body["objectId"];
                if (!relay.TryGetValue(id, out var record)) relay[id] = record = Record(id, _session.OwnRoomSessionId, 0, 1);
                return Task.FromResult(new JObject { ["ok"] = true, ["revision"] = (long)record["revision"], ["object"] = record });
            };
            _binder.Register(networkObject);
            JoinWith(Me);
            Assert.AreEqual(1, CountRequests(ObjectMessages.Create), "the first join declares");
            Assert.IsTrue(networkObject.IsOwned);
            return networkObject;
        }

        int CountRequests(string type)
        {
            int count = 0;
            foreach (var request in _session.Requests)
            {
                if (request.Type == type) count++;
            }
            return count;
        }

        [Test]
        public void AResumedSession_OnlyRebinds_ANewOneDeclaresAgain()
        {
            var go = new GameObject("synced");
            try
            {
                var relay = new Dictionary<string, JObject>();
                var networkObject = DeclareOnFirstJoin(go, relay);
                int declares = _binder.SceneLoadSerial;

                // The connection dropped and the same room session resumed: its records are still there.
                _session.Drop();
                JoinWith(Me, Record("a", Me, 0, 1));
                Assert.AreEqual(1, CountRequests(ObjectMessages.Create), "a resumed session re-binds without declaring");
                Assert.AreEqual(declares, _binder.SceneLoadSerial);
                Assert.IsTrue(_binder.TryGetBound("a", out var bound) && bound == networkObject);

                // Leave → Join (or a refused resume) after the relay deleted the record, as it does when nobody is left
                // to take it over: the new session declares the object again instead of binding it to nothing.
                _session.EndSession();
                relay.Clear();
                JoinWith("rsess_new");
                Assert.AreEqual(2, CountRequests(ObjectMessages.Create), "a new room session declares every object again");
                Assert.AreEqual(declares + 1, _binder.SceneLoadSerial);
                Assert.IsTrue(_binder.TryGetBound("a", out bound) && bound == networkObject);
                Assert.IsNotNull(networkObject.Record, "the object has a room record again");
                Assert.IsTrue(networkObject.IsOwned, "and is this player's, so its body isn't held kinematic");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ANewSession_BindsToARecordThatSurvived()
        {
            var go = new GameObject("synced");
            try
            {
                var relay = new Dictionary<string, JObject>();
                var networkObject = DeclareOnFirstJoin(go, relay);

                // Another player took the object over while this one was away; the idempotent create returns that record.
                _session.EndSession();
                relay["a"] = Record("a", Other, 1, 2);
                JoinWith("rsess_new", Record("a", Other, 1, 2));
                Assert.AreEqual(2, CountRequests(ObjectMessages.Create));
                Assert.IsTrue(_binder.TryGetBound("a", out var bound) && bound == networkObject);
                Assert.AreEqual(Other, networkObject.OwnerRoomSessionId, "the declare doesn't take the object back");
                Assert.IsFalse(networkObject.IsOwned);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        // ─── Fakes ──────────────────────────────────────────────────────────────

#pragma warning disable CS0067 // events the binder doesn't use
        internal sealed class FakeSession : ILocalSession
        {
            readonly Dictionary<string, List<Action<JObject>>> _handlers = new Dictionary<string, List<Action<JObject>>>();
            public readonly List<(string Type, JObject Body)> Requests = new List<(string Type, JObject Body)>();
            public readonly List<(string Type, JObject Body)> Sent = new List<(string Type, JObject Body)>();
            public Func<string, JObject, Task<JObject>> Reply;

            public SessionState State { get; private set; } = SessionState.Idle;
            public event Action<SessionState> StateChanged;
            public string RoomId => "s-test";
            public string OwnRoomSessionId { get; private set; }
            public string OwnPeerId => "peer_me";
            public string RelayUri => "ws://127.0.0.1:42068/__sq-relay";
            public double RttMs => 0;
            public string LastError => null;
            public HubInfo Hub { get; } = new HubInfo();
            public event Action RoomJoined;
            public event Action RoomLeft;
            public IReadOnlyCollection<LocalPeer> Peers => Array.Empty<LocalPeer>();
            public bool TryGetPeer(string roomSessionId, out LocalPeer peer)
            {
                peer = null;
                return false;
            }
            public event Action<LocalPeer> PeerJoined;
            public event Action<LocalPeer> PeerLeft;
            public event Action<LocalPeer> PeerManifestChanged;

            public Task<JObject> RequestAsync(string type, JObject body, CancellationToken cancellationToken = default)
            {
                if (State != SessionState.Joined)
                    return Task.FromException<JObject>(new LocalRelayException("not_connected"));
                Requests.Add((type, body));
                return Reply != null ? Reply(type, body) : Task.FromResult(new JObject { ["ok"] = true, ["revision"] = 0 });
            }

            public bool Send(string type, JObject body)
            {
                if (State != SessionState.Joined) return false;
                Sent.Add((type, body));
                return true;
            }

            public void On(string type, Action<JObject> handler)
            {
                if (!_handlers.TryGetValue(type, out var list)) _handlers[type] = list = new List<Action<JObject>>();
                list.Add(handler);
            }

            public void Off(string type, Action<JObject> handler)
            {
                if (_handlers.TryGetValue(type, out var list)) list.Remove(handler);
            }

            public void Deliver(string type, JObject body)
            {
                body["type"] = type;
                if (!_handlers.TryGetValue(type, out var list)) return;
                foreach (var handler in list.ToArray()) handler(body);
            }

            public void JoinRoom(string ownRoomSessionId)
            {
                OwnRoomSessionId = ownRoomSessionId;
                State = SessionState.Joined;
                StateChanged?.Invoke(State);
                RoomJoined?.Invoke();
            }

            public void Drop()
            {
                State = SessionState.Reconnecting;
                StateChanged?.Invoke(State);
            }

            public void EndSession()
            {
                OwnRoomSessionId = null;
                RoomLeft?.Invoke();
                State = SessionState.Idle;
                StateChanged?.Invoke(State);
            }

            public void SendParticipantFrame(in ParticipantFrame frame) { }
            public event Action<string, ParticipantFrame> ParticipantFrameReceived;
            public readonly List<ObjectFrame> QueuedFrames = new List<ObjectFrame>();
            public void QueueObjectFrame(in ObjectFrame frame) => QueuedFrames.Add(frame);
            public event Action<string, ObjectFrame> ObjectFrameReceived;
            public void SetEngineUserState(string key, string value) { }
            public event Action<string, string, string, bool> EngineUserStateChanged;
            public event Action<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> EngineUserStateSnapshot;
            public event Action<string> EngineUserStateRemoved;
            public void UpdateManifest(ObjectManifest manifest) { }
            public void Leave() { }
            public void Join() { }
            public void Rejoin() { }
            public Task<bool> ClearRoomStateAsync() => Task.FromResult(false);
        }

        internal sealed class FakeHost : ILocalHost
        {
            readonly Dictionary<Type, object> _services = new Dictionary<Type, object>();

            public FakeHost(ILocalSession session)
            {
                Session = session;
            }

            public BSScene Scene => null;
            public ILocalSession Session { get; }
            public LocalIdentity Identity { get; } = new LocalIdentity();
            public LocalMultiplayerSettings Settings { get; } = new LocalMultiplayerSettings();
            public ILocalDiagnostics Diagnostics => null;
            public GameObject HostObject => null;
            public Transform RemotesRoot => null;
            public bool PublishingLive => true;
            public event Action<bool> PublishingChanged;
            public int SessionEpoch => 0;
            public void Provide<T>(T service) where T : class => _services[typeof(T)] = service;
            public T Get<T>() where T : class => _services.TryGetValue(typeof(T), out var service) ? service as T : null;
        }
#pragma warning restore CS0067
    }
}
