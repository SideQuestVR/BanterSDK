using System;
using System.Collections.Generic;
using BS.LocalMultiplayer.Overlay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BS.LocalMultiplayer.Editor
{
    /// <summary>
    /// Local Multiplayer (Creator SDK > Setup, Tools section): test a world's multiplayer in the editor with Multiplayer Play Mode. It checks
    /// the environment (Multiplayer Play Mode, Ora, the desktop player), edits the settings every player reads,
    /// keeps the scenes loadable by the other players (saved, with stable object Ids) and shows the room during
    /// Play. Only the main editor writes the settings; in a Multiplayer Play Mode clone the window says where they
    /// are and shows that player's room.
    /// </summary>
    /// <remarks>
    /// IMGUI draws each frame in two passes (Layout, then the event), which must see the same controls. So
    /// everything shown is gathered on Layout events, and an action that changes what is shown ends the pass
    /// with <see cref="GUIUtility.ExitGUI"/>.
    /// </remarks>
    internal sealed class LocalMultiplayerWindow : EditorWindow
    {
        static readonly string[] KnownSlots =
        {
            LocalMultiplayerSettings.MainEditorSlot, "Player 2", "Player 3", "Player 4"
        };

        static readonly string[] ModeValues =
        {
            LocalMultiplayerSettings.ModeAuto, LocalMultiplayerSettings.ModeOn, LocalMultiplayerSettings.ModeOff
        };

        static readonly GUIContent[] ModeLabels =
        {
            new GUIContent("Auto"), new GUIContent("On"), new GUIContent("Off")
        };

        const double EnvironmentRefreshSeconds = 2.0;
        // Scene edits mark the Id scan stale on every change; the scan itself runs at most this often.
        const double ScanIntervalSeconds = 1.0;
        const double LiveRepaintSeconds = 0.25;
        const int MaxListedFindings = 12;
        const int MaxListedDiagnostics = 30;

        // Opened from the Setup panel's Tools section (LocalMultiplayerSetupTool).
        internal static void Open()
        {
            var window = GetWindow<LocalMultiplayerWindow>();
            window.titleContent = new GUIContent("Local Multiplayer", Resources.Load<Texture2D>("UI/Images/altspace-window-icon"));
            window.minSize = new Vector2(400f, 320f);
            window.Show();
        }

        sealed class LivePeer
        {
            public Color Tint;
            public string Line;
            public string Activity;
        }

        sealed class LiveDiagnostic
        {
            public string Color;
            public string Message;
        }

        string _root;
        LocalMultiplayerSettings _settings;
        Vector2 _scroll;
        GUIStyle _rich;
        GUIStyle _note;

        // ------------------------------------------------ gathered on Layout events
        bool _isClone;
        string _slot;
        bool _playing;
        double _nextEnvironmentRefresh;
        bool _mppmInstalled;
        bool _mppmUsable;
        bool _mppmCore;
        string _mppmVersion;
        string _oraVersion;
        bool _oraOk;
        bool _flexaBody;
        bool _runInBackground;
        string[] _roomFiles = Array.Empty<string>();
        long _roomBytes;

        List<Scene> _untitled = new List<Scene>();
        List<Scene> _dirty = new List<Scene>();
        List<ObjectIdSweep.Finding> _findings = new List<ObjectIdSweep.Finding>();
        int _networkedCount;
        bool _scanStale = true;
        double _lastScan = double.NegativeInfinity;
        readonly List<string> _moderatorChoices = new List<string>();
        string[] _ownerChoices = KnownSlots;

        ILocalHost _host;
        bool _liveHasSession;
        SessionState _liveState;
        string _liveYou;
        string _liveStateText;
        string _liveRoom;
        string _liveRelay;
        string _liveHub;
        string _liveHubMessage;
        string _livePersistence;
        string _liveError;
        string _liveStatus;
        bool _liveOffersJoin;
        bool _liveIsOwner;
        bool _liveClearing;
        readonly List<LivePeer> _livePeers = new List<LivePeer>();
        readonly List<LiveDiagnostic> _liveDiagnostics = new List<LiveDiagnostic>();
        int _liveDiagnosticsHidden;

        // ------------------------------------------------ messages and repaint pacing
        string _message;
        MessageType _messageType;
        string _overlayKeyError;
        string _overlayKeyErrorShown;
        double _nextLiveRepaint;
        bool _repaintWanted;

        void OnEnable()
        {
            _root = MppmEnvironment.Current.MainProjectRoot;
            ReloadSettings();
            EditorApplication.hierarchyChanged += OnSceneContentChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorSceneManager.sceneSaved += OnSceneChanged;
            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorSceneManager.sceneClosed += OnSceneChanged;
            EditorSceneManager.sceneDirtied += OnSceneChanged;
            Undo.undoRedoPerformed += OnSceneContentChanged;
            LocalMpPackages.InstallFinished += OnInstallFinished;
            LocalMultiplayerRuntime.Installed += OnRuntimeInstalled;
            LocalMultiplayerRuntime.Uninstalled += OnRuntimeUninstalled;
            _nextEnvironmentRefresh = 0;
            _scanStale = true;
        }

        void OnDisable()
        {
            EditorApplication.hierarchyChanged -= OnSceneContentChanged;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorSceneManager.sceneSaved -= OnSceneChanged;
            EditorSceneManager.sceneOpened -= OnSceneOpened;
            EditorSceneManager.sceneClosed -= OnSceneChanged;
            EditorSceneManager.sceneDirtied -= OnSceneChanged;
            Undo.undoRedoPerformed -= OnSceneContentChanged;
            LocalMpPackages.InstallFinished -= OnInstallFinished;
            LocalMultiplayerRuntime.Installed -= OnRuntimeInstalled;
            LocalMultiplayerRuntime.Uninstalled -= OnRuntimeUninstalled;
            _host = null;
        }

        void OnFocus()
        {
            // The file may have changed while the window was in the background (only this window writes it, but
            // a second window or a hand edit can).
            ReloadSettings();
            _nextEnvironmentRefresh = 0;
            _scanStale = true;
        }

        void OnInspectorUpdate()
        {
            var now = EditorApplication.timeSinceStartup;
            if (EditorApplication.isPlaying)
            {
                if (now >= _nextLiveRepaint)
                {
                    _nextLiveRepaint = now + LiveRepaintSeconds;
                    Repaint();
                }
                return;
            }
            if (_repaintWanted || LocalMpPackages.Installing || (_scanStale && now - _lastScan >= ScanIntervalSeconds))
            {
                _repaintWanted = false;
                Repaint();
            }
        }

        void OnSceneContentChanged()
        {
            _scanStale = true;
            _repaintWanted = true;
        }

        void OnSceneChanged(Scene scene) => OnSceneContentChanged();

        void OnSceneOpened(Scene scene, OpenSceneMode mode) => OnSceneContentChanged();

        void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            _nextEnvironmentRefresh = 0;
            _scanStale = true;
            _repaintWanted = true;
            Repaint();
        }

        void OnInstallFinished()
        {
            _nextEnvironmentRefresh = 0;
            Repaint();
        }

        void OnRuntimeInstalled(ILocalHost host) => Repaint();

        void OnRuntimeUninstalled() => Repaint();

        void ReloadSettings()
        {
            if (string.IsNullOrEmpty(_root))
            {
                _root = MppmEnvironment.Current.MainProjectRoot;
            }
            _settings = LocalMultiplayerSettings.Load(_root);
        }

        void SaveSettings()
        {
            try
            {
                _settings.Save(_root);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LocalMP] Couldn't save {LocalMultiplayerSettings.FilePath(_root)}: {e.Message}");
                SetMessage("Couldn't save the settings: " + e.Message, MessageType.Error);
            }
        }

        void SetMessage(string message, MessageType type)
        {
            _message = message;
            _messageType = type;
        }

        // ================================================================ gathering (Layout events only)

        void Gather()
        {
            var now = EditorApplication.timeSinceStartup;
            var environment = MppmEnvironment.Current;
            _isClone = environment.IsClone;
            _slot = environment.Slot;
            _playing = EditorApplication.isPlaying;
            if (_settings == null)
            {
                ReloadSettings();
            }
            if (now >= _nextEnvironmentRefresh)
            {
                _nextEnvironmentRefresh = now + EnvironmentRefreshSeconds;
                GatherEnvironment();
            }
            GatherLive();
            if (_isClone)
            {
                return;
            }

            _overlayKeyErrorShown = _overlayKeyError;
            _ownerChoices = SlotChoices(_settings.worldOwnerSlot);
            _moderatorChoices.Clear();
            foreach (var slot in KnownSlots)
            {
                if (!string.Equals(slot, _settings.worldOwnerSlot, StringComparison.Ordinal))
                {
                    _moderatorChoices.Add(slot);
                }
            }
            if (_settings.moderatorSlots != null)
            {
                // Slots only the file names (a clone started without -name is "vp-<id>"): shown so they can go.
                foreach (var slot in _settings.moderatorSlots)
                {
                    if (!string.IsNullOrEmpty(slot) && !_moderatorChoices.Contains(slot)
                        && !string.Equals(slot, _settings.worldOwnerSlot, StringComparison.Ordinal))
                    {
                        _moderatorChoices.Add(slot);
                    }
                }
            }

            if (_playing)
            {
                return;
            }
            _untitled = LocalMpScenes.Untitled();
            _dirty = LocalMpScenes.Dirty();
            if (_scanStale && now - _lastScan >= ScanIntervalSeconds)
            {
                _findings = ObjectIdSweep.Find(out _networkedCount);
                _lastScan = now;
                _scanStale = false;
            }
        }

        void GatherEnvironment()
        {
            var mppm = LocalMpPackages.Find(LocalMpPackages.MppmName);
            _mppmInstalled = mppm != null;
            _mppmVersion = mppm != null ? mppm.version : null;
            _mppmUsable = mppm != null && LocalMpPackages.IsUsableMppm(mppm.version);
            _mppmCore = LocalMpPackages.MppmIsCore(Application.unityVersion);

            var ora = LocalMpPackages.FindOra();
            _oraVersion = ora != null ? ora.version : null;
            _oraOk = ora != null && LocalMpPackages.MeetsMinimum(ora.version, LocalMpPackages.OraMinimumVersion);

            _flexaBody = LocalMpPackages.Find(LocalMpPackages.FlexaBodyName) != null;
            _runInBackground = PlayerSettings.runInBackground;

            _roomFiles = RoomStateFiles.List(RoomStateFiles.DirectoryFor(_root));
            _roomBytes = RoomStateFiles.TotalBytes(_roomFiles);
        }

        void GatherLive()
        {
            _host = _playing ? LocalMultiplayerRuntime.Current : null;
            _livePeers.Clear();
            _liveDiagnostics.Clear();
            _liveDiagnosticsHidden = 0;
            _liveHasSession = false;
            if (_host == null)
            {
                return;
            }

            var session = _host.Session;
            var identity = _host.Identity;
            var overlay = _host.Get<OverlayModule>();
            var activity = overlay != null ? overlay.Activity : _host.Get<PeerActivity>();
            var ownRsid = session != null ? session.OwnRoomSessionId : null;

            _liveYou = identity == null
                ? OverlayText.None
                : OverlayText.SlotLabel(identity.Slot, identity.IsClone)
                  + " \u00B7 " + OverlayText.RoleLabel(identity.Role)
                  + " \u00B7 uid " + OverlayText.ShortId(identity.Uid)
                  + " \u00B7 rsid " + OverlayText.ShortId(ownRsid);
            if (activity != null && activity.TryGet(ownRsid, out var own))
            {
                _liveYou = OverlayText.Join(_liveYou, OverlayText.Activity(own.SeatId, own.Attachments));
            }
            _liveIsOwner = identity != null && identity.Role == PlayerRole.Owner;
            _liveStatus = overlay != null ? overlay.Status : null;
            _liveClearing = overlay != null && overlay.Clearing;

            if (session == null)
            {
                return;
            }
            _liveHasSession = true;
            _liveState = session.State;
            _liveOffersJoin = overlay != null ? overlay.OffersJoin : _liveState == SessionState.Idle;
            _liveStateText = "<color=" + OverlayText.StateColor(_liveState) + ">" + OverlayText.StateLabel(_liveState)
                             + "</color>"
                             + (_liveState == SessionState.Joined ? " \u00B7 rtt " + OverlayText.Rtt(session.RttMs) : "");
            _liveRoom = Or(session.RoomId);
            _liveRelay = Or(session.RelayUri);
            var hub = session.Hub;
            // What the probe found while the page loaded, so only until this player is in the room (as the overlay).
            _liveHub = OverlayText.HubLine(hub, _liveState);
            _liveHubMessage = _liveHub.Length > 0 && hub != null ? hub.Message : null;
            var settings = _host.Settings;
            _livePersistence = OverlayText.Persistence(hub, settings == null || settings.persistSpaceState);
            _liveError = _liveState != SessionState.Joined ? session.LastError : null;

            var peers = new List<LocalPeer>();
            if (session.Peers != null)
            {
                foreach (var peer in session.Peers)
                {
                    if (peer != null)
                    {
                        peers.Add(peer);
                    }
                }
            }
            peers.Sort((a, b) => string.CompareOrdinal(a.Slot, b.Slot));
            foreach (var peer in peers)
            {
                var row = new LivePeer
                {
                    Tint = peer.Tint,
                    Line = OverlayText.PlayerName(peer.Slot, peer.DisplayName)
                           + " \u00B7 " + OverlayText.RoleLabel(peer.Role)
                           + " \u00B7 rsid " + OverlayText.ShortId(peer.RoomSessionId),
                    Activity = string.Empty,
                };
                if (activity != null && activity.TryGet(peer.RoomSessionId, out var entry))
                {
                    row.Line += " \u00B7 " + OverlayText.PoseRate(entry.PoseHz);
                    row.Activity = OverlayText.Activity(entry.SeatId, entry.Attachments);
                }
                _livePeers.Add(row);
            }

            var diagnostics = _host.Diagnostics;
            var entries = diagnostics != null ? diagnostics.Entries : null;
            if (entries != null && entries.Count > 0)
            {
                var sorted = new List<DiagnosticEntry>(entries.Count);
                for (var i = 0; i < entries.Count; i++)
                {
                    if (entries[i] != null)
                    {
                        sorted.Add(entries[i]);
                    }
                }
                sorted.Sort((a, b) => b.Time.CompareTo(a.Time));
                var shown = Math.Min(sorted.Count, MaxListedDiagnostics);
                for (var i = 0; i < shown; i++)
                {
                    _liveDiagnostics.Add(new LiveDiagnostic
                    {
                        Color = OverlayText.LevelColor(sorted[i].Level),
                        Message = OverlayText.Plain(sorted[i].Message),
                    });
                }
                _liveDiagnosticsHidden = sorted.Count - shown;
            }
        }

        static string[] SlotChoices(string current)
        {
            if (string.IsNullOrEmpty(current) || Array.IndexOf(KnownSlots, current) >= 0)
            {
                return KnownSlots;
            }
            var choices = new string[KnownSlots.Length + 1];
            KnownSlots.CopyTo(choices, 0);
            choices[KnownSlots.Length] = current;
            return choices;
        }

        static string Or(string value) => string.IsNullOrEmpty(value) ? OverlayText.None : value;

        // ================================================================ drawing

        void OnGUI()
        {
            if (Event.current.type == EventType.Layout)
            {
                Gather();
            }
            EnsureStyles();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            if (_isClone)
            {
                DrawCloneNotice();
                DrawMessage();
                DrawLive();
            }
            else
            {
                EditorGUILayout.LabelField(
                    "Test this world's multiplayer with Multiplayer Play Mode: each player runs in its own editor, and "
                    + "they meet in a room on a relay in this editor's Ora app.", _note);
                DrawMessage();
                DrawEnvironment();
                DrawSettings();
                DrawScenes();
                DrawLive();
            }
            EditorGUILayout.EndScrollView();
        }

        void EnsureStyles()
        {
            if (_rich != null)
            {
                return;
            }
            _rich = new GUIStyle(EditorStyles.label) { richText = true, wordWrap = true };
            _note = new GUIStyle(EditorStyles.wordWrappedMiniLabel) { richText = true };
        }

        static void Header(string text)
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField(text, EditorStyles.boldLabel);
        }

        void Row(string label, string value)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(label);
            GUILayout.Label(value ?? OverlayText.None, _rich);
            EditorGUILayout.EndHorizontal();
        }

        static void CopyableRow(string label, string value)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(label);
            EditorGUILayout.SelectableLabel(value ?? OverlayText.None, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            EditorGUILayout.EndHorizontal();
        }

        void DrawMessage()
        {
            if (string.IsNullOrEmpty(_message))
            {
                return;
            }
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.HelpBox(_message, _messageType);
            if (GUILayout.Button("OK", GUILayout.Width(36f), GUILayout.Height(EditorGUIUtility.singleLineHeight * 2f)))
            {
                _message = null;
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.EndHorizontal();
        }

        void DrawCloneNotice()
        {
            Header("Multiplayer Play Mode player");
            EditorGUILayout.HelpBox(
                $"This editor is Multiplayer Play Mode's \"{_slot}\". Local multiplayer settings live in the main "
                + "editor: open Creator SDK > Setup there and press Open on Local Multiplayer. Every player reads them from "
                + LocalMultiplayerSettings.FilePath(_root) + ".", MessageType.Info);
        }

        // ---------------------------------------------------------------- environment

        void DrawEnvironment()
        {
            Header("Environment");

            if (LocalMpPackages.Installing)
            {
                Row("Multiplayer Play Mode", "installing\u2026");
            }
            else if (_mppmUsable)
            {
                Row("Multiplayer Play Mode", _mppmVersion);
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(EditorGUIUtility.labelWidth);
                if (GUILayout.Button("Open Multiplayer Play Mode"))
                {
                    if (!LocalMpPackages.OpenMppmWindow())
                    {
                        SetMessage("Couldn't open Window > Multiplayer > Multiplayer Play Mode. If the package was just "
                                   + "installed, let the editor finish recompiling, or restart it.", MessageType.Warning);
                    }
                    GUIUtility.ExitGUI();
                }
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.HelpBox(_mppmInstalled
                        ? $"Multiplayer Play Mode {_mppmVersion} doesn't run on this editor: it needs 2.0 or later."
                        : "Multiplayer Play Mode isn't installed. It runs up to three more editor players next to this "
                          + "one, and they join this editor's room.",
                    _mppmInstalled ? MessageType.Warning : MessageType.Info);
                if (_mppmCore)
                {
                    EditorGUILayout.LabelField("Install it from Window > Package Manager: it comes with this editor.", _note);
                }
                else
                {
                    using (new EditorGUI.DisabledScope(_playing))
                    {
                        if (GUILayout.Button($"Install Multiplayer Play Mode {LocalMpPackages.MppmPinnedVersion}"))
                        {
                            LocalMpPackages.InstallMppm();
                            GUIUtility.ExitGUI();
                        }
                    }
                }
            }
            if (!string.IsNullOrEmpty(LocalMpPackages.InstallMessage))
            {
                EditorGUILayout.LabelField(LocalMpPackages.InstallMessage, _note);
            }

            Row("Ora", _oraVersion ?? "not found");
            if (!_oraOk)
            {
                EditorGUILayout.HelpBox($"Local multiplayer needs Ora {LocalMpPackages.OraMinimumVersion} or newer: its "
                                        + "Electron app runs the relay.", MessageType.Error);
            }

            if (_flexaBody)
            {
                EditorGUILayout.HelpBox("FlexaBody is installed. Local multiplayer doesn't support FlexaBody projects: "
                                        + "they have no desktop player or local user.", MessageType.Error);
            }

            if (!_runInBackground)
            {
                EditorGUILayout.HelpBox("Run In Background (Player Settings) is off, so a player whose window isn't "
                                        + "focused stops updating, and the others see its avatar freeze.",
                    MessageType.Warning);
                using (new EditorGUI.DisabledScope(_playing))
                {
                    // Only on this click: it changes a project setting, which local multiplayer never does by itself.
                    if (GUILayout.Button(new GUIContent("Turn On Run In Background in Player Settings",
                            "Changes this project's Player Settings > Resolution and Presentation > Run In Background. "
                            + "It is saved in ProjectSettings/ProjectSettings.asset, which is usually version-controlled, "
                            + "unlike the local multiplayer settings.")))
                    {
                        PlayerSettings.runInBackground = true;
                        _nextEnvironmentRefresh = 0;
                        GUIUtility.ExitGUI();
                    }
                }
            }
        }

        // ---------------------------------------------------------------- settings

        void DrawSettings()
        {
            Header("Settings");
            EditorGUILayout.LabelField("Saved in UserSettings (not version-controlled). Every player reads them when "
                                       + "Play starts.", _note);

            EditorGUI.BeginChangeCheck();

            var mode = string.IsNullOrEmpty(_settings.mode) ? LocalMultiplayerSettings.ModeAuto : _settings.mode.ToLowerInvariant();
            var modeIndex = Math.Max(0, Array.IndexOf(ModeValues, mode));
            modeIndex = EditorGUILayout.Popup(
                new GUIContent("Mode", "Auto: on when Multiplayer Play Mode is installed."), modeIndex, ModeLabels);
            _settings.mode = ModeValues[modeIndex];
            EditorGUILayout.LabelField(" ", "auto = on when Multiplayer Play Mode is installed", EditorStyles.miniLabel);

            var ownerIndex = Math.Max(0, Array.IndexOf(_ownerChoices, _settings.worldOwnerSlot));
            ownerIndex = EditorGUILayout.Popup(
                new GUIContent("World owner", "Their one-shots arrive with fromAdmin, and they may write protected "
                                              + "space state and clear the room."),
                ownerIndex, Contents(_ownerChoices));
            _settings.worldOwnerSlot = _ownerChoices[ownerIndex];

            DrawModerators();

            _settings.persistSpaceState = EditorGUILayout.Toggle(
                new GUIContent("Keep space state", "Keep the room's space state between play sessions, as a "
                                                   + "production room keeps it for 24 hours."),
                _settings.persistSpaceState);
            _settings.autoSaveScenesOnPlay = EditorGUILayout.Toggle(
                new GUIContent("Save scenes on Play", "Multiplayer Play Mode players only load saved scenes. Off: "
                                                      + "you are asked instead."),
                _settings.autoSaveScenesOnPlay);
            DrawOverlayKey();
            _settings.overlayVisibleOnStart = EditorGUILayout.Toggle(
                new GUIContent("Show overlay at start", "In the main editor; otherwise the overlay starts as a one-line "
                                                        + "pill. Multiplayer Play Mode players always start with the "
                                                        + "pill, so the panel doesn't cover their small Game view."),
                _settings.overlayVisibleOnStart);

            if (EditorGUI.EndChangeCheck())
            {
                SaveSettings();
                GUIUtility.ExitGUI();
            }

            Row("Local multiplayer", _settings.IsEnabled ? "<b>on</b> for this project" : "off for this project");
            if (_playing)
            {
                EditorGUILayout.LabelField("Changes apply the next time Play starts.", _note);
            }
            DrawRoomState();
        }

        void DrawModerators()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(new GUIContent("Moderators", "They may write protected space state too, like a "
                                                                    + "world's moderators in production."));
            EditorGUILayout.BeginVertical();
            if (_settings.moderatorSlots == null)
            {
                _settings.moderatorSlots = new List<string>();
            }
            foreach (var slot in _moderatorChoices)
            {
                var on = _settings.moderatorSlots.Contains(slot);
                var now = EditorGUILayout.ToggleLeft(slot, on);
                if (now == on)
                {
                    continue;
                }
                if (now)
                {
                    _settings.moderatorSlots.Add(slot);
                }
                else
                {
                    _settings.moderatorSlots.RemoveAll(s => string.Equals(s, slot, StringComparison.Ordinal));
                }
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
        }

        void DrawOverlayKey()
        {
            var entered = EditorGUILayout.DelayedTextField(
                new GUIContent("Overlay key", "The Input System key that shows and hides the in-game overlay, such as "
                                              + "F8, F9 or Backquote."),
                _settings.overlayKey ?? string.Empty);
            if (!string.Equals(entered, _settings.overlayKey, StringComparison.Ordinal))
            {
                var canonical = OverlayKeys.Canonical(entered);
                if (canonical != null)
                {
                    _settings.overlayKey = canonical;
                    _overlayKeyError = null;
                }
                else
                {
                    _overlayKeyError = $"\"{entered}\" isn't an Input System key name. Try F8, F9 or Backquote.";
                }
            }
            // As gathered on Layout: a box that appears mid-event would shift every control after it.
            if (!string.IsNullOrEmpty(_overlayKeyErrorShown))
            {
                EditorGUILayout.HelpBox(_overlayKeyErrorShown, MessageType.Warning);
            }
        }

        void DrawRoomState()
        {
            var count = _roomFiles.Length;
            Row("Saved space state", count == 0
                ? "none"
                : $"{count} room{(count == 1 ? "" : "s")} ({EditorUtility.FormatBytes(_roomBytes)})");

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(EditorGUIUtility.labelWidth);
            if (_playing)
            {
                // The hub holds the state during Play and would write it back: the world owner clears it through
                // the relay (admin.clearRoomState), for everyone in the room.
                var canClear = _host != null && _liveHasSession && _liveIsOwner && _liveState == SessionState.Joined
                               && !_liveClearing;
                using (new EditorGUI.DisabledScope(!canClear))
                {
                    if (GUILayout.Button(new GUIContent("Clear Room State\u2026",
                            "During Play only the world owner can clear the room, once joined.")))
                    {
                        ClearLive();
                    }
                }
            }
            else
            {
                using (new EditorGUI.DisabledScope(count == 0))
                {
                    if (GUILayout.Button("Clear Room State\u2026"))
                    {
                        ClearSaved();
                    }
                }
            }
            if (GUILayout.Button("Show Folder"))
            {
                var folder = RoomStateFiles.DirectoryFor(_root);
                if (System.IO.Directory.Exists(folder))
                {
                    EditorUtility.RevealInFinder(folder);
                }
                else
                {
                    SetMessage("No space state has been saved yet: " + folder, MessageType.Info);
                    GUIUtility.ExitGUI();
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        void ClearSaved()
        {
            var folder = RoomStateFiles.DirectoryFor(_root);
            var files = RoomStateFiles.List(folder);
            if (files.Length == 0)
            {
                SetMessage("There is no saved space state to clear.", MessageType.Info);
                GUIUtility.ExitGUI();
                return;
            }
            var rooms = $"{files.Length} room{(files.Length == 1 ? "" : "s")}";
            if (EditorUtility.DisplayDialog("Clear room state",
                    $"Delete the space state saved for {rooms}? The next play session starts with empty space state.\n\n"
                    + folder, "Delete", "Cancel"))
            {
                var deleted = RoomStateFiles.Delete(files, out var error);
                if (error == null)
                {
                    SetMessage($"Deleted the space state saved for {rooms}.", MessageType.Info);
                }
                else
                {
                    SetMessage($"Deleted {deleted} of {files.Length} files. {error}", MessageType.Warning);
                }
                Debug.Log($"[LocalMP] Cleared the saved space state ({deleted} file{(deleted == 1 ? "" : "s")}) in {folder}.");
            }
            _nextEnvironmentRefresh = 0;
            GUIUtility.ExitGUI();
        }

        void ClearLive()
        {
            var host = LocalMultiplayerRuntime.Current;
            if (host == null || host.Session == null)
            {
                GUIUtility.ExitGUI();
                return;
            }
            if (EditorUtility.DisplayDialog("Clear room state",
                    "Clear the room's space state for everyone in it? The relay also deletes what it saved.",
                    "Clear", "Cancel"))
            {
                var overlay = host.Get<OverlayModule>();
                if (overlay != null)
                {
                    // One implementation for both, so the overlay and this window show the same outcome.
                    overlay.ClearRoomState();
                }
                else
                {
                    ClearThroughSession(host.Session);
                }
            }
            GUIUtility.ExitGUI();
        }

        async void ClearThroughSession(ILocalSession session)
        {
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
            // The window may have closed while the relay answered.
            if (this == null)
            {
                return;
            }
            SetMessage(result, MessageType.Info);
            Repaint();
        }

        // ---------------------------------------------------------------- scenes

        void DrawScenes()
        {
            Header("Scenes");
            if (_playing)
            {
                EditorGUILayout.LabelField("Scene checks run outside Play.", _note);
                return;
            }

            if (_untitled.Count > 0)
            {
                EditorGUILayout.HelpBox($"Never saved: {LocalMpScenes.Names(_untitled)}. Multiplayer Play Mode players "
                                        + "can't load a scene that isn't saved.", MessageType.Warning);
            }
            if (_dirty.Count > 0)
            {
                EditorGUILayout.HelpBox($"Unsaved changes in {LocalMpScenes.Names(_dirty)}. The other players load the "
                                        + "saved version.", MessageType.Warning);
            }
            if (_untitled.Count + _dirty.Count > 0)
            {
                if (GUILayout.Button("Save All"))
                {
                    // Untitled scenes go through Unity's save dialog.
                    if (LocalMpScenes.SaveUntitled(LocalMpScenes.Untitled()))
                    {
                        LocalMpScenes.SaveDirty(LocalMpScenes.Dirty());
                    }
                    _scanStale = true;
                    GUIUtility.ExitGUI();
                }
            }
            else
            {
                Row("Open scenes", "all saved");
            }

            EditorGUILayout.Space(4f);
            var unstable = _findings.Count;
            Row("Networked objects", unstable == 0
                ? $"{_networkedCount}, all with stable Ids"
                : $"{_networkedCount}, <b>{unstable}</b> with unstable Ids");
            if (unstable > 0)
            {
                EditorGUILayout.HelpBox("Players find a networked object (synced, attached, seat) by its BSObjectId. "
                                        + "These Ids are empty, duplicated, or instance ids that each editor picks for "
                                        + "itself, so the other players won't find the same object.", MessageType.Warning);
                var listed = Math.Min(unstable, MaxListedFindings);
                for (var i = 0; i < listed; i++)
                {
                    DrawFinding(_findings[i]);
                }
                if (unstable > listed)
                {
                    EditorGUILayout.LabelField($"\u2026and {unstable - listed} more", _note);
                }
            }

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(unstable == 0))
            {
                if (GUILayout.Button("Assign Stable Ids\u2026"))
                {
                    ObjectIdSweep.AssignWithConfirmation(_findings);
                    _scanStale = true;
                    _lastScan = double.NegativeInfinity;
                    GUIUtility.ExitGUI();
                }
            }
            if (GUILayout.Button("Refresh", GUILayout.Width(70f)))
            {
                _scanStale = true;
                _lastScan = double.NegativeInfinity;
                _nextEnvironmentRefresh = 0;
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.EndHorizontal();
        }

        void DrawFinding(ObjectIdSweep.Finding finding)
        {
            EditorGUILayout.BeginHorizontal();
            var component = finding.Component;
            var alive = component != null;
            if (GUILayout.Button(alive ? component.name : "(deleted)", EditorStyles.linkLabel) && alive)
            {
                Selection.activeGameObject = component.gameObject;
                EditorGUIUtility.PingObject(component.gameObject);
            }
            var id = alive ? component.Id : null;
            GUILayout.Label(ObjectIdSweep.Describe(finding.Problem) + (string.IsNullOrEmpty(id) ? "" : " (" + id + ")"),
                EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        // ---------------------------------------------------------------- live

        void DrawLive()
        {
            Header("Live");
            if (!_playing)
            {
                EditorGUILayout.LabelField("Press Play to see the room here.", _note);
                return;
            }
            if (_host == null)
            {
                EditorGUILayout.HelpBox("Local multiplayer isn't running in this player: it is off in the settings, or Play is "
                                        + "still starting.", MessageType.Info);
                return;
            }

            Row("You", _liveYou);
            if (!_liveHasSession)
            {
                EditorGUILayout.HelpBox("This player has no relay session.", MessageType.Warning);
                return;
            }
            Row("State", _liveStateText);
            CopyableRow("Room", _liveRoom);
            CopyableRow("Relay", _liveRelay);
            if (!string.IsNullOrEmpty(_liveHub))
            {
                Row("Hub", _liveHub);
                if (!string.IsNullOrEmpty(_liveHubMessage))
                {
                    EditorGUILayout.LabelField(_liveHubMessage, _note);
                }
            }
            Row("Space state", _livePersistence);
            if (!string.IsNullOrEmpty(_liveError))
            {
                EditorGUILayout.HelpBox(_liveError, MessageType.Warning);
            }

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField(_livePeers.Count == 0
                ? (_liveState == SessionState.Joined ? "Players: only you" : "Players: not in a room")
                : "Players: " + (_livePeers.Count + 1) + " in the room", EditorStyles.boldLabel);
            foreach (var peer in _livePeers)
            {
                EditorGUILayout.BeginHorizontal();
                var swatch = GUILayoutUtility.GetRect(10f, 10f, GUILayout.Width(10f), GUILayout.Height(10f));
                swatch.y += 4f;
                EditorGUI.DrawRect(swatch, peer.Tint);
                GUILayout.Label(peer.Line, _rich);
                EditorGUILayout.EndHorizontal();
                if (!string.IsNullOrEmpty(peer.Activity))
                {
                    EditorGUILayout.LabelField("    " + peer.Activity, _note);
                }
            }

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField(_liveDiagnostics.Count == 0 ? "Diagnostics: none" : "Diagnostics",
                EditorStyles.boldLabel);
            foreach (var diagnostic in _liveDiagnostics)
            {
                GUILayout.Label("<color=" + diagnostic.Color + ">\u2022</color> " + diagnostic.Message, _rich);
            }
            if (_liveDiagnosticsHidden > 0)
            {
                EditorGUILayout.LabelField($"\u2026and {_liveDiagnosticsHidden} older", _note);
            }

            EditorGUILayout.Space(4f);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Rejoin", "Reload the page: this player leaves and joins again as a late joiner.")))
            {
                var overlay = _host.Get<OverlayModule>();
                if (overlay != null)
                {
                    overlay.Rejoin();
                }
                else if (_host.Session != null)
                {
                    _host.Session.Rejoin();
                }
                GUIUtility.ExitGUI();
            }
            using (new EditorGUI.DisabledScope(!_liveOffersJoin && _liveState == SessionState.Idle))
            {
                if (GUILayout.Button(_liveOffersJoin ? "Join" : "Leave"))
                {
                    var overlay = _host.Get<OverlayModule>();
                    if (overlay != null)
                    {
                        overlay.LeaveOrJoin();
                    }
                    else if (_host.Session != null)
                    {
                        if (_liveOffersJoin)
                        {
                            _host.Session.Join();
                        }
                        else
                        {
                            _host.Session.Leave();
                        }
                    }
                    GUIUtility.ExitGUI();
                }
            }
            if (_liveIsOwner)
            {
                using (new EditorGUI.DisabledScope(_liveState != SessionState.Joined || _liveClearing))
                {
                    if (GUILayout.Button(_liveClearing ? "Clearing\u2026" : "Clear Room State\u2026"))
                    {
                        ClearLive();
                    }
                }
            }
            EditorGUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(_liveStatus))
            {
                EditorGUILayout.LabelField(_liveStatus, _note);
            }
        }

        static GUIContent[] Contents(string[] labels)
        {
            var contents = new GUIContent[labels.Length];
            for (var i = 0; i < labels.Length; i++)
            {
                contents[i] = new GUIContent(labels[i]);
            }
            return contents;
        }
    }
}
