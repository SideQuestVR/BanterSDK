using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace BS.LocalMultiplayer.Tests
{
    /// <summary>
    /// An <see cref="ILocalSession"/> that records what the state code sends and lets a test play the relay: canned
    /// replies (<see cref="Reply"/>; null leaves the request pending), pushed messages (<see cref="Emit"/>) and the
    /// session events. Requests and sends obey the contract: "not_connected" / dropped unless joined.
    /// </summary>
    internal sealed class FakeLocalSession : ILocalSession
    {
        public struct Recorded
        {
            public string Type;
            public JObject Body;
        }

        readonly Dictionary<string, List<Action<JObject>>> _handlers = new Dictionary<string, List<Action<JObject>>>();
        readonly Dictionary<string, LocalPeer> _peers = new Dictionary<string, LocalPeer>();

        public readonly List<Recorded> Requests = new List<Recorded>();
        public readonly List<Recorded> Sent = new List<Recorded>();

        /// <summary>The relay's answer to a request, or null to leave it pending.</summary>
        public Func<string, JObject, JObject> Reply;

        /// <summary>When it returns an exception, the request fails with it instead (a socket drop, a timeout).</summary>
        public Func<string, JObject, Exception> Fault;

        public SessionState State { get; set; } = SessionState.Joined;
        public event Action<SessionState> StateChanged;
        public string RoomId { get; set; } = "s-test";
        public string OwnRoomSessionId { get; set; } = "rsess_me";
        public string OwnPeerId { get; set; } = "peer_me";
        public string RelayUri => "ws://localhost:42068/__sq-relay";
        public double RttMs => 0;
        public string LastError => "";
        public HubInfo Hub { get; } = new HubInfo();

        public event Action RoomJoined;
        public event Action RoomLeft;

        public IReadOnlyCollection<LocalPeer> Peers => _peers.Values;
        public bool TryGetPeer(string roomSessionId, out LocalPeer peer) => _peers.TryGetValue(roomSessionId ?? "", out peer);
        public event Action<LocalPeer> PeerJoined { add { } remove { } }
        public event Action<LocalPeer> PeerLeft { add { } remove { } }
        public event Action<LocalPeer> PeerManifestChanged { add { } remove { } }

        public Task<JObject> RequestAsync(string type, JObject body, CancellationToken cancellationToken = default)
        {
            Requests.Add(new Recorded { Type = type, Body = body });
            if (State != SessionState.Joined)
            {
                return Task.FromException<JObject>(new LocalRelayException("not_connected"));
            }
            var fault = Fault?.Invoke(type, body);
            if (fault != null)
            {
                return Task.FromException<JObject>(fault);
            }
            var reply = Reply?.Invoke(type, body);
            return reply != null ? Task.FromResult(reply) : new TaskCompletionSource<JObject>().Task;
        }

        public bool Send(string type, JObject body)
        {
            if (State != SessionState.Joined)
            {
                return false;
            }
            Sent.Add(new Recorded { Type = type, Body = body });
            return true;
        }

        public void On(string type, Action<JObject> handler)
        {
            if (!_handlers.TryGetValue(type, out var list))
            {
                list = new List<Action<JObject>>();
                _handlers[type] = list;
            }
            list.Add(handler);
        }

        public void Off(string type, Action<JObject> handler)
        {
            if (_handlers.TryGetValue(type, out var list))
            {
                list.Remove(handler);
            }
        }

        public int HandlerCount(string type) => _handlers.TryGetValue(type, out var list) ? list.Count : 0;

        /// <summary>Delivers a relay message to every handler of its type, as the session's dispatch does.</summary>
        public void Emit(string type, JObject message)
        {
            message["type"] = type;
            if (!_handlers.TryGetValue(type, out var list))
            {
                return;
            }
            foreach (var handler in list.ToArray())
            {
                handler(message);
            }
        }

        public void RaiseRoomJoined() => RoomJoined?.Invoke();
        public void RaiseRoomLeft() => RoomLeft?.Invoke();

        public void SetState(SessionState state)
        {
            State = state;
            StateChanged?.Invoke(state);
        }

        public void SendParticipantFrame(in ParticipantFrame frame) { }
        public event Action<string, ParticipantFrame> ParticipantFrameReceived { add { } remove { } }
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
