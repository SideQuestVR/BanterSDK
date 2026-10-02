// <mirror source="Assets/Systems/Networking/NetworkService.cs" sha256="cab93afa037c3e7a7ef1181ed552a18541ae46c49b93872cbae9b18a5f804c80" mode="port" />
// <mirror source="Assets/Systems/SceneEvents/BanterSceneEventHandler.cs" sha256="ee046e8061e439c34909a9015a187b387824455d5cdd6a88e9e1f07941660813" mode="port" />
using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;

namespace BS.LocalMultiplayer
{
    /// <summary>
    /// When this player is in the room, ported from NetworkService's space lifecycle
    /// (NetworkService.cs:592-674, 707-895, 1090-1180):
    /// <list type="bullet">
    /// <item>OnSceneReady (the space finished loading): replay every user into the fresh page
    /// (BanterSceneEventHandler.ResyncUsersToScene), then join the room, holding the loading cage while the
    /// join resolves.</item>
    /// <item>OnLoad (the space is unloading, a page reload included): pause publishing and leave by closing
    /// the socket.</item>
    /// <item>Joined (room.ready): the local user takes the room session id in place; publishing goes live one
    /// frame after the cage opens, so load-time teleports are never broadcast. The SDK has no loading cage,
    /// so without one it goes live one frame after the join.</item>
    /// </list>
    /// It also serves the session's Leave, Join and Rejoin; Rejoin reloads the page, so this player goes
    /// through OnLoad and OnSceneReady exactly like a late joiner.
    /// </summary>
    [LocalModule(ModuleOrder.Lifecycle)]
    [AddComponentMenu("")]
    public sealed class LocalNetworkService : MonoBehaviour, ILocalModule, ILocalSessionController
    {
        ILocalHost _host;
        LocalSession _session;
        LocalUserIdentity _localUser;
        BSScene _scene;
        Func<bool> _previousCageGate;
        Func<bool> _cageGate;
        LoadingBarManager _cageOpenHooked;
        string _roomId;               // the room the last scene-ready asked for
        string _requestedRoomId;      // latest room a scene-ready asked for
        bool _joinWorkerRunning;      // serializes the join reconcile loop
        bool _joinInFlight;           // a join is running; the loading cage waits on this
        int _publishGeneration;       // a pause cancels a go-live that is still a frame away
        bool _awaitingCage;           // a load started and its cage hasn't opened yet
        SessionState _lastLoggedState = SessionState.Idle;
        [NonSerialized] bool _live;

        public void Install(ILocalHost host)
        {
            _host = host;
            _session = host.Session as LocalSession;
            if (_session == null)
            {
                throw new InvalidOperationException("LocalNetworkService needs the host's LocalSession");
            }
            _localUser = host.Get<LocalUserIdentity>();
            _scene = host.Scene;
            _live = true;

            // SubscribeSpaceLifecycle (NetworkService.cs:592-606).
            _scene.events.OnLoad.AddListener(HandleSpaceLoad);
            _scene.events.OnSceneReady.AddListener(HandleSceneReady);
            _previousCageGate = _scene.CanOpenLoadingCage;
            _cageGate = CanOpenLoadingCage;
            _scene.CanOpenLoadingCage = _cageGate;
            EnsureCageOpenHooked(); // loadingManager resolves lazily; retried at every OnLoad

            _session.StateChanged += OnConnectionStateChanged;
            _session.RoomJoined += HandleRoomJoined;
            _session.RoomLeft += HandleRoomLeft;
            _session.Controller = this;
            Debug.Log("[LocalMP] Subscribed to the space lifecycle: join on scene-ready, hold the cage until joined, publish once joined.");
        }

        public void Uninstall()
        {
            _live = false;
            _publishGeneration++;
            if (_scene != null)
            {
                _scene.events.OnLoad.RemoveListener(HandleSpaceLoad);
                _scene.events.OnSceneReady.RemoveListener(HandleSceneReady);
                if (_scene.CanOpenLoadingCage == _cageGate)
                {
                    _scene.CanOpenLoadingCage = _previousCageGate;
                }
            }
            if (_cageOpenHooked != null)
            {
                _cageOpenHooked.onLoadOutStarted.RemoveListener(HandleCageOpen);
                _cageOpenHooked = null;
            }
            if (_session != null)
            {
                _session.StateChanged -= OnConnectionStateChanged;
                _session.RoomJoined -= HandleRoomJoined;
                _session.RoomLeft -= HandleRoomLeft;
                if (ReferenceEquals(_session.Controller, this)) _session.Controller = null;
            }
            PausePublishing();
            // The page and the local user outlive Play's last frame: leave the user as it was offline.
            _localUser?.ClearNetworkSession();
        }

        // ---------------------------------------------------------------- the space lifecycle

        // Gate for the loading cage: hold it open-pending while a join we started is still resolving.
        bool CanOpenLoadingCage() => !_joinInFlight;

        // loadingManager is a lazy BSScene property (resolved from the link), so it may be null at install;
        // the first OnLoad comes after the link exists, so hook it there too. The SDK itself has no cage.
        void EnsureCageOpenHooked()
        {
            if (_cageOpenHooked != null || _scene == null) return;
            var loadingManager = _scene.loadingManager;
            if (loadingManager == null) return;
            loadingManager.onLoadOutStarted.AddListener(HandleCageOpen);
            _cageOpenHooked = loadingManager;
        }

        // Space is unloading — leave the room and stop publishing pose during the load.
        void HandleSpaceLoad()
        {
            if (!_live) return;
            EnsureCageOpenHooked();
            PausePublishing();
            _awaitingCage = _cageOpenHooked != null;
            _joinInFlight = false; // not connecting during a load; the next scene-ready re-arms it
            if (_session.State == SessionState.Idle && !_session.WantsRoom) return;
            Debug.Log("[LocalMP] Space unloading — leaving room.");
            DisconnectAsync();
        }

        // Loading cage is opening. Start publishing local pose — but defer one frame so it happens AFTER the
        // other onLoadOutStarted listeners (the teleport to spawn) have run this frame.
        void HandleCageOpen()
        {
            if (!_live) return;
            _awaitingCage = false;
            StartCoroutine(StartPublishingAfterSpawn(_publishGeneration));
        }

        IEnumerator StartPublishingAfterSpawn(int generation)
        {
            yield return null; // let this frame's teleport-to-spawn settle the player first
            if (!_live || generation != _publishGeneration) yield break;
            StartPublishing();
        }

        internal const string UntitledCloneKey = "lifecycle.untitledClone";

        internal const string UntitledCloneMessage =
            "This player is playing an empty, untitled scene, so it is in a room of its own: Play started before " +
            "Multiplayer Play Mode had loaded the Main Editor's scene here. Stop, wait until this player shows the " +
            "Main Editor's scene, then press Play again.";

        // An MPPM player loads the Main Editor's scene only once it has finished launching; Play pressed before
        // that runs it in the empty scene it started with, which is a room of its own. Say so instead of leaving
        // the players silently apart.
        void ReportUntitledClone(string roomId)
        {
            var diagnostics = _host.Diagnostics;
            if (diagnostics == null) return;
            var identity = _host.Identity;
            bool isClone = identity != null ? identity.IsClone : MppmEnvironment.Current.IsClone;
            if (isClone && roomId == LocalRoomId.Untitled)
            {
                diagnostics.Set(UntitledCloneKey, DiagnosticLevel.Warning, UntitledCloneMessage);
                Debug.LogWarning("[LocalMP] " + UntitledCloneMessage);
            }
            else
            {
                diagnostics.Clear(UntitledCloneKey);
            }
        }

        // Space finished loading — replay the users into the new page, then connect and join its room (the
        // cage waits on this via CanOpenLoadingCage). A serialized worker honours the latest requested room
        // so back-to-back space events can't race into overlapping connects.
        async void HandleSceneReady()
        {
            if (!_live) return;
            try
            {
                ResyncUsersToScene();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
            string roomId = LocalRoomId.ForActiveScene();
            ReportUntitledClone(roomId);
            _joinInFlight = true; // hold the cage until this join reaches Joined (or Failed)
            _requestedRoomId = roomId;
            if (_joinWorkerRunning) return;

            _joinWorkerRunning = true;
            try
            {
                while (_requestedRoomId != null && _live)
                {
                    string target = _requestedRoomId;
                    _requestedRoomId = null;
                    Debug.Log($"[LocalMP] Space ready — joining room '{target}'.");
                    await JoinRoomAsync(target);
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
            finally
            {
                _joinWorkerRunning = false;
            }
        }

        // BanterSceneEventHandler.ResyncUsersToScene: each space loads a fresh JS scene that starts with no
        // users. Users added earlier were announced to a page that no longer exists, so replay them into the
        // new scene now that its JS is listening. (The SDK's own once-per-page replay steps aside while a
        // network host is active.)
        void ResyncUsersToScene()
        {
            if (_scene?.users == null || _scene.link == null) return;
            foreach (var user in _scene.users)
                if (user != null) _scene.link.OnUserJoined(user);
            Debug.Log($"[LocalMP] Re-injected {_scene.users.Count} user(s) into the loaded scene.");
        }

        /// <summary>Switch rooms: disconnect, then connect to <paramref name="roomId"/> (NetworkService.JoinRoomAsync).</summary>
        async Task JoinRoomAsync(string roomId)
        {
            if (_session.State != SessionState.Idle || _session.WantsRoom)
                DisconnectAsync();
            _roomId = roomId;
            await ConnectAsync();
        }

        async Task ConnectAsync()
        {
            Debug.Log($"[LocalMP] Connecting to room '{_roomId}' at localhost …");
            // Publishing is NOT started here: it begins once joined (or at cage-open), so the player's
            // load-time teleports are never broadcast. Connecting here only receives.
            var joined = await _session.ConnectAsync(_roomId);
            if (!joined && _live && _session.WantsRoom)
            {
                // RELAY.md 9: keep trying in the background; the cage is not held for it.
                Debug.Log($"[LocalMP] Room '{_roomId}' not joined yet; still trying ({_session.LastError}).");
            }
        }

        // NetworkService.DisconnectAsync: close the socket (the session raises RoomLeft, which clears the
        // remote players and reverts the local user's session id).
        void DisconnectAsync()
        {
            _session.Disconnect();
        }

        void OnConnectionStateChanged(SessionState state)
        {
            // Production logs every change. The local session also retries for ever while the hub is away
            // (RELAY.md 9), so the steps of that loop are logged once, not every few seconds.
            var retrying = _lastLoggedState == SessionState.Failed || _lastLoggedState == SessionState.Reconnecting;
            if (!retrying || state == SessionState.Joined || state == SessionState.Idle)
            {
                Debug.Log($"[LocalMP] State: {state}");
                _lastLoggedState = state;
            }
            // Release the loading-cage gate once the join resolves. Never on Idle — that is the transient
            // middle of a JoinRoomAsync (Disconnect → Connect).
            if (state == SessionState.Joined || state == SessionState.Failed)
                _joinInFlight = false;
        }

        void HandleRoomJoined()
        {
            if (!_live) return;
            Debug.Log($"[LocalMP] Room joined: room={_session.RoomId} localSession={_session.OwnRoomSessionId} peers={_session.Peers.Count}");
            // Enrich the always-present local user with this session's id. The uid is unchanged, so only id
            // flips from the offline placeholder to the real room session id.
            _localUser?.SetNetworkSession(_session.OwnRoomSessionId);
            // The SDK has no loading cage to wait for (nor does a Join from the overlay, which loads nothing):
            // go live one frame after the join instead.
            if (!_awaitingCage)
            {
                StartCoroutine(StartPublishingAfterSpawn(_publishGeneration));
            }
        }

        // DisposeSceneBinder's tail: leaving the room reverts the local user to its offline session id — it
        // is NOT removed, so solo content still sees a local user.
        void HandleRoomLeft()
        {
            _localUser?.ClearNetworkSession();
        }

        // Start (or resume) publishing local pose — called a frame after the cage opens (or the join).
        void StartPublishing()
        {
            if (_host is LocalMultiplayerHost host) host.SetPublishingLive(true);
        }

        // Pause publishing during a load so the player's spawn/scripted teleports aren't broadcast.
        void PausePublishing()
        {
            _publishGeneration++;
            if (_host is LocalMultiplayerHost host) host.SetPublishingLive(false);
        }

        // ---------------------------------------------------------------- the overlay's buttons

        /// <summary>Close the socket and stay out until Join (or the next scene-ready).</summary>
        public void Leave()
        {
            if (!_live) return;
            Debug.Log("[LocalMP] Leaving the room.");
            PausePublishing();
            _requestedRoomId = null;
            _joinInFlight = false;
            DisconnectAsync();
        }

        /// <summary>Join the current room again after <see cref="Leave"/>; the page keeps its users.</summary>
        public void Join()
        {
            if (!_live || _session.WantsRoom) return;
            var roomId = !string.IsNullOrEmpty(_roomId) ? _roomId : LocalRoomId.ForActiveScene();
            _joinInFlight = true;
            _requestedRoomId = roomId;
            if (_joinWorkerRunning) return;
            RunJoinWorker();
        }

        async void RunJoinWorker()
        {
            _joinWorkerRunning = true;
            try
            {
                while (_requestedRoomId != null && _live)
                {
                    string target = _requestedRoomId;
                    _requestedRoomId = null;
                    await JoinRoomAsync(target);
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
            finally
            {
                _joinWorkerRunning = false;
            }
        }

        /// <summary>
        /// Reload the page: OnLoad leaves the room and OnSceneReady replays the users and joins again, which
        /// is what a player arriving late goes through.
        /// </summary>
        public void Rejoin()
        {
            if (!_live) return;
            var view = _scene != null && _scene.link != null && _scene.link.pipe != null ? _scene.link.pipe.view : null;
            if (view == null)
            {
                Debug.LogWarning("[LocalMP] There is no space view to reload; joining again instead.");
                Leave();
                Join();
                return;
            }
            var port = _host is LocalMultiplayerHost host ? host.Port : RelayProtocol.DefaultPort;
            Debug.Log("[LocalMP] Rejoining: reloading the page.");
            // The same URL BSStarterUpper.OpenPageDev loads.
            view.LoadUrl("http://localhost:" + port);
        }
    }
}
