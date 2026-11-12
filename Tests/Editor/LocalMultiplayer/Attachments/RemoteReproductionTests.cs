using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using BS.LocalMultiplayer.Attachments;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BS.LocalMultiplayer.Tests
{
    /// <summary>
    /// Peers' attachments and seats reproduced on their avatars (the client's AttachmentNetworkBridge and
    /// AttachmentsSystem), built in edit mode with stand-in orbs. A peer whose socket closes keeps their user state on
    /// the relay for the 30 s they may resume in, and a resume sends nothing again: what they wore drops in place and
    /// goes back on the orb the resume builds, and their seat stays taken meanwhile. A release drops an object under the
    /// parent it had when it was put on.
    /// </summary>
    public class RemoteReproductionTests
    {
        const string Me = "rsess_me";
        const string Other = "rsess_other";

        BridgeSession _session;
        LocalObjectBinderTests.FakeHost _host;
        FakeBones _bones;
        BSScene _scene;
        LocalAttachmentsSystem _system;
        LocalAttachmentBridge _bridge;
        Func<BSSeat, bool> _savedResolver;
        readonly List<GameObject> _created = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            _savedResolver = BSSeat.OccupiedByOtherResolver;
            _session = new BridgeSession();
            _host = new LocalObjectBinderTests.FakeHost(_session);
            _bones = new FakeBones();
            _host.Provide<IRemoteBoneSource>(_bones);
            _scene = SceneWithObjects();
            var diagnostics = new AttachmentDiagnostics(null);
            _system = new LocalAttachmentsSystem(_host, diagnostics);
            _system.Install(_scene);
            _bridge = new LocalAttachmentBridge(_host, _system, diagnostics);
            _bridge.Start(_scene, _session);
        }

        [TearDown]
        public void TearDown()
        {
            _bridge.Stop();
            _system.TearDown();
            foreach (var go in _created)
            {
                if (go != null) Object.DestroyImmediate(go);
            }
            _created.Clear();
            BSSeat.OccupiedByOtherResolver = _savedResolver;
        }

        // Only what the bridge reads: the object registry and the users. No constructor, so no page, input or global
        // scene is touched.
        static BSScene SceneWithObjects()
        {
            var scene = (BSScene)FormatterServices.GetUninitializedObject(typeof(BSScene));
            var objects = typeof(BSScene).GetField("objects", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(objects, "BSScene.objects is gone: update this test");
            objects.SetValue(scene, new ConcurrentDictionary<int, UnityAndBanterObject>());
            scene.users = new List<UserData>();
            return scene;
        }

        GameObject Add(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            else _created.Add(go);
            return go;
        }

        // A scene object with a stable BSObjectId, as every player loads it.
        GameObject AddSceneObject(string id)
        {
            var go = Add(id);
            var objectId = go.GetComponent<BSObjectId>();
            if (objectId == null) objectId = go.AddComponent<BSObjectId>();
            objectId.Id = id;
            _scene.AddBanterObject(go, objectId, skipChangeFlush: true);
            return go;
        }

        BSSeat AddSceneSeat(string id)
        {
            var go = Add(id);
            var seat = go.AddComponent<BSSeat>();
            var objectId = go.GetComponent<BSObjectId>();
            objectId.Id = id;
            _scene.AddBanterObject(go, objectId, skipChangeFlush: true);
            return seat;
        }

        static BSAttachment LeftHandHat() => new BSAttachment
        {
            attachmentType = AttachmentType.NonPhysics,
            avatarAttachmentType = AvatarAttachmentType.AttachToAvatar,
            avatarAttachmentPoint = AvatarBoneName.LEFTARM_HAND,
            physicsAttachmentPoint = PhysicsAttachmentPoint.Head,
            jointAvatar = true,
            attachmentPosition = new Vector3(0.1f, -0.2f, 0.3f),
            attachmentRotation = Quaternion.identity,
        };

        // The other player wears the hat and sits on the chair, on the orb whose hand is returned.
        Transform WearAndSit(GameObject hat, BSSeat chair)
        {
            var hand = Add("their hand").transform;
            _bones.Orbs[Other] = hand;
            _session.RaiseUserState(Other, AttachmentWireCodec.KeyPrefix + hat.name, AttachmentWireCodec.Serialize(LeftHandHat()));
            _session.RaiseUserState(Other, AttachmentWireCodec.PilotKey,
                AttachmentWireCodec.SerializePilot(chair.name, new Vector3(0f, 0.4f, 0f), Quaternion.identity));
            Assert.AreSame(hand, hat.transform.parent, "worn on their orb");
            Assert.AreSame(chair.transform, _bones.SeatOf(Other), "their orb sits on the chair");
            Assert.IsTrue(chair.IsOccupiedByOther);
            return hand;
        }

        // Their socket closes: the relay announces they left, and their orb goes with them.
        void DropTheirSocket()
        {
            _bones.Orbs.Remove(Other);
            _bones.Seats.Remove(Other);
            _session.RaisePeerLeft(Other);
        }

        [Test]
        public void ADroppedPeer_GetsTheirObjectsAndSeatBack_WhenTheyResume()
        {
            var hat = AddSceneObject("hat");
            var chair = AddSceneSeat("chair");
            WearAndSit(hat, chair);

            DropTheirSocket();
            Assert.IsNull(hat.transform.parent, "the hat drops in place when their orb goes");
            Assert.IsTrue(chair.IsOccupiedByOther, "their seat stays taken while they may resume");
            _bridge.Update();
            Assert.AreEqual(1, _bridge.PendingAttachmentCount, "the hat waits for their orb");
            Assert.AreEqual(1, _bridge.PendingPilotCount, "so does their seat");

            // They resume under the same room session; the relay sends nothing again.
            var newHand = Add("their new hand").transform;
            _bones.Orbs[Other] = newHand;
            _bridge.Update();
            Assert.AreSame(newHand, hat.transform.parent, "worn on their new orb");
            Assert.AreSame(chair.transform, _bones.SeatOf(Other), "their new orb sits on the chair");
            Assert.AreEqual(0, _bridge.PendingAttachmentCount);
            Assert.AreEqual(0, _bridge.PendingPilotCount);
        }

        [Test]
        public void ADroppedPeerWhoseSessionEnds_IsForgotten()
        {
            var hat = AddSceneObject("hat");
            var chair = AddSceneSeat("chair");
            WearAndSit(hat, chair);

            DropTheirSocket();
            _session.RaiseUserStateRemoved(Other); // the relay's 30 s ran out
            _bridge.Update();
            Assert.AreEqual(0, _bridge.PendingAttachmentCount);
            Assert.AreEqual(0, _bridge.PendingPilotCount);
            Assert.IsFalse(chair.IsOccupiedByOther, "the seat is free");

            _bones.Orbs[Other] = Add("a stray orb").transform;
            _bridge.Update();
            Assert.IsNull(hat.transform.parent, "nothing is put back on");
            Assert.IsNull(_bones.SeatOf(Other));
        }

        [Test]
        public void ADroppedPeer_WhoTookTheirHatOffMeanwhile_DoesNotGetItBack()
        {
            var hat = AddSceneObject("hat");
            var chair = AddSceneSeat("chair");
            WearAndSit(hat, chair);

            DropTheirSocket();
            _session.RaiseUserState(Other, AttachmentWireCodec.KeyPrefix + "hat", "");
            _session.RaiseUserState(Other, AttachmentWireCodec.PilotKey, "");
            _bones.Orbs[Other] = Add("their new hand").transform;
            _bridge.Update();
            Assert.IsNull(hat.transform.parent);
            Assert.IsNull(_bones.SeatOf(Other));
            Assert.IsFalse(chair.IsOccupiedByOther);
        }

        [Test]
        public void AReleasedObject_DropsUnderTheParentItHadWhenPutOn()
        {
            var shelf = Add("shelf").transform;
            var table = Add("table").transform;
            var hat = Add("hat", shelf);
            var hatId = hat.AddComponent<BSObjectId>();
            hatId.Id = "hat";
            var hand = Add("their hand").transform;
            var data = LeftHandHat();
            data.attachedObject = new UnityAndBanterObject { gameObject = hat, id = hatId };

            _system.ApplyRemoteAttachment("hat", data, hand);
            _system.RemoveRemoteAttachment("hat"); // they took it off: it drops back under the shelf
            Assert.AreSame(shelf, hat.transform.parent);

            hat.transform.SetParent(table, true); // the world moves it
            _system.ApplyRemoteAttachment("hat", data, hand);
            Assert.AreSame(hand, hat.transform.parent);
            Assert.IsTrue(_system.ReleaseRemoteAttachment("hat"), "a live reproduction was released");
            Assert.AreSame(table, hat.transform.parent, "dropped under the table, where it was put on from");
            Assert.IsFalse(_system.ReleaseRemoteAttachment("hat"), "nothing is left to release");
        }

        // ─── Fakes ──────────────────────────────────────────────────────────────

        // The orbs: one transform per peer stands in for every bone; a seat glue is remembered while the orb exists.
        sealed class FakeBones : IRemoteBoneSource
        {
            public readonly Dictionary<string, Transform> Orbs = new Dictionary<string, Transform>(StringComparer.Ordinal);
            public readonly Dictionary<string, Transform> Seats = new Dictionary<string, Transform>(StringComparer.Ordinal);

            public Transform SeatOf(string roomSessionId) => Seats.TryGetValue(roomSessionId, out var seat) ? seat : null;

            public bool TryGetRemoteBone(string roomSessionId, HumanBodyBones bone, out Transform result)
            {
                result = null;
                return roomSessionId != null && Orbs.TryGetValue(roomSessionId, out result) && result != null;
            }

            public Transform ResolveHeldAnchor(string roomSessionId, string anchorKey) => null;

            public bool SetPilotSeat(string roomSessionId, Transform seat, Vector3 hipLocalPosition, Quaternion hipLocalRotation)
            {
                if (roomSessionId == null || !Orbs.ContainsKey(roomSessionId)) return seat == null;
                if (seat == null) Seats.Remove(roomSessionId);
                else Seats[roomSessionId] = seat;
                return true;
            }
        }

#pragma warning disable CS0067 // events the bridge doesn't use
        // A joined session whose user-state and peer events the test raises.
        sealed class BridgeSession : ILocalSession
        {
            public SessionState State => SessionState.Joined;
            public event Action<SessionState> StateChanged;
            public string RoomId => "s-test";
            public string OwnRoomSessionId => Me;
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
                => Task.FromException<JObject>(new LocalRelayException("unsupported"));
            public bool Send(string type, JObject body) => false;
            public void On(string type, Action<JObject> handler) { }
            public void Off(string type, Action<JObject> handler) { }
            public void SendParticipantFrame(in ParticipantFrame frame) { }
            public event Action<string, ParticipantFrame> ParticipantFrameReceived;
            public void QueueObjectFrame(in ObjectFrame frame) { }
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

            public void RaiseUserState(string roomSessionId, string path, string value)
                => EngineUserStateChanged?.Invoke(roomSessionId, path, value, false);
            public void RaiseUserStateRemoved(string roomSessionId) => EngineUserStateRemoved?.Invoke(roomSessionId);
            public void RaisePeerLeft(string roomSessionId)
                => PeerLeft?.Invoke(new LocalPeer { RoomSessionId = roomSessionId, PeerId = "peer_" + roomSessionId });
        }
#pragma warning restore CS0067
    }
}
