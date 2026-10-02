using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using SideQuest.Ora;
using UnityEngine;

namespace BS.LocalMultiplayer
{
    /// <summary>
    /// The in-editor stand-in for the Greenfield client's networking (NetworkService and its bridges), on a
    /// DontDestroyOnLoad "[LocalMultiplayer]" GameObject. Created when BSStarterUpper links the scene, before
    /// any scene object has started, it: stamps and announces the local user, so "me" resolves as in
    /// production; creates the relay session; adds every [LocalModule] of this assembly and installs them in
    /// order, each attaching its SDK listeners and delegates at once; and only then sets
    /// BSNetworkHost.Active, so the SDK's offline stand-ins (the Visual Scripting space-state loopback, the
    /// per-page user replay) step aside only when their replacements are in place. If any module fails, all
    /// of it comes off again and the SDK behaves as it does offline.
    /// It stops when Play stops, the editor quits, or scripts reload (which also ends the rest of the SDK's
    /// play session: restart Play).
    /// </summary>
    [DefaultExecutionOrder(-950)]
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    internal sealed class LocalMultiplayerHost : MonoBehaviour, ILocalHost
    {
        internal const string ScriptReloadMessage = "Local multiplayer stopped by a script reload - restart Play.";

        /// <summary>The running host, or null.</summary>
        internal static LocalMultiplayerHost Instance { get; private set; }

        // Play mode without a domain reload keeps statics; start every session clean.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            Instance = null;
        }

        readonly Dictionary<Type, object> _services = new Dictionary<Type, object>();
        readonly List<ILocalModule> _modules = new List<ILocalModule>();
        readonly CancellationTokenSource _lifetime = new CancellationTokenSource();

        BSScene _scene;
        LocalSession _session;
        LocalIdentity _identity;
        LocalMultiplayerSettings _settings;
        LocalDiagnostics _diagnostics;
        LocalUserIdentity _localUser;
        GameObject _hostObject;
        Transform _remotesRoot;
        LocalSessionPump _pump;
        int _port = RelayProtocol.DefaultPort;
        int _epochOffset;
        bool _publishingLive;
        bool _editorHooked;
        bool _tornDown;
        [NonSerialized] bool _live;

        // ---------------------------------------------------------------- ILocalHost

        public BSScene Scene => _scene;
        public ILocalSession Session => _session;
        public LocalIdentity Identity => _identity;
        public LocalMultiplayerSettings Settings => _settings;
        public ILocalDiagnostics Diagnostics => _diagnostics;
        public GameObject HostObject => _hostObject;
        public Transform RemotesRoot => _remotesRoot;
        public bool PublishingLive => _publishingLive;
        public event Action<bool> PublishingChanged;
        public int SessionEpoch => (_session != null ? _session.Epoch : 0) + _epochOffset;

        public void Provide<T>(T service) where T : class
        {
            if (service == null)
            {
                _services.Remove(typeof(T));
                return;
            }
            _services[typeof(T)] = service;
        }

        public T Get<T>() where T : class
        {
            if (!_services.TryGetValue(typeof(T), out var service)) return null;
            // A module that provided itself and was destroyed since is gone.
            if (service is UnityEngine.Object unityObject && unityObject == null) return null;
            return service as T;
        }

        // ---------------------------------------------------------------- for this assembly

        internal LocalSession LocalSession => _session;
        internal LocalUserIdentity LocalUser => _localUser;
        /// <summary>Cancelled when the host stops; async work of the modules can tie itself to it.</summary>
        internal CancellationToken Lifetime => _lifetime.Token;
        /// <summary>The web server port the page and the relay share.</summary>
        internal int Port => HubProbe.PagePort > 0 ? HubProbe.PagePort : _port;
        internal bool Live => _live;

        /// <summary>Pose and object publishing on or off (the lifecycle module decides when, as NetworkService does).</summary>
        internal void SetPublishingLive(bool live)
        {
            if (_publishingLive == live) return;
            _publishingLive = live;
            try
            {
                PublishingChanged?.Invoke(live);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        /// <summary>Builds and installs the host for <paramref name="scene"/>; null (and the SDK left offline) if it couldn't.</summary>
        internal static LocalMultiplayerHost Create(BSScene scene, MppmEnvironment env, LocalMultiplayerSettings settings)
        {
            var go = new GameObject("[LocalMultiplayer]");
            DontDestroyOnLoad(go);
            var host = go.AddComponent<LocalMultiplayerHost>();
            bool installed;
            try
            {
                installed = host.Install(scene, env, settings);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                installed = false;
            }
            if (installed) return host;
            host.Teardown(immediate: true);
            DestroyImmediate(go);
            return null;
        }

        bool Install(BSScene scene, MppmEnvironment env, LocalMultiplayerSettings settings)
        {
            _hostObject = gameObject;
            _scene = scene;
            _settings = settings ?? new LocalMultiplayerSettings();
            _identity = LocalIdentityFactory.Build(env, _settings);
            _diagnostics = new LocalDiagnostics();
            _port = ResolvePort(scene);
            Instance = this;

            // The local user exists, with its final uid, before anything in the scene can ask for "me"
            // (LocalUserService.EnsureUser runs at BanterSceneEventHandler.Start, before space content).
            UserData localUserData = null;
            var desktop = BSDesktopController.Instance;
            if (desktop != null) desktop.TryGetComponent(out localUserData);
            _localUser = new LocalUserIdentity(_identity);
            _localUser.EnsureUser(scene, localUserData);
            if (_localUser.Local == null)
            {
                Debug.LogWarning("[LocalMP] The desktop player has no UserData, so there is no local user to network; local multiplayer is off for this Play.");
                return false;
            }

            var remotes = new GameObject("Remote Players");
            remotes.transform.SetParent(transform, false);
            _remotesRoot = remotes.transform;

            _session = new LocalSession(_identity, _diagnostics, SdkVersion(), () => Port);
            _pump = gameObject.AddComponent<LocalSessionPump>();
            _pump.Session = _session;
            Provide(_localUser);
            Provide<ILocalDiagnostics>(_diagnostics);
            _live = true;

            foreach (var type in ModuleTypes())
            {
                try
                {
                    var component = gameObject.AddComponent(type);
                    if (!(component is ILocalModule module))
                    {
                        throw new InvalidOperationException($"{type.FullName} could not be added");
                    }
                    // Uninstalled with the rest even when its own Install throws half way.
                    _modules.Add(module);
                    module.Install(this);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[LocalMP] {type.Name} failed to install, so local multiplayer is off for this Play and the SDK runs as it does offline.\n{e}");
                    return false;
                }
            }

            // Last: the offline stand-ins step aside only now that everything replacing them is attached.
            BSNetworkHost.Active = true;
            LocalMultiplayerRuntime.SetCurrent(this);
            HookEditor();
            CheckRunInBackground();
            Debug.Log($"[LocalMP] Local multiplayer is on: {_identity.Slot} ({RelayProtocol.RoleWire(_identity.Role)}), uid={_identity.Uid}, " +
                      $"{_modules.Count} modules, relay on localhost:{Port}.");
            return true;
        }

        void Update()
        {
            if (!_live) return;
            _session.Tick();
        }

        /// <summary>
        /// Uninstalls the modules in reverse order, steps back from the SDK (BSNetworkHost.Active off) and
        /// closes the socket. Immediate aborts the socket instead of closing it (script reload, quit).
        /// </summary>
        internal void Teardown(bool immediate, string reason = null)
        {
            if (_tornDown) return;
            _tornDown = true;
            _live = false;
            _epochOffset++;
            try
            {
                _lifetime.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
            UnhookEditor();
            for (var i = _modules.Count - 1; i >= 0; i--)
            {
                try
                {
                    _modules[i].Uninstall();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
            _modules.Clear();
            BSNetworkHost.Active = false;
            if (ReferenceEquals(LocalMultiplayerRuntime.Current, this))
            {
                LocalMultiplayerRuntime.SetCurrent(null);
            }
            _session?.Shutdown(immediate);
            if (_pump != null) _pump.Session = null;
            _services.Clear();
            if (Instance == this) Instance = null;
            if (!string.IsNullOrEmpty(reason)) Debug.Log("[LocalMP] " + reason);
        }

        void OnApplicationQuit()
        {
            Teardown(immediate: false);
        }

        void OnDestroy()
        {
            Teardown(immediate: false);
        }

        // ---------------------------------------------------------------- editor lifetime

        void HookEditor()
        {
#if UNITY_EDITOR
            if (_editorHooked) return;
            _editorHooked = true;
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            UnityEditor.EditorApplication.quitting += OnEditorQuitting;
#endif
        }

        void UnhookEditor()
        {
#if UNITY_EDITOR
            if (!_editorHooked) return;
            _editorHooked = false;
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            UnityEditor.EditorApplication.quitting -= OnEditorQuitting;
#endif
        }

#if UNITY_EDITOR
        // Scripts recompiled during Play: every reference the modules hold is about to be lost, and
        // BSStarterUpper won't link the scene again. Take everything down cleanly now.
        void OnBeforeAssemblyReload()
        {
            Teardown(immediate: true, ScriptReloadMessage);
            if (_hostObject != null) DestroyImmediate(_hostObject);
        }

        // Stop while the browser link is still alive, so the remote users leave the page cleanly.
        void OnPlayModeStateChanged(UnityEditor.PlayModeStateChange change)
        {
            if (change == UnityEditor.PlayModeStateChange.ExitingPlayMode)
            {
                Teardown(immediate: false);
            }
        }

        void OnEditorQuitting()
        {
            Teardown(immediate: true);
        }
#endif

        // ---------------------------------------------------------------- helpers

        // The modules: every concrete MonoBehaviour in this assembly that implements ILocalModule and carries
        // [LocalModule], in ascending order (then by name, so the order never depends on reflection).
        static List<Type> ModuleTypes()
        {
            Type[] types;
            try
            {
                types = typeof(LocalMultiplayerHost).Assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                types = e.Types;
            }
            var found = new List<KeyValuePair<int, Type>>();
            foreach (var type in types)
            {
                if (type == null || type.IsAbstract || type.ContainsGenericParameters) continue;
                if (!typeof(MonoBehaviour).IsAssignableFrom(type) || !typeof(ILocalModule).IsAssignableFrom(type)) continue;
                var attribute = type.GetCustomAttribute<LocalModuleAttribute>(false);
                if (attribute == null) continue;
                found.Add(new KeyValuePair<int, Type>(attribute.Order, type));
            }
            found.Sort((a, b) => a.Key != b.Key ? a.Key.CompareTo(b.Key) : string.CompareOrdinal(a.Value.FullName, b.Value.FullName));
            var ordered = new List<Type>(found.Count);
            foreach (var pair in found) ordered.Add(pair.Value);
            return ordered;
        }

        // The port the page is served on: BSStarterUpper's OraManager sits next to the browser link.
        static int ResolvePort(BSScene scene)
        {
            var link = scene != null ? scene.link : null;
            if (link != null && link.TryGetComponent<OraManager>(out var ora) && ora.staticPort > 0)
            {
                return ora.staticPort;
            }
            return RelayProtocol.DefaultPort;
        }

        static string SdkVersion()
        {
#if UNITY_EDITOR
            try
            {
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(BSScene).Assembly);
                if (package != null && !string.IsNullOrEmpty(package.version)) return package.version;
            }
            catch (Exception)
            {
            }
#endif
            return "unknown";
        }

        // Checked, never set: in the editor it is the project's Player setting, which every clone shares.
        void CheckRunInBackground()
        {
            if (Application.runInBackground) return;
            const string message = "Run In Background is off (Project Settings > Player > Resolution and Presentation). " +
                                   "Multiplayer Play Mode players stall while their window isn't focused, so the others see them freeze. " +
                                   "Turn it on to test with several players.";
            Debug.LogWarning("[LocalMP] " + message);
            _diagnostics.Set("runInBackground", DiagnosticLevel.Warning, message);
        }
    }
}
