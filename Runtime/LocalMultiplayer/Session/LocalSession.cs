// <mirror source="Packages/com.sidequest.packetparty/Runtime/PacketPartyClient.cs" sha256="6ab88cbf53c894dcdd8273685d80972419478499cfec5f09de9499a52b97367d" mode="port" />
// <mirror source="Packages/com.sidequest.packetparty/Runtime/Signaling/PacketPartySignaling.cs" sha256="d6f5b8c336353a6bca23abc1482305522d36a9bf4a712e852332a9e7b5c7ada7" mode="port" />
// <mirror source="Packages/com.sidequest.packetparty/Runtime/Scene/PacketPartySceneBinder.cs" sha256="8c31a15d5a4478ad23cd2b9ec946426e85a850318e0f9f8c14496682128ead3f" mode="port" />
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace BS.LocalMultiplayer
{
    /// <summary>What the session's Leave, Join and Rejoin hand over to (the lifecycle module).</summary>
    internal interface ILocalSessionController
    {
        void Leave();
        void Join();
        void Rejoin();
    }

    /// <summary>A reply with the times its request left and its answer arrived (PacketPartySignaling's TimedSignalResponse).</summary>
    internal readonly struct TimedReply
    {
        public readonly JObject Body;
        /// <summary><see cref="MachineClock.NowMs"/> right before the socket write.</summary>
        public readonly double SentAtMs;
        /// <summary><see cref="MachineClock.NowMs"/> when the whole reply frame arrived.</summary>
        public readonly double ReceivedAtMs;

        public TimedReply(JObject body, double sentAtMs, double receivedAtMs)
        {
            Body = body;
            SentAtMs = sentAtMs;
            ReceivedAtMs = receivedAtMs;
        }
    }

    /// <summary>
    /// The relay session, standing in for PacketPartyClient: one room session over one relay socket at a
    /// time. It joins with hello and raises <see cref="RoomJoined"/> at room.ready, after the snapshots
    /// (PacketPartyClient.ConnectAsync). An unexpected drop keeps the room and its peers and resumes the same
    /// session within the relay's 30 s grace (ReconnectLoopAsync); a resume the relay no longer knows comes
    /// back as a fresh session, which ends the old one. An intentional disconnect just closes the socket, as
    /// DisconnectAsync does: no leave message, so the relay's object and user-state graces apply. It never
    /// gives up while Play runs (RELAY.md 9). Everything here, every event and every On(type) handler, runs
    /// on the main thread, in the order the messages arrived; only the socket and the JSON parsing are not.
    /// </summary>
    internal sealed class LocalSession : ILocalSession
    {
        public const float RequestTimeoutSeconds = 10f;
        /// <summary>From an open socket to room.ready; a relay that never answers the hello is not a relay.</summary>
        public const float JoinTimeoutSeconds = 10f;
        public const int MaxInboundPerFrame = 2000;
        /// <summary>RELAY.md 9: a hub that refuses the hello (4000, 4001, 4003, 4004) or the upgrade is retried every 5 s.</summary>
        public const float RejectedRetrySeconds = 5f;
        static readonly float[] BackoffSeconds = { 0.5f, 1f, 2f, 4f, 5f };
        const float BackoffJitterSeconds = 0.25f;

        sealed class PendingRequest
        {
            public readonly TaskCompletionSource<JObject> Tcs =
                new TaskCompletionSource<JObject>(TaskCreationOptions.RunContinuationsAsynchronously);
            public readonly string Type;
            public readonly float Deadline;
            public double SentAtMs = double.NaN;
            public double ReceivedAtMs = double.NaN;
            public CancellationTokenRegistration Registration;
            public readonly Action<double> MarkSending;

            public PendingRequest(string type, float deadline)
            {
                Type = type;
                Deadline = deadline;
                // Written by the send thread, read on the main thread once the reply arrived.
                MarkSending = now => Volatile.Write(ref SentAtMs, now);
            }

            public void Resolve(JObject body, double receivedAtMs)
            {
                ReceivedAtMs = receivedAtMs;
                Registration.Dispose();
                Tcs.TrySetResult(body);
            }

            public void Fail(Exception error)
            {
                Registration.Dispose();
                Tcs.TrySetException(error);
                // Requests fail when the session closes, often as Play ends: the caller's continuation may never run
                // before the domain unloads, so mark the failure observed here (the caller still gets it).
                _ = Tcs.Task.Exception;
            }
        }

        readonly LocalIdentity _identity;
        readonly ILocalDiagnostics _diagnostics;
        readonly string _sdkVersion;
        readonly Func<int> _port;
        readonly ConcurrentQueue<RelayInbound> _inbound = new ConcurrentQueue<RelayInbound>();
        readonly Dictionary<string, Action<JObject>[]> _handlers = new Dictionary<string, Action<JObject>[]>(StringComparer.Ordinal);
        readonly Dictionary<string, PendingRequest> _pending = new Dictionary<string, PendingRequest>(StringComparer.Ordinal);
        readonly List<string> _expired = new List<string>();
        readonly Dictionary<string, LocalPeer> _peers = new Dictionary<string, LocalPeer>(StringComparer.Ordinal);
        // Who the relay says is here before room.ready: announced together at room.ready, as the scene binder
        // builds every present peer's view in OnRoomJoined.
        readonly Dictionary<string, LocalPeer> _roster = new Dictionary<string, LocalPeer>(StringComparer.Ordinal);
        readonly List<LocalPeer> _rosterScratch = new List<LocalPeer>();
        readonly List<LocalPeer> _leftScratch = new List<LocalPeer>();
        readonly EngineUserStateChannel _engineState;
        readonly System.Random _random = new System.Random();
        readonly StringBuilder _frameText = new StringBuilder(1024);
        readonly Dictionary<string, ObjectFrame> _pendingObjects = new Dictionary<string, ObjectFrame>(StringComparer.Ordinal);
        readonly List<string> _sentObjects = new List<string>();
        readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        readonly HubInfo _hub;

        RelayConnection _connection;
        int _connectionIds;
        bool _wantJoined;
        bool _hasSession;
        bool _shutdown;
        string _resumeSessionId;
        string _resumeToken;
        float _retryAt = -1f;
        int _backoffAttempt;
        float _joinDeadline = -1f;
        float _nextTimeoutSweep;
        TaskCompletionSource<bool> _connectWaiter;
        long _requestCounter;
        long _userStateRevision;
        ObjectManifest _manifest = new ObjectManifest();
        string _manifestJson;
        string _manifestSentJson;
        bool _hasParticipant;
        ParticipantFrame _participant;
        JObject _lastRelayError;
        string _lastLoggedFailure;
        bool _warnedOrphanHub;
        double _currentReceivedAtMs = double.NaN;

        public LocalSession(LocalIdentity identity, ILocalDiagnostics diagnostics, string sdkVersion, Func<int> port)
        {
            _identity = identity ?? throw new ArgumentNullException(nameof(identity));
            _diagnostics = diagnostics;
            _sdkVersion = sdkVersion ?? "";
            _port = port ?? (() => RelayProtocol.DefaultPort);
            _hub = HubProbe.Last;
            _engineState = new EngineUserStateChannel((body, cancellation) => RequestAsync(RelayProtocol.UserStateBatch, body, cancellation));
            _manifestJson = RelayProtocol.ManifestJson(_manifest).ToString(Formatting.None);
        }

        // ---------------------------------------------------------------- ILocalSession: state

        public SessionState State { get; private set; } = SessionState.Idle;
        public event Action<SessionState> StateChanged;
        public string RoomId { get; private set; }
        public string OwnRoomSessionId { get; private set; }
        public string OwnPeerId { get; private set; }
        public string RelayUri { get; private set; } = "";
        public double RttMs { get; private set; } = double.NaN;
        public string LastError { get; private set; }
        public HubInfo Hub => _hub;

        public event Action RoomJoined;
        public event Action RoomLeft;

        public IReadOnlyCollection<LocalPeer> Peers => _peers.Values;
        public event Action<LocalPeer> PeerJoined;
        public event Action<LocalPeer> PeerLeft;
        public event Action<LocalPeer> PeerManifestChanged;

        public event Action<string, ParticipantFrame> ParticipantFrameReceived;
        public event Action<string, ObjectFrame> ObjectFrameReceived;

        public event Action<string, string, string, bool> EngineUserStateChanged;
        public event Action<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> EngineUserStateSnapshot;
        public event Action<string> EngineUserStateRemoved;

        public bool TryGetPeer(string roomSessionId, out LocalPeer peer)
        {
            peer = null;
            return !string.IsNullOrEmpty(roomSessionId) && _peers.TryGetValue(roomSessionId, out peer);
        }

        /// <summary>Changes whenever a room session starts or ends (ILocalHost.SessionEpoch).</summary>
        internal int Epoch { get; private set; }

        /// <summary>Leave, Join and Rejoin go here when set (the lifecycle module: it pauses publishing and
        /// reloads the page).</summary>
        internal ILocalSessionController Controller { get; set; }

        /// <summary>While an On(type) handler runs: when its message arrived (<see cref="MachineClock.NowMs"/>).</summary>
        internal double CurrentMessageReceivedAtMs => _currentReceivedAtMs;

        internal bool WantsRoom => _wantJoined;

        // ---------------------------------------------------------------- joining and leaving

        /// <summary>
        /// Joins <paramref name="roomId"/> as a fresh session, ending the current one first
        /// (NetworkService.JoinRoomAsync → PacketPartyClient.ConnectAsync). Resolves true at room.ready, false
        /// when the first attempt fails or a disconnect cancels it; a failed join keeps retrying in the
        /// background.
        /// </summary>
        internal Task<bool> ConnectAsync(string roomId)
        {
            if (_shutdown) return Task.FromResult(false);
            if (_wantJoined || _connection != null || _hasSession)
            {
                Disconnect();
            }
            RoomId = string.IsNullOrEmpty(roomId) ? LocalRoomId.Untitled : roomId;
            _wantJoined = true;
            _backoffAttempt = 0;
            _lastLoggedFailure = null;
            var waiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _connectWaiter = waiter;
            StartAttempt();
            return waiter.Task;
        }

        /// <summary>
        /// Closes the socket and stays out (PacketPartyClient.DisconnectAsync): no leave message, the resume
        /// token is forgotten, pending requests fail with "closed", and <see cref="RoomLeft"/> fires if a
        /// session existed.
        /// </summary>
        internal void Disconnect()
        {
            _wantJoined = false;
            _retryAt = -1f;
            _joinDeadline = -1f;
            var connection = _connection;
            _connection = null;
            connection?.Close(immediate: false);
            FailAllPending(RelayProtocol.ErrorClosed, "the session was closed");
            _resumeSessionId = null;
            _resumeToken = null;
            CompleteConnectWaiter(false);
            EndSession(raiseEvents: true);
            SetState(SessionState.Idle);
        }

        /// <summary>Host teardown: abort or close the socket and go quiet. No events are raised.</summary>
        internal void Shutdown(bool immediate)
        {
            if (_shutdown) return;
            _shutdown = true;
            _wantJoined = false;
            _retryAt = -1f;
            var connection = _connection;
            _connection = null;
            connection?.Close(immediate);
            FailAllPending(RelayProtocol.ErrorClosed, "local multiplayer stopped");
            CompleteConnectWaiter(false);
            EndSession(raiseEvents: false);
            try
            {
                _lifetime.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
            while (_inbound.TryDequeue(out _))
            {
            }
            _handlers.Clear();
            StateChanged = null;
            RoomJoined = null;
            RoomLeft = null;
            PeerJoined = null;
            PeerLeft = null;
            PeerManifestChanged = null;
            ParticipantFrameReceived = null;
            ObjectFrameReceived = null;
            EngineUserStateChanged = null;
            EngineUserStateSnapshot = null;
            EngineUserStateRemoved = null;
            State = SessionState.Idle;
        }

        public void Leave()
        {
            if (Controller != null)
            {
                Controller.Leave();
                return;
            }
            Disconnect();
        }

        public void Join()
        {
            if (Controller != null)
            {
                Controller.Join();
                return;
            }
            if (!_wantJoined)
            {
                _ = ConnectAsync(!string.IsNullOrEmpty(RoomId) ? RoomId : LocalRoomId.ForActiveScene());
            }
        }

        public void Rejoin()
        {
            if (Controller != null)
            {
                Controller.Rejoin();
                return;
            }
            Debug.LogWarning("[LocalMP] Rejoin reloads the page, which needs the lifecycle module; joining again instead.");
            Disconnect();
            Join();
        }

        public async Task<bool> ClearRoomStateAsync()
        {
            try
            {
                var reply = await RequestAsync(RelayProtocol.AdminClearRoomState, new JObject());
                if (RelayProtocol.Bool(reply, "ok")) return true;
                Debug.LogWarning($"[LocalMP] The relay refused to clear the room state: {RelayProtocol.Str(reply, "error")}");
                return false;
            }
            catch (LocalRelayException e)
            {
                Debug.LogWarning($"[LocalMP] Couldn't clear the room state [{e.Code}] {e.Message}");
                return false;
            }
        }

        // ---------------------------------------------------------------- requests and messages

        public Task<JObject> RequestAsync(string type, JObject body, CancellationToken cancellationToken = default)
        {
            if (!TryStartRequest(type, body, cancellationToken, false, out var pending, out var error))
            {
                return error is OperationCanceledException ? Task.FromCanceled<JObject>(cancellationToken) : Task.FromException<JObject>(error);
            }
            return pending.Tcs.Task;
        }

        /// <summary>
        /// A request that also reports when it left and when its reply arrived, for the room clock
        /// (PacketPartySignaling.RequestTimedAsync). With <paramref name="allowWhileJoining"/> it may go out as
        /// soon as the welcome arrived, as PacketPartyClient's clock burst does inside the welcome.
        /// </summary>
        internal async Task<TimedReply> RequestTimedAsync(string type, JObject body, CancellationToken cancellationToken = default,
            bool allowWhileJoining = false)
        {
            if (!TryStartRequest(type, body, cancellationToken, allowWhileJoining, out var pending, out var error))
            {
                throw error;
            }
            var reply = await pending.Tcs.Task;
            return new TimedReply(reply, Volatile.Read(ref pending.SentAtMs), pending.ReceivedAtMs);
        }

        public bool Send(string type, JObject body)
        {
            if (string.IsNullOrEmpty(type) || State != SessionState.Joined || _connection == null) return false;
            _connection.EnqueueReliable(Envelope(type, body).ToString(Formatting.None));
            return true;
        }

        public void On(string type, Action<JObject> handler)
        {
            if (string.IsNullOrEmpty(type) || handler == null) return;
            _handlers.TryGetValue(type, out var current);
            var length = current != null ? current.Length : 0;
            // Copy on write: a handler may subscribe or unsubscribe while its message is being dispatched.
            var next = new Action<JObject>[length + 1];
            if (current != null) Array.Copy(current, next, length);
            next[length] = handler;
            _handlers[type] = next;
        }

        public void Off(string type, Action<JObject> handler)
        {
            if (string.IsNullOrEmpty(type) || handler == null || !_handlers.TryGetValue(type, out var current)) return;
            var index = Array.LastIndexOf(current, handler);
            if (index < 0) return;
            if (current.Length == 1)
            {
                _handlers.Remove(type);
                return;
            }
            var next = new Action<JObject>[current.Length - 1];
            Array.Copy(current, 0, next, 0, index);
            Array.Copy(current, index + 1, next, index, current.Length - index - 1);
            _handlers[type] = next;
        }

        public void SendParticipantFrame(in ParticipantFrame frame)
        {
            if (State != SessionState.Joined) return;
            _participant = frame;
            _hasParticipant = true;
        }

        public void QueueObjectFrame(in ObjectFrame frame)
        {
            if (State != SessionState.Joined || string.IsNullOrEmpty(frame.ObjectId)) return;
            _pendingObjects[frame.ObjectId] = frame;
        }

        public void SetEngineUserState(string key, string value)
        {
            _engineState.Set(key, value);
        }

        public void UpdateManifest(ObjectManifest manifest)
        {
            var copy = new ObjectManifest();
            if (manifest != null)
            {
                if (manifest.Ids != null) copy.Ids.AddRange(manifest.Ids);
                if (manifest.RuntimeIds != null) copy.RuntimeIds.AddRange(manifest.RuntimeIds);
            }
            _manifest = copy;
            _manifestJson = RelayProtocol.ManifestJson(copy).ToString(Formatting.None);
            FlushManifest();
        }

        internal EngineUserStateChannel EngineState => _engineState;

        // ---------------------------------------------------------------- frame hooks (the host calls these)

        /// <summary>Early in every frame: apply what arrived, run the timers.</summary>
        internal void Tick()
        {
            if (_shutdown) return;
            Drain();
            var now = Time.realtimeSinceStartup;
            if (_joinDeadline > 0f && now >= _joinDeadline && State != SessionState.Joined)
            {
                FailAttempt("the relay didn't finish the join within 10 s");
            }
            if (now >= _nextTimeoutSweep)
            {
                _nextTimeoutSweep = now + 0.25f;
                SweepTimeouts(now);
            }
            if (_wantJoined && _connection == null && _retryAt >= 0f && now >= _retryAt)
            {
                StartAttempt();
            }
            _engineState.Tick(State == SessionState.Joined, Time.unscaledTime, _lifetime.Token);
            FlushManifest();
        }

        /// <summary>At the end of every frame: hand this frame's pose and object frames to the socket.</summary>
        internal void FlushOutgoing()
        {
            if (_shutdown) return;
            var connection = _connection;
            var live = State == SessionState.Joined && connection != null;
            if (_hasParticipant)
            {
                _hasParticipant = false;
                if (live)
                {
                    _frameText.Clear();
                    RelayFrameCodec.WriteParticipant(_frameText, _participant);
                    connection.SetParticipant(_frameText.ToString());
                }
            }
            if (_pendingObjects.Count == 0) return;
            if (!live)
            {
                _pendingObjects.Clear();
                return;
            }
            // The previous batch is still going out: keep collecting the latest frame per object.
            if (!connection.ObjectsSlotFree) return;
            _frameText.Clear();
            _sentObjects.Clear();
            RelayFrameCodec.BeginObjects(_frameText);
            foreach (var pair in _pendingObjects)
            {
                if (_sentObjects.Count > 0 && _frameText.Length > RelayFrameCodec.MaxObjectsMessageChars) break;
                RelayFrameCodec.AppendObjectFrame(_frameText, pair.Value, _sentObjects.Count == 0);
                _sentObjects.Add(pair.Key);
            }
            RelayFrameCodec.EndObjects(_frameText);
            if (connection.TrySetObjects(_frameText.ToString()))
            {
                foreach (var id in _sentObjects) _pendingObjects.Remove(id);
            }
        }

        // ---------------------------------------------------------------- connection attempts

        void StartAttempt()
        {
            _retryAt = -1f;
            _joinDeadline = -1f;
            _lastRelayError = null;
            // Only a live session resumes; anything else joins fresh.
            var resume = _hasSession && !string.IsNullOrEmpty(_resumeSessionId) && !string.IsNullOrEmpty(_resumeToken);
            var hello = RelayProtocol.BuildHello(_identity, RoomId, _sdkVersion, _manifest,
                resume ? _resumeSessionId : null, resume ? _resumeToken : null);
            // The relay keeps the hello's manifest; only later changes need a manifest message.
            _manifestSentJson = _manifestJson;
            var port = SafePort();
            RelayUri = RelayConnection.AddressFor("127.0.0.1", port);
            _connection = new RelayConnection(++_connectionIds, port, _inbound);
            SetState(_hasSession ? SessionState.Reconnecting : SessionState.Connecting);
            _connection.Start(hello.ToString(Formatting.None));
        }

        int SafePort()
        {
            try
            {
                var port = _port();
                return port > 0 ? port : RelayProtocol.DefaultPort;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return RelayProtocol.DefaultPort;
            }
        }

        // The join failed on our side (a timeout, a broken welcome): drop the socket and retry.
        void FailAttempt(string reason)
        {
            var connection = _connection;
            _connection = null;
            connection?.Close(immediate: true);
            FailAllPending(RelayProtocol.ErrorClosed, reason);
            AttemptEnded(RelayCloseKind.Failed, 0, reason, 0);
        }

        void OnClosed(RelayInbound item)
        {
            var connection = _connection;
            _connection = null;
            connection?.Close(immediate: true);
            FailAllPending(RelayProtocol.ErrorClosed, $"the relay socket closed ({item.CloseCode})");
            AttemptEnded(item.CloseKind, item.CloseCode, item.CloseReason, item.HttpStatus);
        }

        void AttemptEnded(RelayCloseKind kind, int code, string reason, int httpStatus)
        {
            _joinDeadline = -1f;
            if (!_wantJoined)
            {
                SetState(SessionState.Idle);
                return;
            }
            var rejected = kind == RelayCloseKind.Rejected || IsRejection(code);
            var message = DescribeFailure(kind, code, reason, httpStatus);
            LastError = message;
            if (_hasSession && (State == SessionState.Joined || State == SessionState.Reconnecting))
            {
                // A live session dropped: keep the room and its peers and resume (ReconnectLoopAsync).
                if (State == SessionState.Joined)
                {
                    Debug.LogWarning($"[LocalMP] Lost the relay, reconnecting. {message}");
                    _lastLoggedFailure = message;
                }
                SetState(SessionState.Reconnecting);
            }
            else
            {
                // The join itself failed (ConnectAsync → Failed). A session that got its welcome but never its
                // room.ready is over: the next attempt joins fresh.
                EndSession(raiseEvents: true);
                _resumeSessionId = null;
                _resumeToken = null;
                SetState(SessionState.Failed);
                CompleteConnectWaiter(false);
            }
            if (!string.Equals(_lastLoggedFailure, message, StringComparison.Ordinal))
            {
                _lastLoggedFailure = message;
                Debug.LogWarning($"[LocalMP] {message} Retrying{(rejected ? " every 5 s" : "")}.");
            }
            _diagnostics?.Set("relay.connection", rejected ? DiagnosticLevel.Error : DiagnosticLevel.Warning, message);
            _retryAt = Time.realtimeSinceStartup + (rejected ? RejectedRetrySeconds : NextBackoff());
        }

        static bool IsRejection(int code)
        {
            return code == RelayProtocol.CloseProtocolMismatch || code == RelayProtocol.CloseProjectMismatch ||
                   code == RelayProtocol.CloseInvalidHello || code == RelayProtocol.CloseRoomFull;
        }

        float NextBackoff()
        {
            var index = Math.Min(_backoffAttempt, BackoffSeconds.Length - 1);
            _backoffAttempt++;
            return BackoffSeconds[index] + (float)_random.NextDouble() * BackoffJitterSeconds;
        }

        string DescribeFailure(RelayCloseKind kind, int code, string reason, int httpStatus)
        {
            var port = SafePort();
            var error = _lastRelayError;
            var errorCode = error != null ? RelayProtocol.Str(error, "code") : "";
            switch (errorCode)
            {
                case "project_mismatch":
                    return $"localhost:{port} is the hub of another project, {RelayProtocol.Str(error, "hubProjectRoot")} (pid {RelayProtocol.Long(error, "hubPid")}): stop Play there.";
                case "protocol_mismatch":
                    return $"The hub at localhost:{port} speaks another relay protocol version ({error["supported"]?.ToString(Formatting.None) ?? "?"}); update the SDK and Ora together.";
                case "invalid_hello":
                    return $"The relay refused this player's hello (field '{RelayProtocol.Str(error, "field")}').";
                case "room_full":
                    return "The room is full.";
            }
            switch (kind)
            {
                case RelayCloseKind.Refused:
                    return $"Nothing answered at localhost:{port}: is the main editor in Play?";
                case RelayCloseKind.Rejected:
                    return $"localhost:{port} refused the relay connection (HTTP {httpStatus}): Ora is older than 0.1.0, or local multiplayer is off in the main editor.";
            }
            switch (code)
            {
                case RelayProtocol.CloseHubShuttingDown:
                    return "The hub is shutting down (the main editor left Play).";
                case RelayProtocol.CloseBackpressure:
                    return "The relay dropped this player for falling behind (backpressure).";
                case RelayProtocol.CloseHelloTimeout:
                    return "The relay timed out waiting for the hello.";
                case RelayProtocol.CloseProtocolMismatch:
                    return "The relay speaks another protocol version.";
                case RelayProtocol.CloseProjectMismatch:
                    return $"localhost:{port} is the hub of another project.";
                case RelayProtocol.CloseInvalidHello:
                    return "The relay refused this player's hello.";
                case RelayProtocol.CloseRoomFull:
                    return "The room is full.";
            }
            if (code == 0 && !string.IsNullOrEmpty(reason))
            {
                // Our own verdict (a join timeout, a broken welcome), already a sentence.
                return char.ToUpperInvariant(reason[0]) + reason.Substring(1) + ".";
            }
            return string.IsNullOrEmpty(reason) ? $"The relay connection closed ({code})." : $"The relay connection closed ({code}: {reason}).";
        }

        void CompleteConnectWaiter(bool joined)
        {
            var waiter = _connectWaiter;
            _connectWaiter = null;
            waiter?.TrySetResult(joined);
        }

        // The session is over: its peers and its id go, and listeners hear RoomLeft once.
        void EndSession(bool raiseEvents)
        {
            _roster.Clear();
            _pendingObjects.Clear();
            _hasParticipant = false;
            if (!_hasSession) return;
            _hasSession = false;
            OwnRoomSessionId = null;
            OwnPeerId = null;
            _peers.Clear();
            Epoch++;
            if (!raiseEvents) return;
            try
            {
                RoomLeft?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        void SetState(SessionState state)
        {
            if (State == state) return;
            State = state;
            try
            {
                StateChanged?.Invoke(state);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        // ---------------------------------------------------------------- inbound

        void Drain()
        {
            for (var budget = MaxInboundPerFrame; budget > 0; budget--)
            {
                if (!_inbound.TryDequeue(out var item)) return;
                // Leftovers of a socket the session already let go of.
                var connection = _connection;
                if (connection == null || item.Connection != connection.Id) continue;
                try
                {
                    Handle(item);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        /// <summary>One thing the transport handed over, on the main thread: Drain's step, once the item is known
        /// to come from the current socket (tests feed it directly).</summary>
        internal void Handle(RelayInbound item)
        {
            switch (item.Kind)
            {
                case RelayInboundKind.Opened:
                    RelayUri = item.Uri ?? RelayUri;
                    _joinDeadline = Time.realtimeSinceStartup + JoinTimeoutSeconds;
                    if (State == SessionState.Connecting) SetState(SessionState.Joining);
                    break;
                case RelayInboundKind.Message:
                    OnMessage(item);
                    break;
                case RelayInboundKind.Participant:
                    // The relay never echoes a sender's lanes; drop our own anyway (PacketPartyClient.cs:4164-4170).
                    if (State != SessionState.Joined || IsOwn(item.From)) break;
                    try
                    {
                        ParticipantFrameReceived?.Invoke(item.From, item.Participant);
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                    }
                    break;
                case RelayInboundKind.Objects:
                    if (State != SessionState.Joined || IsOwn(item.From) || item.Objects == null) break;
                    foreach (var frame in item.Objects)
                    {
                        try
                        {
                            ObjectFrameReceived?.Invoke(item.From, frame);
                        }
                        catch (Exception e)
                        {
                            Debug.LogException(e);
                        }
                    }
                    break;
                case RelayInboundKind.Closed:
                    OnClosed(item);
                    break;
            }
        }

        // Bookkeeping first, then the On(type) handlers, then what the message means for the session.
        void OnMessage(RelayInbound item)
        {
            var type = item.Type;
            var body = item.Body;
            _currentReceivedAtMs = item.ReceivedAtMs;
            try
            {
                switch (type)
                {
                    case RelayProtocol.Welcome:
                        if (!ApplyWelcome(body)) return;
                        Dispatch(type, body);
                        return;
                    case RelayProtocol.RoomReady:
                        ApplyRoomReady(body);
                        return;
                    case RelayProtocol.PeerJoined:
                        Dispatch(type, body);
                        ApplyPeerJoined(body);
                        return;
                    case RelayProtocol.PeerLeft:
                        Dispatch(type, body);
                        ApplyPeerLeft(body);
                        return;
                    case RelayProtocol.Manifest:
                        if (IsOwn(RelayProtocol.Str(body, "from"))) return;
                        Dispatch(type, body);
                        ApplyManifest(body);
                        return;
                    case RelayProtocol.OneShot:
                        if (IsOwn(RelayProtocol.Str(body, "fromRoomSessionId"))) return;
                        break;
                    case RelayProtocol.ObjectPayload:
                        if (IsOwn(RelayProtocol.Str(body, "from"))) return;
                        break;
                    case RelayProtocol.UserStateSnapshot:
                        Dispatch(type, body);
                        ApplyUserStateSnapshot(body);
                        return;
                    case RelayProtocol.UserStateChanged:
                        Dispatch(type, body);
                        ApplyUserStateChanged(body);
                        return;
                    case RelayProtocol.UserStateRemoved:
                        Dispatch(type, body);
                        ApplyUserStateRemoved(body);
                        return;
                    case RelayProtocol.RelayError:
                        Dispatch(type, body);
                        ApplyRelayError(body);
                        return;
                    case RelayProtocol.SignalError:
                        Dispatch(type, body);
                        ApplySignalError(body);
                        return;
                }
                Dispatch(type, body);
                // Anything carrying a requestId answers a request (PacketPartySignaling.DispatchIncoming).
                ResolveRequest(body, item.ReceivedAtMs);
            }
            finally
            {
                _currentReceivedAtMs = double.NaN;
            }
        }

        void Dispatch(string type, JObject body)
        {
            if (!_handlers.TryGetValue(type, out var handlers)) return;
            foreach (var handler in handlers)
            {
                try
                {
                    handler(body);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        bool IsOwn(string roomSessionId)
        {
            return !string.IsNullOrEmpty(roomSessionId) && string.Equals(roomSessionId, OwnRoomSessionId, StringComparison.Ordinal);
        }

        bool ApplyWelcome(JObject body)
        {
            var sessionId = RelayProtocol.Str(body, "sessionId");
            if (string.IsNullOrEmpty(sessionId))
            {
                FailAttempt("the relay's welcome had no sessionId");
                return false;
            }
            var resumed = RelayProtocol.Bool(body, "resumed");
            if (_hasSession && (!resumed || !string.Equals(sessionId, OwnRoomSessionId, StringComparison.Ordinal)))
            {
                // The relay no longer had our session (its grace ran out, or it is a new hub): that session is
                // over, and this is a fresh one.
                Debug.Log("[LocalMP] The relay no longer had this player's session; joined as a new one.");
                EndSession(raiseEvents: true);
            }
            var fresh = !_hasSession;
            _hasSession = true;
            OwnRoomSessionId = sessionId;
            OwnPeerId = RelayProtocol.Str(body, "peerId");
            _resumeSessionId = sessionId;
            var token = RelayProtocol.Str(body, "resumeToken");
            _resumeToken = string.IsNullOrEmpty(token) ? null : token;
            if (fresh)
            {
                // A hard disconnect cleared these in production (TeardownAsync); the relay has none of our state.
                Epoch++;
                _engineState.ResetForNewSession();
                _userStateRevision = 0;
            }
            else
            {
                _engineState.ResetForResume();
            }

            _roster.Clear();
            if (body["existingPeers"] is JArray members)
            {
                foreach (var member in members)
                {
                    var peer = ParsePeer(member as JObject);
                    if (peer == null || IsOwn(peer.RoomSessionId)) continue;
                    _roster[peer.RoomSessionId] = peer;
                }
            }
            ApplyHubInfo(body["hub"] as JObject);
            return true;
        }

        /// <summary>
        /// PacketPartyClient's room.ready check (PacketPartyClient.cs:3433-3450): the join completes only when the
        /// room state, the user state, the objects and the transforms are all hydrated. The relay has no
        /// transform graph, but sends transformsReady:true with graph revision 0, as production does for a room
        /// without one (RELAY.md 2).
        /// </summary>
        internal static bool IsRoomHydrated(JObject roomReady)
        {
            return RelayProtocol.Bool(roomReady, "userStateReady") && RelayProtocol.Bool(roomReady, "roomStateReady") &&
                   RelayProtocol.Bool(roomReady, "objectsReady") && RelayProtocol.Bool(roomReady, "transformsReady");
        }

        void ApplyRoomReady(JObject body)
        {
            if (!_hasSession)
            {
                Dispatch(RelayProtocol.RoomReady, body);
                return;
            }
            // PacketPartyClient fails a join whose hydration failed.
            if (!IsRoomHydrated(body))
            {
                FailAttempt("the relay couldn't hydrate the room");
                return;
            }
            _joinDeadline = -1f;
            _backoffAttempt = 0;
            _retryAt = -1f;
            LastError = null;
            _lastLoggedFailure = null;
            _diagnostics?.Clear("relay.connection");
            SetState(SessionState.Joined);
            Dispatch(RelayProtocol.RoomReady, body);
            ReconcileRoster();
            FlushManifest();
            CompleteConnectWaiter(true);
            try
            {
                RoomJoined?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        // In PacketPartyClient's order: ReconcileWelcomePeers first (PacketPartyClient.cs:3806, 4764-4791), so
        // the peers that went away while we were reconnecting leave before anyone joins, each removed and then
        // announced (Room.RemovePeer, RaisePeerLeft). Then the welcome registers everyone present
        // (RegisterPeerPresence, :3870-3877): a new peer joins, a known one is updated (UpsertPeer). All of
        // it at room.ready, as the scene binder builds every present peer's view in OnRoomJoined.
        void ReconcileRoster()
        {
            _leftScratch.Clear();
            foreach (var pair in _peers)
            {
                if (!_roster.ContainsKey(pair.Key)) _leftScratch.Add(pair.Value);
            }
            foreach (var stale in _leftScratch)
            {
                if (!_peers.Remove(stale.RoomSessionId)) continue;
                RaisePeerLeft(stale);
            }
            _leftScratch.Clear();

            _rosterScratch.Clear();
            _rosterScratch.AddRange(_roster.Values);
            _roster.Clear();
            foreach (var member in _rosterScratch)
            {
                if (_peers.TryGetValue(member.RoomSessionId, out var existing))
                {
                    UpdatePeer(existing, member);
                    continue;
                }
                _peers[member.RoomSessionId] = member;
                RaisePeerJoined(member);
            }
            _rosterScratch.Clear();
        }

        void ApplyPeerJoined(JObject body)
        {
            var peer = ParsePeer(body);
            if (peer == null || IsOwn(peer.RoomSessionId)) return;
            if (State != SessionState.Joined)
            {
                // Before room.ready: it is announced with the rest of the roster.
                _roster[peer.RoomSessionId] = peer;
                return;
            }
            if (_peers.TryGetValue(peer.RoomSessionId, out var existing))
            {
                UpdatePeer(existing, peer);
                return;
            }
            _peers[peer.RoomSessionId] = peer;
            RaisePeerJoined(peer);
        }

        void ApplyPeerLeft(JObject body)
        {
            var rsid = RelayProtocol.Str(body, "roomSessionId");
            var peerId = RelayProtocol.Str(body, "peerId");
            var peers = State == SessionState.Joined ? _peers : _roster;
            var peer = Find(peers, rsid, peerId);
            if (peer == null) return;
            peers.Remove(peer.RoomSessionId);
            if (peers == _peers) RaisePeerLeft(peer);
        }

        static LocalPeer Find(Dictionary<string, LocalPeer> peers, string rsid, string peerId)
        {
            if (!string.IsNullOrEmpty(rsid) && peers.TryGetValue(rsid, out var peer)) return peer;
            if (string.IsNullOrEmpty(peerId)) return null;
            foreach (var candidate in peers.Values)
            {
                if (string.Equals(candidate.PeerId, peerId, StringComparison.Ordinal)) return candidate;
            }
            return null;
        }

        void ApplyManifest(JObject body)
        {
            var from = RelayProtocol.Str(body, "from");
            var manifest = RelayProtocol.ParseManifest(body);
            if (_peers.TryGetValue(from, out var peer))
            {
                if (RelayProtocol.SameManifest(peer.Manifest, manifest)) return;
                peer.Manifest = manifest;
                RaisePeerManifestChanged(peer);
            }
            else if (_roster.TryGetValue(from, out var pending))
            {
                pending.Manifest = manifest;
            }
        }

        void UpdatePeer(LocalPeer existing, LocalPeer fresh)
        {
            if (!string.IsNullOrEmpty(fresh.DisplayName)) existing.DisplayName = fresh.DisplayName;
            if (!string.IsNullOrEmpty(fresh.PeerId)) existing.PeerId = fresh.PeerId;
            existing.EditorPid = fresh.EditorPid;
            existing.Role = fresh.Role;
            if (RelayProtocol.SameManifest(existing.Manifest, fresh.Manifest)) return;
            existing.Manifest = fresh.Manifest;
            RaisePeerManifestChanged(existing);
        }

        LocalPeer ParsePeer(JObject json)
        {
            if (json == null) return null;
            var peerId = RelayProtocol.Str(json, "peerId");
            var rsid = RelayProtocol.Str(json, "roomSessionId");
            if (string.IsNullOrEmpty(rsid)) rsid = peerId;
            if (string.IsNullOrEmpty(rsid)) return null;
            var meta = json["presenceMetadata"] as JObject;
            var peer = new LocalPeer
            {
                RoomSessionId = rsid,
                PeerId = peerId,
                ClientId = RelayProtocol.Str(json, "userId"),
                DisplayName = RelayProtocol.Str(json, "displayName"),
                Slot = RelayProtocol.Str(meta, "slot"),
                Role = RelayProtocol.ParseRole(RelayProtocol.Str(meta, "role")),
                EditorPid = (int)RelayProtocol.Long(meta, "editorPid"),
                Manifest = RelayProtocol.ParseManifest(meta?["manifest"] as JObject),
            };
            // NetworkService.RegisterSdkUser: the uid hashes the stable user id, falling back to the session ids.
            var rawUid = !string.IsNullOrEmpty(peer.ClientId) ? peer.ClientId : !string.IsNullOrEmpty(rsid) ? rsid : peerId;
            peer.Uid = LocalIdentityFactory.HashUid(rawUid);
            peer.Tint = LocalIdentityFactory.TintFor(LocalIdentityFactory.SlotIndexOf(peer.Slot), rawUid);
            return peer;
        }

        void RaisePeerJoined(LocalPeer peer)
        {
            Debug.Log($"[LocalMP] Peer joined: {peer.DisplayName} peerId={peer.PeerId} userId='{peer.ClientId}' roomSessionId={peer.RoomSessionId}");
            try
            {
                PeerJoined?.Invoke(peer);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        void RaisePeerLeft(LocalPeer peer)
        {
            try
            {
                PeerLeft?.Invoke(peer);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        void RaisePeerManifestChanged(LocalPeer peer)
        {
            try
            {
                PeerManifestChanged?.Invoke(peer);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        // The engine's user state (non-props paths), with PacketPartyClient's revision gate
        // (OnUserStateSnapshotSignal / Changed / Removed, PacketPartyClient.cs:3032-3086).
        void ApplyUserStateSnapshot(JObject body)
        {
            var revision = RelayProtocol.Long(body, "revision");
            if (revision < _userStateRevision) return;
            _userStateRevision = revision;
            var snapshot = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
            if (body["users"] is JObject users)
            {
                foreach (var user in users.Properties())
                {
                    if (!(user.Value is JObject state)) continue;
                    Dictionary<string, string> engine = null;
                    foreach (var leaf in state.Properties())
                    {
                        if (!RelayProtocol.IsEnginePath(leaf.Name)) continue;
                        if (engine == null) engine = new Dictionary<string, string>(StringComparer.Ordinal);
                        // As the attachment bridge reads it: change.Value?.ToString().
                        engine[leaf.Name] = leaf.Value?.ToString();
                    }
                    if (engine != null) snapshot[user.Name] = engine;
                }
            }
            try
            {
                EngineUserStateSnapshot?.Invoke(snapshot);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        void ApplyUserStateChanged(JObject body)
        {
            var rsid = RelayProtocol.Str(body, "roomSessionId");
            var revision = RelayProtocol.Long(body, "revision");
            if (string.IsNullOrEmpty(rsid) || revision < _userStateRevision) return;
            _userStateRevision = revision;
            if (!(body["changes"] is JArray changes)) return;
            foreach (var token in changes)
            {
                if (!(token is JObject change)) continue;
                var path = RelayProtocol.Str(change, "path");
                if (!RelayProtocol.IsEnginePath(path)) continue;
                var deleted = RelayProtocol.Bool(change, "deleted");
                var value = deleted ? null : change["value"]?.ToString();
                try
                {
                    EngineUserStateChanged?.Invoke(rsid, path, value, deleted);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        void ApplyUserStateRemoved(JObject body)
        {
            var rsid = RelayProtocol.Str(body, "roomSessionId");
            var revision = RelayProtocol.Long(body, "revision");
            if (string.IsNullOrEmpty(rsid) || revision < _userStateRevision) return;
            _userStateRevision = revision;
            try
            {
                EngineUserStateRemoved?.Invoke(rsid);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        void ApplyRelayError(JObject body)
        {
            var code = RelayProtocol.Str(body, "code");
            if (string.IsNullOrEmpty(code)) code = "relay_error";
            var requestId = RelayProtocol.Str(body, "requestId");
            if (!string.IsNullOrEmpty(requestId) && _pending.TryGetValue(requestId, out var pending))
            {
                _pending.Remove(requestId);
                var text = RelayProtocol.Str(body, "message");
                pending.Fail(new LocalRelayException(code, string.IsNullOrEmpty(text) ? code : text));
                return;
            }
            _lastRelayError = body;
            // Before the join completes it is the reason for the close that follows; say it then.
            if (State != SessionState.Joined) return;
            var message = RelayProtocol.Str(body, "message");
            Debug.LogWarning($"[LocalMP] Relay: {code}{(string.IsNullOrEmpty(message) ? "" : " - " + message)}");
            _diagnostics?.Set("relay.error." + code, DiagnosticLevel.Warning, string.IsNullOrEmpty(message) ? code : message);
        }

        void ApplySignalError(JObject body)
        {
            var code = RelayProtocol.Str(body, "error");
            if (string.IsNullOrEmpty(code)) code = "signal_error";
            var requestId = RelayProtocol.Str(body, "requestId");
            if (!string.IsNullOrEmpty(requestId) && _pending.TryGetValue(requestId, out var pending))
            {
                _pending.Remove(requestId);
                pending.Fail(new LocalRelayException(code));
                return;
            }
            Debug.LogWarning($"[LocalMP] Relay signal error: {code}");
        }

        void ApplyHubInfo(JObject hub)
        {
            if (hub == null) return;
            _hub.State = HubState.Ready;
            _hub.Pid = (int)RelayProtocol.Long(hub, "pid");
            _hub.ParentPid = (int)RelayProtocol.Long(hub, "parentPid");
            _hub.ProjectRoot = RelayProtocol.Str(hub, "projectRoot");
            _hub.Message = $"joined through the hub (pid {_hub.Pid})";
            if (hub["persistence"] is JObject persistence)
            {
                _hub.PersistenceEnabled = RelayProtocol.Bool(persistence, "enabled");
                var file = RelayProtocol.Str(persistence, "file");
                if (!string.IsNullOrEmpty(file))
                {
                    try
                    {
                        _hub.PersistenceDir = Path.GetDirectoryName(file) ?? "";
                    }
                    catch (ArgumentException)
                    {
                    }
                }
            }
            if (!_warnedOrphanHub && hub["parentPidMatches"] is JValue matches && matches.Type == JTokenType.Boolean && !(bool)matches)
            {
                _warnedOrphanHub = true;
                var message = $"The hub (Electron pid {_hub.Pid}) belongs to process {_hub.ParentPid}, not to the main editor ({_identity.MainProcessId}): it is probably left over from an earlier session. If players can't see each other, stop Play everywhere and end stray ora-windows.exe processes.";
                Debug.LogWarning("[LocalMP] " + message);
                _diagnostics?.Set("hub.orphan", DiagnosticLevel.Warning, message);
            }
        }

        // ---------------------------------------------------------------- requests (main thread)

        bool TryStartRequest(string type, JObject body, CancellationToken cancellationToken, bool allowWhileJoining,
            out PendingRequest pending, out Exception error)
        {
            pending = null;
            error = null;
            if (string.IsNullOrEmpty(type))
            {
                error = new ArgumentException("type required", nameof(type));
                return false;
            }
            if (cancellationToken.IsCancellationRequested)
            {
                error = new OperationCanceledException(cancellationToken);
                return false;
            }
            var connection = _connection;
            var open = State == SessionState.Joined || (allowWhileJoining && _hasSession);
            if (!open || connection == null)
            {
                error = new LocalRelayException(RelayProtocol.ErrorNotConnected, $"not in a room ({State})");
                return false;
            }
            var requestId = "c" + (++_requestCounter).ToString(CultureInfo.InvariantCulture);
            var envelope = Envelope(type, body);
            envelope["requestId"] = requestId;
            pending = new PendingRequest(type, Time.realtimeSinceStartup + RequestTimeoutSeconds);
            _pending[requestId] = pending;
            if (cancellationToken.CanBeCanceled)
            {
                var tcs = pending.Tcs;
                pending.Registration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
            }
            connection.EnqueueReliable(envelope.ToString(Formatting.None), pending.MarkSending);
            return true;
        }

        static JObject Envelope(string type, JObject body)
        {
            var envelope = new JObject { ["type"] = type };
            if (body == null) return envelope;
            foreach (var property in body.Properties())
            {
                if (property.Name == "type" || property.Name == "requestId") continue;
                envelope[property.Name] = property.Value?.DeepClone();
            }
            return envelope;
        }

        void ResolveRequest(JObject body, double receivedAtMs)
        {
            var requestId = RelayProtocol.Str(body, "requestId");
            if (string.IsNullOrEmpty(requestId) || !_pending.TryGetValue(requestId, out var pending)) return;
            _pending.Remove(requestId);
            var sent = Volatile.Read(ref pending.SentAtMs);
            if (!double.IsNaN(sent) && receivedAtMs >= sent)
            {
                RttMs = receivedAtMs - sent;
            }
            pending.Resolve(body, receivedAtMs);
        }

        void SweepTimeouts(float now)
        {
            if (_pending.Count == 0) return;
            _expired.Clear();
            foreach (var pair in _pending)
            {
                if (pair.Value.Tcs.Task.IsCompleted || now >= pair.Value.Deadline) _expired.Add(pair.Key);
            }
            foreach (var id in _expired)
            {
                var pending = _pending[id];
                _pending.Remove(id);
                pending.Fail(new LocalRelayException(RelayProtocol.ErrorTimeout, $"{pending.Type} got no reply within {RequestTimeoutSeconds:0} s"));
            }
            _expired.Clear();
        }

        void FailAllPending(string code, string message)
        {
            if (_pending.Count == 0) return;
            var pending = new List<PendingRequest>(_pending.Values);
            _pending.Clear();
            foreach (var request in pending)
            {
                request.Fail(new LocalRelayException(code, $"{request.Type}: {message}"));
            }
        }

        void FlushManifest()
        {
            if (State != SessionState.Joined || _connection == null) return;
            if (string.Equals(_manifestJson, _manifestSentJson, StringComparison.Ordinal)) return;
            if (Send(RelayProtocol.Manifest, RelayProtocol.ManifestJson(_manifest)))
            {
                _manifestSentJson = _manifestJson;
            }
        }
    }
}
