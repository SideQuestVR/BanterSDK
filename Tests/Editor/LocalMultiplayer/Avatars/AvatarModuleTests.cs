using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BS.LocalMultiplayer.Avatars;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BS.LocalMultiplayer.Tests
{
    /// <summary>
    /// The avatar module's wiring to the session: the services it provides, which orb a pose frame reaches, and the
    /// orbs it lets go of when a player or the whole session leaves (edit mode, without a desktop player).
    /// </summary>
    public class AvatarModuleTests
    {
        TestSession _session;
        TestHost _host;
        AvatarModule _module;
        readonly List<Object> _created = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            _session = new TestSession { OwnRoomSessionId = "rsess_me" };
            var hostObject = new GameObject("[LocalMultiplayer]");
            _created.Add(hostObject);
            _host = new TestHost(_session, hostObject);
            _module = hostObject.AddComponent<AvatarModule>();
            _module.Install(_host);
        }

        [TearDown]
        public void TearDown()
        {
            _module.Uninstall();
            foreach (var created in _created)
            {
                if (created != null) Object.DestroyImmediate(created);
            }
            _created.Clear();
        }

        [Test]
        public void Install_ProvidesTheAvatarFactoryAndTheBoneSource()
        {
            Assert.That(_host.Get<IRemoteAvatarFactory>(), Is.SameAs(_module));
            Assert.That(_host.Get<IRemoteBoneSource>(), Is.SameAs(_module));
        }

        [Test]
        public void AFrame_ReachesItsSendersOrb()
        {
            var root = BuildRemote("rsess_a", "peer_a");
            double now = MachineClock.NowMs;
            _session.RaiseFrame("rsess_a", Frame(new Vector3(2f, 0f, 7f), now));
            Assert.That(_module.TryGetAvatar("rsess_a", out var avatar), Is.True);
            avatar.Tick(now);
            Assert.That(root.transform.position, Is.EqualTo(new Vector3(2f, 0f, 7f)));
            Assert.That(_module.UnroutedFrames, Is.EqualTo(0));
        }

        [Test]
        public void OwnAndUnknownFrames_AreDropped()
        {
            BuildRemote("rsess_a", "peer_a");
            double now = MachineClock.NowMs;
            _session.RaiseFrame("rsess_me", Frame(Vector3.one, now));
            Assert.That(_module.UnroutedFrames, Is.EqualTo(0), "our own frames are not routed at all");
            _session.RaiseFrame("rsess_x", Frame(Vector3.one, now));
            Assert.That(_module.UnroutedFrames, Is.EqualTo(1));
        }

        [Test]
        public void PeerLeft_LetsGoOfTheirOrb_AndHidesIt()
        {
            BuildRemote("rsess_a", "peer_a");
            _module.TryGetAvatar("rsess_a", out var avatar);
            double now = MachineClock.NowMs;
            _session.RaiseFrame("rsess_a", Frame(Vector3.zero, now));
            avatar.Tick(now);
            avatar.Applier.Tick(0.016f);
            Assert.That(avatar.IsBodyVisible, Is.True);
            Assert.That(_module.TryGetRemoteBone("rsess_a", HumanBodyBones.Head, out _), Is.True);

            _session.RaisePeerLeft(new LocalPeer { RoomSessionId = "rsess_a", PeerId = "peer_a" });
            Assert.That(_module.TryGetAvatar("rsess_a", out _), Is.False);
            Assert.That(_module.TryGetRemoteBone("rsess_a", HumanBodyBones.Head, out _), Is.False);
            foreach (var renderer in avatar.Orb.Renderers)
            {
                Assert.That(renderer.enabled, Is.False, renderer.name);
            }
        }

        [Test]
        public void PeerLeft_OfAnOlderConnection_KeepsTheReplacement()
        {
            BuildRemote("rsess_a", "peer_new");
            _session.RaisePeerLeft(new LocalPeer { RoomSessionId = "rsess_a", PeerId = "peer_old" });
            Assert.That(_module.TryGetAvatar("rsess_a", out _), Is.True);
        }

        [Test]
        public void RoomLeft_LetsGoOfEveryOrb()
        {
            BuildRemote("rsess_a", "peer_a");
            BuildRemote("rsess_b", "peer_b");
            _session.RaiseRoomLeft();
            Assert.That(_module.TryGetAvatar("rsess_a", out _), Is.False);
            Assert.That(_module.TryGetAvatar("rsess_b", out _), Is.False);
            Assert.That(_module.SetPilotSeat("rsess_a", _host.HostObject.transform, Vector3.zero, Quaternion.identity), Is.False);
        }

        [Test]
        public void Uninstall_StopsListening()
        {
            BuildRemote("rsess_a", "peer_a");
            _module.Uninstall();
            Assert.That(_session.ListenerCount, Is.EqualTo(0));
            Assert.That(_host.PublishingListenerCount, Is.EqualTo(0));
        }

        GameObject BuildRemote(string roomSessionId, string peerId)
        {
            var root = new GameObject("[Remote]");
            _created.Add(root);
            var head = _module.BuildAvatar(new LocalPeer
            {
                RoomSessionId = roomSessionId,
                PeerId = peerId,
                ClientId = "local:Player 2",
                DisplayName = "Player 2",
                Tint = Color.green
            }, root.transform);
            Assert.That(head, Is.Not.Null);
            return root;
        }

        static ParticipantFrame Frame(Vector3 position, double t) => new ParticipantFrame
        {
            Seq = 0,
            T = t,
            ValidForMs = 33,
            Discontinuity = true,
            Position = position,
            Rotation = Quaternion.identity,
            HasVelocity = true,
            Velocity = Vector3.zero,
            HasExt = true,
            Ext = PoseExtCodec.RestPose(position + new Vector3(0f, 0.95f, 0f), Quaternion.identity)
        };

        // ------------------------------------------------------------------ fakes

        sealed class TestHost : ILocalHost
        {
            readonly Dictionary<Type, object> _services = new Dictionary<Type, object>();
            Action<bool> _publishingChanged;

            public TestHost(ILocalSession session, GameObject hostObject)
            {
                Session = session;
                HostObject = hostObject;
            }

            public BSScene Scene => null;
            public ILocalSession Session { get; }
            public LocalIdentity Identity { get; } = new LocalIdentity { Tint = Color.blue };
            public LocalMultiplayerSettings Settings { get; } = new LocalMultiplayerSettings();
            public ILocalDiagnostics Diagnostics { get; } = new TestDiagnostics();
            public GameObject HostObject { get; }
            public Transform RemotesRoot => HostObject.transform;
            public bool PublishingLive => false;
            public int SessionEpoch => 1;
            public int PublishingListenerCount => _publishingChanged == null ? 0 : _publishingChanged.GetInvocationList().Length;

            public event Action<bool> PublishingChanged
            {
                add => _publishingChanged += value;
                remove => _publishingChanged -= value;
            }

            public void Provide<T>(T service) where T : class => _services[typeof(T)] = service;
            public T Get<T>() where T : class => _services.TryGetValue(typeof(T), out var service) ? (T)service : null;
        }

        sealed class TestDiagnostics : ILocalDiagnostics
        {
            readonly List<DiagnosticEntry> _entries = new List<DiagnosticEntry>();
            public IReadOnlyList<DiagnosticEntry> Entries => _entries;
            public event Action Changed { add { } remove { } }
            public void Set(string key, DiagnosticLevel level, string message)
            {
                _entries.RemoveAll(e => e.Key == key);
                _entries.Add(new DiagnosticEntry { Key = key, Level = level, Message = message });
            }
            public void Clear(string key) => _entries.RemoveAll(e => e.Key == key);
            public void ClearPrefix(string prefix) => _entries.RemoveAll(e => e.Key.StartsWith(prefix, StringComparison.Ordinal));
        }

        sealed class TestSession : ILocalSession
        {
            Action<string, ParticipantFrame> _frames;
            Action<LocalPeer> _peerLeft;
            Action _roomJoined;
            Action _roomLeft;

            public string OwnRoomSessionId { get; set; }
            public int ListenerCount => Count(_frames) + Count(_peerLeft) + Count(_roomJoined) + Count(_roomLeft);
            static int Count(Delegate d) => d == null ? 0 : d.GetInvocationList().Length;

            public void RaiseFrame(string from, ParticipantFrame frame) => _frames?.Invoke(from, frame);
            public void RaisePeerLeft(LocalPeer peer) => _peerLeft?.Invoke(peer);
            public void RaiseRoomLeft() => _roomLeft?.Invoke();

            public event Action<string, ParticipantFrame> ParticipantFrameReceived { add => _frames += value; remove => _frames -= value; }
            public event Action<LocalPeer> PeerLeft { add => _peerLeft += value; remove => _peerLeft -= value; }
            public event Action RoomJoined { add => _roomJoined += value; remove => _roomJoined -= value; }
            public event Action RoomLeft { add => _roomLeft += value; remove => _roomLeft -= value; }

            public SessionState State => SessionState.Joined;
            public event Action<SessionState> StateChanged { add { } remove { } }
            public string RoomId => "s-test";
            public string OwnPeerId => "peer_me";
            public string RelayUri => "ws://localhost:42068/__sq-relay";
            public double RttMs => 0;
            public string LastError => "";
            public HubInfo Hub { get; } = new HubInfo();
            public IReadOnlyCollection<LocalPeer> Peers => Array.Empty<LocalPeer>();
            public bool TryGetPeer(string roomSessionId, out LocalPeer peer) { peer = null; return false; }
            public event Action<LocalPeer> PeerJoined { add { } remove { } }
            public event Action<LocalPeer> PeerManifestChanged { add { } remove { } }
            public Task<JObject> RequestAsync(string type, JObject body, CancellationToken cancellationToken = default)
                => Task.FromException<JObject>(new LocalRelayException("not_connected"));
            public bool Send(string type, JObject body) => false;
            public void On(string type, Action<JObject> handler) { }
            public void Off(string type, Action<JObject> handler) { }
            public void SendParticipantFrame(in ParticipantFrame frame) { }
            public void QueueObjectFrame(in ObjectFrame frame) { }
            public event Action<string, ObjectFrame> ObjectFrameReceived { add { } remove { } }
            public void SetEngineUserState(string key, string value) { }
            public event Action<string, string, string, bool> EngineUserStateChanged { add { } remove { } }
            public event Action<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> EngineUserStateSnapshot { add { } remove { } }
            public event Action<string> EngineUserStateRemoved { add { } remove { } }
            public void UpdateManifest(ObjectManifest manifest) { }
            public void Leave() { }
            public void Join() { }
            public void Rejoin() { }
            public Task<bool> ClearRoomStateAsync() => Task.FromResult(false);
        }
    }
}
