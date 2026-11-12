using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace BS.LocalMultiplayer.Overlay
{
    /// <summary>
    /// The in-game overlay every player shows, Multiplayer Play Mode clones included: who this player is in the
    /// room, the relay connection, the other players, and the diagnostics the other modules raise. Its buttons
    /// rejoin as a late joiner, leave and join again, and (for the world owner) clear the room's space state.
    /// The settings' overlay key (F8 by default) switches between the panel and a one-line pill. Clones start
    /// with the pill, because their Game views are small (<see cref="OverlayLayout.PanelVisibleOnStart"/>).
    /// </summary>
    /// <remarks>
    /// Clicks never reach the world: the canvas is screen-space UI, which the desktop controller already gives
    /// to the EventSystem (see <see cref="OverlayCanvas"/>), so no input hook is needed. The text is rebuilt at
    /// most four times a second and only when something it shows changed; between rebuilds a frame costs a key
    /// poll and a few comparisons.
    /// </remarks>
    [LocalModule(ModuleOrder.Overlay)]
    [DisallowMultipleComponent]
    public sealed class OverlayModule : MonoBehaviour, ILocalModule
    {
        const float RefreshSeconds = 0.25f;
        const int MaxDiagnostics = 8;
        const float ClearConfirmSeconds = 3f;
        const string Swatch = "\u25A0";
        const string Dim = "#9AA0A6";

        ILocalHost _host;
        ILocalSession _session;
        ILocalDiagnostics _diagnostics;
        OverlayCanvas _view;
        readonly PeerActivity _activity = new PeerActivity();

        // A script reload during Play restores serialized fields but never calls Install, so nothing may run
        // until one does.
        [NonSerialized] bool _live;
        [NonSerialized] int _installCount;

        bool _panelVisible;
        bool _dirty;
        float _nextRefresh;

        Key _toggleKey = Key.F8;
        string _toggleKeyName = OverlayKeys.DefaultName;
        Keyboard _keyboard;
        KeyControl _toggleControl;

        // Values that change without an event, compared on each refresh tick.
        SessionState _shownState;
        long _shownRtt = long.MinValue;
        HubState _shownHubState;
        string _shownHubMessage;
        string _shownError;

        bool _leftByUser;
        bool _clearArmed;
        float _clearArmedUntil;
        bool _clearing;
        string _status = string.Empty;

        readonly StringBuilder _text = new StringBuilder(1024);
        readonly List<LocalPeer> _peers = new List<LocalPeer>(8);
        readonly List<DiagnosticEntry> _entries = new List<DiagnosticEntry>(16);

        static readonly Comparison<LocalPeer> BySlot = (a, b) => string.CompareOrdinal(a.Slot, b.Slot);
        static readonly Comparison<DiagnosticEntry> NewestFirst = (a, b) =>
        {
            var byTime = b.Time.CompareTo(a.Time);
            return byTime != 0 ? byTime : string.CompareOrdinal(a.Key, b.Key);
        };

        // Cached so += and -= always see the same delegate instances.
        Action<SessionState> _onStateChanged;
        Action _onRoomJoined;
        Action _onRoomLeft;
        Action<LocalPeer> _onPeerChanged;
        Action<string, ParticipantFrame> _onParticipantFrame;
        Action<string, string, string, bool> _onEngineState;
        Action<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> _onEngineSnapshot;
        Action<string> _onEngineRemoved;
        Action _onDiagnosticsChanged;

        /// <summary>Pose rates, seats and attachments per player, also read by the Local Multiplayer window.</summary>
        public PeerActivity Activity => _activity;

        /// <summary>What the last button did, or is doing; the window shows it too.</summary>
        public string Status => _status;

        /// <summary>True from Leave until the player is back in the room: Join is offered instead of Leave.</summary>
        public bool OffersJoin => _live && _session != null && ShowsJoin(_session.State);

        /// <summary>A clear request is waiting for the relay's answer.</summary>
        public bool Clearing => _clearing;

        /// <summary>Reloads the page, so this player goes through OnLoad and OnSceneReady like a late joiner.</summary>
        public void Rejoin() => OnRejoinClicked();

        /// <summary>Leave, or Join after a Leave: the overlay's second button.</summary>
        public void LeaveOrJoin() => OnLeaveJoinClicked();

        /// <summary>
        /// Clears the room's space state (admin.clearRoomState, the world owner only). The caller has confirmed:
        /// the overlay's own button asks with a second click, the window with a dialog.
        /// </summary>
        public void ClearRoomState()
        {
            if (!_live || _session == null || _clearing)
            {
                return;
            }
            _clearArmed = false;
            RunClearRoomState();
        }

        public void Install(ILocalHost host)
        {
            if (_live)
            {
                Uninstall();
            }
            _host = host;
            _session = host != null ? host.Session : null;
            _diagnostics = host != null ? host.Diagnostics : null;
            _installCount++;

            var settings = host != null ? host.Settings : null;
            ResolveToggleKey(settings);
            var identity = host != null ? host.Identity : null;
            var isClone = identity != null ? identity.IsClone : MppmEnvironment.Current.IsClone;
            _panelVisible = OverlayLayout.PanelVisibleOnStart(settings, isClone);

            _onStateChanged ??= OnStateChanged;
            _onRoomJoined ??= MarkDirty;
            _onRoomLeft ??= OnRoomLeft;
            _onPeerChanged ??= OnPeerChanged;
            _onParticipantFrame ??= OnParticipantFrame;
            _onEngineState ??= OnEngineState;
            _onEngineSnapshot ??= OnEngineSnapshot;
            _onEngineRemoved ??= OnEngineRemoved;
            _onDiagnosticsChanged ??= OnDiagnosticsChanged;

            if (_session != null)
            {
                _session.StateChanged += _onStateChanged;
                _session.RoomJoined += _onRoomJoined;
                _session.RoomLeft += _onRoomLeft;
                _session.PeerJoined += _onPeerChanged;
                _session.PeerLeft += _onPeerChanged;
                _session.ParticipantFrameReceived += _onParticipantFrame;
                _session.EngineUserStateChanged += _onEngineState;
                _session.EngineUserStateSnapshot += _onEngineSnapshot;
                _session.EngineUserStateRemoved += _onEngineRemoved;
            }
            if (_diagnostics != null)
            {
                _diagnostics.Changed += _onDiagnosticsChanged;
            }

            // The modules live on the host object, which outlives scene loads; the canvas goes with it.
            _view = OverlayCanvas.Build(transform, _panelVisible, ShowPanel, HidePanel, OnRejoinClicked,
                OnLeaveJoinClicked, OnClearClicked);
            if (host != null)
            {
                host.Provide(_activity);
                // The window drives the same buttons, so the overlay and the window agree on Leave/Join and status.
                host.Provide(this);
            }

            _live = true;
            _dirty = true;
            _nextRefresh = 0f;
        }

        public void Uninstall()
        {
            if (!_live)
            {
                return;
            }
            _live = false;
            if (_session != null)
            {
                _session.StateChanged -= _onStateChanged;
                _session.RoomJoined -= _onRoomJoined;
                _session.RoomLeft -= _onRoomLeft;
                _session.PeerJoined -= _onPeerChanged;
                _session.PeerLeft -= _onPeerChanged;
                _session.ParticipantFrameReceived -= _onParticipantFrame;
                _session.EngineUserStateChanged -= _onEngineState;
                _session.EngineUserStateSnapshot -= _onEngineSnapshot;
                _session.EngineUserStateRemoved -= _onEngineRemoved;
            }
            if (_diagnostics != null)
            {
                _diagnostics.Changed -= _onDiagnosticsChanged;
            }
            if (_view != null)
            {
                _view.Destroy();
                _view = null;
            }
            _activity.Clear();
            _peers.Clear();
            _entries.Clear();
            _session = null;
            _diagnostics = null;
            _host = null;
            _keyboard = null;
            _toggleControl = null;
            _leftByUser = false;
            _clearArmed = false;
            _clearing = false;
            _status = string.Empty;
        }

        void OnDestroy()
        {
            // The host uninstalls first; this only matters when the host object dies without it (script reload).
            Uninstall();
        }

        void Update()
        {
            if (!_live || _view == null)
            {
                return;
            }
            PollToggleKey();
            // Every frame, so a resized Game view rescales at once rather than at the next text refresh.
            _view.UpdateScale();

            var now = Time.unscaledTime;
            if (_clearArmed && now >= _clearArmedUntil)
            {
                _clearArmed = false;
                _dirty = true;
            }
            if (now < _nextRefresh)
            {
                return;
            }
            _nextRefresh = now + RefreshSeconds;

            if (_activity.Tick(Time.realtimeSinceStartupAsDouble) && _panelVisible)
            {
                _dirty = true;
            }
            PollUnsignalled();
            if (!_dirty)
            {
                return;
            }
            _dirty = false;
            Render();
        }

        // ------------------------------------------------------------------ input

        void ResolveToggleKey(LocalMultiplayerSettings settings)
        {
            var name = settings != null ? settings.overlayKey : null;
            if (OverlayKeys.TryParse(name, out var key))
            {
                _toggleKey = key;
                _toggleKeyName = OverlayKeys.Canonical(name);
                return;
            }
            if (!string.IsNullOrWhiteSpace(name))
            {
                Debug.LogWarning($"[LocalMP][Overlay] \"{name}\" is not an Input System key name; "
                               + $"{OverlayKeys.DefaultName} shows and hides the overlay.");
            }
            _toggleKey = Key.F8;
            _toggleKeyName = OverlayKeys.DefaultName;
        }

        void PollToggleKey()
        {
            // No keyboard (a device change, or none at all): the pill and the Hide button still work.
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }
            if (!ReferenceEquals(keyboard, _keyboard))
            {
                _keyboard = keyboard;
                _toggleControl = ResolveControl(keyboard);
            }
            if (_toggleControl != null && _toggleControl.wasPressedThisFrame)
            {
                SetPanelVisible(!_panelVisible);
            }
        }

        KeyControl ResolveControl(Keyboard keyboard)
        {
            try
            {
                return keyboard[_toggleKey];
            }
            catch (ArgumentOutOfRangeException)
            {
                // A named key past what this keyboard layout exposes (the indexer throws rather than return null).
                if (_toggleKey == Key.F8)
                {
                    return null;
                }
                Debug.LogWarning($"[LocalMP][Overlay] The keyboard has no {_toggleKeyName} key; "
                               + $"{OverlayKeys.DefaultName} shows and hides the overlay.");
                _toggleKey = Key.F8;
                _toggleKeyName = OverlayKeys.DefaultName;
                return ResolveControl(keyboard);
            }
        }

        void ShowPanel() => SetPanelVisible(true);

        void HidePanel() => SetPanelVisible(false);

        void SetPanelVisible(bool visible)
        {
            if (_panelVisible == visible)
            {
                return;
            }
            _panelVisible = visible;
            _dirty = true;
            // Show the new state this frame, not at the next tick.
            _nextRefresh = 0f;
        }

        // ------------------------------------------------------------------ buttons

        void OnRejoinClicked()
        {
            if (!_live || _session == null)
            {
                return;
            }
            _leftByUser = false;
            _clearArmed = false;
            _status = "Rejoining: the page reloads and this player joins again, like a late joiner.";
            MarkDirty();
            _session.Rejoin();
        }

        void OnLeaveJoinClicked()
        {
            if (!_live || _session == null)
            {
                return;
            }
            if (ShowsJoin(_session.State))
            {
                _leftByUser = false;
                _status = "Joining the room again.";
                _session.Join();
            }
            else
            {
                // Production never sends an explicit leave: the socket closes, and the others keep this player's
                // user state through the 30 s grace and its objects through the 1 s authority grace.
                _leftByUser = true;
                _status = "Left the room: the connection is closed, as a dropped connection would be.";
                _session.Leave();
            }
            MarkDirty();
        }

        void OnClearClicked()
        {
            if (!_live || _session == null || _clearing)
            {
                return;
            }
            // Clearing wipes what the room has kept, for everyone: the first click arms it, the second clears.
            if (!_clearArmed)
            {
                _clearArmed = true;
                _clearArmedUntil = Time.unscaledTime + ClearConfirmSeconds;
                MarkDirty();
                return;
            }
            ClearRoomState();
        }

        async void RunClearRoomState()
        {
            var install = _installCount;
            var session = _session;
            _clearing = true;
            _status = "Clearing the room's space state\u2026";
            MarkDirty();

            string result;
            try
            {
                result = OverlayText.ClearOutcome(await session.ClearRoomStateAsync());
            }
            catch (LocalRelayException e)
            {
                result = "Couldn't clear the room: " + e.Code + ".";
            }
            catch (Exception e)
            {
                result = "Couldn't clear the room: " + e.Message;
            }

            // Play stopped, or the module was reinstalled, while the relay answered: nothing to show it on.
            if (this == null || !_live || install != _installCount)
            {
                return;
            }
            _clearing = false;
            _status = result;
            MarkDirty();
        }

        bool ShowsJoin(SessionState state) => _leftByUser && state == SessionState.Idle;

        // ------------------------------------------------------------------ session events

        void MarkDirty() => _dirty = true;

        void OnStateChanged(SessionState state)
        {
            if (state == SessionState.Joined)
            {
                _leftByUser = false;
            }
            _dirty = true;
        }

        void OnRoomLeft()
        {
            // The peers are gone, and so is what was known about them; the join's snapshot brings it back.
            _activity.Clear();
            _clearArmed = false;
            _dirty = true;
        }

        void OnPeerChanged(LocalPeer peer) => _dirty = true;

        void OnParticipantFrame(string roomSessionId, ParticipantFrame frame)
        {
            _activity.OnParticipantFrame(roomSessionId);
        }

        void OnEngineState(string roomSessionId, string path, string value, bool deleted)
        {
            if (_activity.OnEngineState(roomSessionId, path, value, deleted) && _panelVisible)
            {
                _dirty = true;
            }
        }

        void OnEngineSnapshot(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> users)
        {
            _activity.OnSnapshot(users);
            _dirty |= _panelVisible;
        }

        void OnEngineRemoved(string roomSessionId)
        {
            if (_activity.OnRemoved(roomSessionId) && _panelVisible)
            {
                _dirty = true;
            }
        }

        void OnDiagnosticsChanged() => _dirty |= _panelVisible;

        void PollUnsignalled()
        {
            if (_session == null)
            {
                return;
            }
            // StateChanged covers the state; this also catches a change raised before Install.
            var state = _session.State;
            if (state != _shownState)
            {
                _shownState = state;
                _dirty = true;
            }
            if (!_panelVisible)
            {
                return;
            }
            var rtt = RoundedRtt(_session.RttMs);
            if (rtt != _shownRtt)
            {
                _shownRtt = rtt;
                _dirty = true;
            }
            var hub = _session.Hub;
            var hubState = hub != null ? hub.State : HubState.Unknown;
            var hubMessage = hub != null ? hub.Message : null;
            if (hubState != _shownHubState || !string.Equals(hubMessage, _shownHubMessage, StringComparison.Ordinal))
            {
                _shownHubState = hubState;
                _shownHubMessage = hubMessage;
                _dirty = true;
            }
            var error = _session.LastError;
            if (!string.Equals(error, _shownError, StringComparison.Ordinal))
            {
                _shownError = error;
                _dirty = true;
            }
        }

        static long RoundedRtt(double ms)
        {
            if (double.IsNaN(ms) || double.IsInfinity(ms) || ms <= 0)
            {
                return -1;
            }
            return (long)Math.Round(ms);
        }

        // ------------------------------------------------------------------ text

        void Render()
        {
            var state = _session != null ? _session.State : SessionState.Idle;
            var peerCount = _session != null && _session.Peers != null ? _session.Peers.Count : 0;
            if (!_panelVisible)
            {
                _view.SetPill(_session != null
                    ? OverlayText.Pill(state, peerCount, _toggleKeyName)
                    : "LocalMP" + " \u00B7 no session \u00B7 " + _toggleKeyName);
                _view.ShowPanel(false);
                return;
            }

            _view.SetTitle("<b>Local multiplayer</b>  <color=" + Dim + ">" + _toggleKeyName + " hides</color>");
            _view.SetBlocks(BuildYou(), BuildRelay(state), BuildPlayers(state), BuildDiagnostics(), _status);

            var identity = _host != null ? _host.Identity : null;
            var isOwner = identity != null && identity.Role == PlayerRole.Owner;
            var showJoin = ShowsJoin(state);
            _view.SetButtons(
                _session != null,
                showJoin ? "Join" : "Leave",
                _session != null && (showJoin || state != SessionState.Idle),
                isOwner,
                _clearing ? "Clearing\u2026" : _clearArmed ? "Click again to clear" : "Clear room state",
                isOwner && !_clearing && state == SessionState.Joined);
            _view.ShowPanel(true);
        }

        string BuildYou()
        {
            var identity = _host != null ? _host.Identity : null;
            if (identity == null)
            {
                return string.Empty;
            }
            var text = _text.Clear();
            text.Append("<b>You</b>  ");
            AppendSwatch(text, identity.Tint);
            text.Append(OverlayText.Plain(OverlayText.SlotLabel(identity.Slot, identity.IsClone)));
            // A badge when this player has rights beyond a member's.
            if (identity.Role != PlayerRole.Member)
            {
                AppendRole(text, identity.Role);
            }
            if (!string.IsNullOrEmpty(identity.DisplayName)
                && !string.Equals(identity.DisplayName, identity.Slot, StringComparison.Ordinal))
            {
                text.Append(" \u00B7 ").Append(OverlayText.Plain(identity.DisplayName));
            }
            var ownRsid = _session != null ? _session.OwnRoomSessionId : null;
            text.Append("\nuid ").Append(OverlayText.ShortId(identity.Uid))
                .Append(" \u00B7 rsid ").Append(OverlayText.ShortId(ownRsid));
            if (_activity.TryGet(ownRsid, out var own))
            {
                var activity = OverlayText.Activity(own.SeatId, own.Attachments);
                if (activity.Length > 0)
                {
                    text.Append('\n').Append(OverlayText.Plain(activity));
                }
            }
            return text.ToString();
        }

        string BuildRelay(SessionState state)
        {
            if (_session == null)
            {
                return "<b>Relay</b>  <color=" + Dim + ">no session</color>";
            }
            var text = _text.Clear();
            text.Append("<b>Relay</b>  <color=").Append(OverlayText.StateColor(state)).Append('>')
                .Append(OverlayText.StateLabel(state)).Append("</color>");
            if (state == SessionState.Joined)
            {
                text.Append(" \u00B7 rtt ").Append(OverlayText.Rtt(_session.RttMs));
            }
            text.Append("\nroom ").Append(OverlayText.Plain(Or(_session.RoomId)))
                .Append('\n').Append(OverlayText.Plain(Or(_session.RelayUri)));

            // The hub line is what the probe found while the page loaded; nothing probes again after that, so
            // it only shows until this player is in the room (OverlayText.HubLine).
            var hub = _session.Hub;
            var hubLine = OverlayText.HubLine(hub, state);
            if (hubLine.Length > 0)
            {
                text.Append("\nhub ").Append(hubLine);
                if (hub != null && !string.IsNullOrEmpty(hub.Message))
                {
                    text.Append("\n<color=").Append(Dim).Append('>').Append(OverlayText.Plain(hub.Message)).Append("</color>");
                }
            }
            var settings = _host != null ? _host.Settings : null;
            text.Append('\n').Append(OverlayText.Persistence(hub, settings == null || settings.persistSpaceState));

            if (state != SessionState.Joined && !string.IsNullOrEmpty(_session.LastError))
            {
                text.Append("\n<color=").Append(OverlayText.LevelColor(DiagnosticLevel.Error)).Append('>')
                    .Append(OverlayText.Plain(_session.LastError)).Append("</color>");
            }
            return text.ToString();
        }

        string BuildPlayers(SessionState state)
        {
            var text = _text.Clear();
            text.Append("<b>Players</b>  ");
            if (_session == null || state != SessionState.Joined)
            {
                text.Append("<color=").Append(Dim).Append(">not in a room</color>");
                return text.ToString();
            }

            _peers.Clear();
            var peers = _session.Peers;
            if (peers != null)
            {
                foreach (var peer in peers)
                {
                    if (peer != null)
                    {
                        _peers.Add(peer);
                    }
                }
            }
            if (_peers.Count == 0)
            {
                text.Append("<color=").Append(Dim).Append(">only you</color>");
                return text.ToString();
            }
            _peers.Sort(BySlot);

            text.Append(OverlayText.PlayerCount(state, _peers.Count)).Append(" in the room");
            foreach (var peer in _peers)
            {
                text.Append('\n');
                AppendSwatch(text, peer.Tint);
                text.Append(OverlayText.Plain(OverlayText.PlayerName(peer.Slot, peer.DisplayName)));
                AppendRole(text, peer.Role);
                text.Append(" \u00B7 rsid ").Append(OverlayText.ShortId(peer.RoomSessionId));
                if (_activity.TryGet(peer.RoomSessionId, out var activity))
                {
                    text.Append(" \u00B7 ").Append(OverlayText.PoseRate(activity.PoseHz));
                    var seatAndAttachments = OverlayText.Activity(activity.SeatId, activity.Attachments);
                    if (seatAndAttachments.Length > 0)
                    {
                        text.Append("\n    <color=").Append(Dim).Append('>')
                            .Append(OverlayText.Plain(seatAndAttachments)).Append("</color>");
                    }
                }
                else
                {
                    text.Append(" \u00B7 ").Append(OverlayText.PoseRate(-1f));
                }
            }
            return text.ToString();
        }

        string BuildDiagnostics()
        {
            var text = _text.Clear();
            text.Append("<b>Diagnostics</b>");
            var entries = _diagnostics != null ? _diagnostics.Entries : null;
            _entries.Clear();
            if (entries != null)
            {
                for (var i = 0; i < entries.Count; i++)
                {
                    if (entries[i] != null)
                    {
                        _entries.Add(entries[i]);
                    }
                }
            }
            if (_entries.Count == 0)
            {
                text.Append("  <color=").Append(Dim).Append(">none</color>");
                return text.ToString();
            }
            _entries.Sort(NewestFirst);
            var shown = Math.Min(_entries.Count, MaxDiagnostics);
            for (var i = 0; i < shown; i++)
            {
                var entry = _entries[i];
                text.Append("\n<color=").Append(OverlayText.LevelColor(entry.Level)).Append(">\u2022 ")
                    .Append(OverlayText.Plain(entry.Message)).Append("</color>");
            }
            if (_entries.Count > shown)
            {
                text.Append("\n<color=").Append(Dim).Append(">+").Append(_entries.Count - shown)
                    .Append(" more in the Local Multiplayer window</color>");
            }
            return text.ToString();
        }

        static void AppendSwatch(StringBuilder text, Color tint)
        {
            text.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(tint)).Append('>').Append(Swatch)
                .Append("</color> ");
        }

        static void AppendRole(StringBuilder text, PlayerRole role)
        {
            text.Append("  <color=").Append(OverlayText.RoleColor(role)).Append('>')
                .Append(OverlayText.RoleLabel(role)).Append("</color>");
        }

        static string Or(string value) => string.IsNullOrEmpty(value) ? OverlayText.None : value;
    }
}
