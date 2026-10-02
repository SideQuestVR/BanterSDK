using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using BS.Utilities.Async;
using Debug = UnityEngine.Debug;
using System.Collections;
using SideQuest.Ora;
using SideQuest.Ora.WebRTC;
using Unity.VisualScripting;

namespace BS
{
    [DefaultExecutionOrder(-1001)]
    public class BSStarterUpper : MonoBehaviour
    {
        public bool openBrowser;
        [SerializeField] Transform _feetTransform;
        public static bool SafeMode = false;
        public static float voiceVolume = 0;
        private object process;
        public BSScene scene;
        public static string WEB_ROOT = "WebRoot";
        public static int mainWWindowId;
        public static int mainWWindowPort = -2;
        private int processId;
        private static bool initialized = false;
        private Coroutine currentCoroutine;
        private OraManager oraManager;

        private const string BANTER_DEVTOOLS_ENABLED = "BANTER_DEVTOOLS_ENABLED";
        private const string BANTER_AUTOSTART_DISABLED = "BANTER_AUTOSTART_DISABLED";

        // Editor-only convenience toggle: when on, skips the desktop controller (fly camera, mouse
        // grab/click, local user) and the HardwareKeyboardInput setup that spams the console when
        // Active Input Handling is set to the new Input System. Never matters outside the Editor
        // (always false in a build).
        public static bool AutoStartDisabled
        {
            get
            {
#if UNITY_EDITOR
                return UnityEditor.EditorPrefs.GetBool(BANTER_AUTOSTART_DISABLED, false);
#else
                return false;
#endif
            }
        }

#if UNITY_EDITOR
        public static void ToggleAutoStart()
        {
            bool newValue = !UnityEditor.EditorPrefs.GetBool(BANTER_AUTOSTART_DISABLED, false);
            UnityEditor.EditorPrefs.SetBool(BANTER_AUTOSTART_DISABLED, newValue);
            LogLine.Do("Banter desktop controller (camera, mouse grab/click, hardware keyboard input) " + (newValue ? "disabled." : "enabled."));
        }
#endif

        void Awake()
        {
            // Safe mode?
            if (PlayerPrefs.HasKey("SafeModeOff"))
            {
                PlayerPrefs.DeleteKey("SafeMode");
                PlayerPrefs.DeleteKey("SafeModeOff");
            }
            else if (PlayerPrefs.HasKey("SafeMode"))
            {
                SafeMode = true;
                LogLine.Do("SAFE MODE is set on");
                PlayerPrefs.SetInt("SafeModeOff", 1);
            }

            BasisLoadHandler.IsInitialized = false;
            _ = BasisLoadHandler.EnsureInitializationComplete();

            if (!initialized)
            {
                UnityGame.SetMainThread();
                var unitySched = UnityMainThreadTaskScheduler.Default as UnityMainThreadTaskScheduler;
                unitySched.SetMonoBehaviour(this);
                if (!unitySched.IsRunning)
                {
                    currentCoroutine = StartCoroutine(unitySched.Coroutine());
                }
                initialized = true;
            }

            scene = BSScene.Instance();
            gameObject.AddComponent<DontDestroyOnLoad>();

#if !GREENFIELD_PROJECT
            SetupExtraEvents();
            if (!AutoStartDisabled)
            {
#if !BANTER_FLEX
                BSDesktopController.Spawn(scene);
#else
                LogLine.Do("FlexaBody is installed, so the SDK desktop controller is not spawned.");
#endif
            }
            StartCoroutine(OpenPageDev());
#endif
#if UNITY_EDITOR
            CreateWebRoot();
#endif
            var oraManager = gameObject.GetComponent<OraManager>();
            if (!oraManager)
            {
                oraManager = gameObject.AddComponent<OraManager>();
            }
<<<<<<< HEAD
=======
            // Ora's audio is now self-contained (OraAudioView / OraAudioEmitter, per-view + static) — the old
            // OraAudioManager component was removed, so there's nothing to wire up here anymore.
>>>>>>> c114b0442175ca9f92ab5c10d3f57f87006465ca
            oraManager.oraWebRTCManager = gameObject.GetComponent<OraWebRTCManager>();
            if (!oraManager.oraWebRTCManager)
            {
                oraManager.oraWebRTCManager = gameObject.AddComponent<OraWebRTCManager>();
            }
            if (!AutoStartDisabled)
            {
                oraManager.hardwareKeyboardInput = gameObject.GetComponent<HardwareKeyboardInput>();
                if (!oraManager.hardwareKeyboardInput)
                {
                    oraManager.hardwareKeyboardInput = gameObject.AddComponent<HardwareKeyboardInput>();
                }
                oraManager.SubscribeHardwareKeyboard();
            }
            // The prefab carries no Ora components: Ora ships both as a DLL and as source, and a
            // serialized reference to one form is a missing script under the other. So the space
            // view is configured here, with what the prefab used to serialize. OraView registers its
            // window (and reads its injection) in Awake, so it is built on an inactive child and
            // configured before that Awake runs.
            var oraView = gameObject.GetComponent<OraView>();
            if (!oraView)
            {
                var viewGo = new GameObject("SpaceView");
                viewGo.SetActive(false);
                viewGo.transform.SetParent(transform, false);
                oraView = viewGo.AddComponent<OraView>();
                oraView.customInjectedJavascript = Resources.Load<TextAsset>("injection");
                oraView.injectShims = true;
                viewGo.SetActive(true);
            }

            oraView.openBrowser = openBrowser;
            this.oraManager = oraManager;
            SetupBrowserLink(oraView, oraManager);
            // The world browser's texture: visual scripting's On World Browser Texture, and the
            // asset_browser_world reference for materials.
            oraView.textureChanged.AddListener(scene.link.OnWorldBrowserTexture);
            if (oraView.texture2D != null)
                scene.link.OnWorldBrowserTexture(oraView.texture2D);

#if GREENFIELD_PROJECT
            // Hand our feet reference to the loading cage — but never clobber a reference
            // the cage already has with null when ours isn't wired up in the scene.
            if (scene.loadingManager != null && _feetTransform != null)
                scene.loadingManager.feetTransform = _feetTransform;
#endif
            scene.ResetLoadingProgress();
#if !GREENFIELD_PROJECT
            BSNetworkHost.RaiseSceneLinked(scene);
#endif
        }

        IEnumerator OpenPageDev()
        {
            // Started from Awake before oraManager is assigned; let Awake finish first.
            yield return null;
            var port = oraManager != null ? oraManager.staticPort : 42068;
            var gate = BSNetworkHost.PageLoadGate;
            if (gate != null)
            {
                yield return gate(scene.link.pipe.view, port);
            }
            else
            {
                yield return new WaitForSeconds(2);
            }
            // Ora's web server serves Assets/WebRoot on this port (see BeforeEditorPlay).
            scene.link.pipe.view.LoadUrl("http://localhost:" + port);
        }

        // Teleports are handled by BSDesktopController, which owns the local user.
        void SetupExtraEvents()
        {
            // Argument order the OnSpaceStatePropsChanged node reads: (value, isPublic). A network
            // host raises it from the room's echo instead, so it isn't raised twice.
            scene.events.OnPublicSpaceStateChanged.AddListener((key, value) =>
            {
                if (BSNetworkHost.Active) return;
                EventBus.Trigger("OnSpaceStatePropsChanged", new CustomEventArgs(key, new object[] { value, true }));
            });
            scene.events.OnProtectedSpaceStateChanged.AddListener((key, value) =>
            {
                if (BSNetworkHost.Active) return;
                EventBus.Trigger("OnSpaceStatePropsChanged", new CustomEventArgs(key, new object[] { value, false }));
            });
        }

        private void OnApplicationQuit()
        {
            // Kill(true);
        }

        void OnDestroy()
        {
            scene.state = SceneState.NONE;
            scene.Destroy();
            try
            {
                var unitySched = UnityMainThreadTaskScheduler.Default;
                unitySched.Cancel();
#if UNITY_EDITOR
                initialized = false;
                if (currentCoroutine != null)
                    StopCoroutine(currentCoroutine);
#endif
            }
            catch (Exception e)
            {
            }
        }
        private void SetupBrowserLink(OraView view, OraManager manager)
        {
            scene.link = gameObject.AddComponent<BSLink>();
            scene.link.SetupPipe(view, manager);
            scene.link.Connected += (arg0, arg1) => UnityMainThreadTaskScheduler.Default.Enqueue(TaskRunner.Track(() => scene.LoadSpaceState(), $"{nameof(BSStarterUpper)}.{nameof(SetupBrowserLink)}"));
        }
        public void CancelLoading()
        {
            if (scene.HasLoadFailed())
            {
                scene.LoadingStatus = "Couldn't load home space, loading fallback...";
                UnityMainThreadTaskScheduler.Default.Enqueue(TaskRunner.Track(() => scene.events.OnLoadUrl.Invoke(BSScene.ORIGINAL_HOME_SPACE), $"{nameof(BSStarterUpper)}.{nameof(CancelLoading)}.Failed"));
            }
            else
            {
                // Allow cancelling and going back to lobby, only if loading
                if (scene.loading)
                {
                    scene.LoadingStatus = "Loading canceled, falling back to lobby";
                    LogLine.Do("Taking you to your home...");
                    scene.Cancel("User cancelled loading", true);
                    UnityMainThreadTaskScheduler.Default.Enqueue(TaskRunner.Track(() => scene.events.OnLoadUrl.Invoke(BSScene.ORIGINAL_HOME_SPACE), $"{nameof(BSStarterUpper)}.{nameof(CancelLoading)}.LoadingCanceled"));
                }

                // The below allows canceling from outside loading screen
                // if (!(scene.loading && scene.CurrentUrl == BSScene.CUSTOM_HOME_SPACE))
                // {
                //     scene.LoadingStatus = "Taking you to your home...";
                //     LogLine.Do("Taking you to your home...");
                //     scene.Cancel("User cancelled loading", true);
                //     UnityMainThreadTaskScheduler.Default.QueueAction(() => scene.events.OnLoadUrl.Invoke(BSScene.CUSTOM_HOME_SPACE));
                // }
            }
        }

        private static bool _devToolsEnabled = false;
        public static void ToggleDevTools()
        {
#if UNITY_EDITOR
            _devToolsEnabled = UnityEditor.EditorPrefs.GetBool(BANTER_DEVTOOLS_ENABLED, false);
            _devToolsEnabled = !_devToolsEnabled;
            UnityEditor.EditorPrefs.SetBool(BANTER_DEVTOOLS_ENABLED, _devToolsEnabled);

            LogLine.Do($"Banter DevTools " + (_devToolsEnabled ? "enabled." : "disabled."));
#else
            _devToolsEnabled = ! _devToolsEnabled;
#endif
            if (Application.isPlaying)
            {
                BSScene.Instance().link.ToggleDevTools(_devToolsEnabled);
            }
        }

        private void Kill(bool force = false)
        {
            if (processId > 0)
            {
                try
                {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR && GREENFIELD_PROJECT
                    var processes = KS.Diagnostics.Process.GetProcessesByName("banter-link");
                    process = processes.FirstOrDefault(p=>p.Id==processId);
                    if (process!=null)
                    {  
                        ((KS.Diagnostics.Process)process).Kill();  
                    }
#else
                    process = Process.GetProcessById(processId);
                    ((Process)process).Kill();
#endif
                }
                catch (InvalidOperationException)
                {
                    LogLine.Do(Color.red, LogTag.Banter,
                        "The process was already dead when we tried to kill it, I guess that's fine? Let's make sure it actualy died though.");
                }
            }

            if (force)
            {
                //_ = KillBanterLink();
            }
        }
        async Task KillBanterLink()
        {
            await new WaitForSeconds(0.1f);
            var processes = Process.GetProcessesByName("banter-link");
            var killedLogs = "";
            var failedLogs = "";
            if (processes.Length > 0)
            {
                killedLogs += "Killed banter-link processes: ";
                failedLogs += "Failed to kill: ";
            }
            foreach (var p in processes)
            {
                try
                {
                    p.Kill();
                    killedLogs += p.Id + ", ";
                }
                catch (InvalidOperationException)
                {
                    failedLogs += p.Id + ", ";
                }
            }
            LogLine.Do(LogLine.browserColor, LogTag.Banter, killedLogs + (failedLogs == "Failed to kill: " ? failedLogs + "none." : failedLogs));
        }

        void CreateWebRoot()
        {
            // TODO: Add more into the boilerplate like examples, meta tags for stuff thats global, etc
#if !GREENFIELD_PROJECT
            var webRoot = Application.dataPath + "/WebRoot";
            if (Directory.Exists(webRoot))
                return;
            Directory.CreateDirectory(webRoot);
            File.WriteAllText(webRoot + "/index.html", "<html android-bundle windows-bundle><head>");
#endif
        }

        void FixedUpdate()
        {
            scene.FixedUpdate();
        }

#if UNITY_EDITOR && !GREENFIELD_PROJECT
        private const string SPAWN_ON_PLAY = "BS_SPAWN_STARTER_UPPER_ON_PLAY";

        // Set by the editor when leaving edit mode with no BSStarterUpper in the scene. Kept in
        // SessionState because entering play mode reloads the domain, which resets statics.
        public static bool SpawnOnPlay
        {
            get => UnityEditor.SessionState.GetBool(SPAWN_ON_PLAY, false);
            set => UnityEditor.SessionState.SetBool(SPAWN_ON_PLAY, value);
        }

        // Before any OraManager.Awake launches Ora: have its web server serve Assets/WebRoot, the page
        // OpenPageDev loads, unless the scene's OraManager names its own folder. Then add the
        // BSStarterUpper the scene lacks. It's made in play mode, so it goes when play stops.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void BeforeEditorPlay()
        {
            OraManager.defaultStaticFolder = Application.dataPath + "/" + WEB_ROOT;
            if (!SpawnOnPlay)
            {
                return;
            }
            SpawnOnPlay = false;
            Debug.LogWarning("BSStarterUpper not found, adding one.");
            Instantiate(Resources.Load<GameObject>("Prefabs/BSStarterUpper"));
        }
#endif

        [RuntimeInitializeOnLoadMethod]
        private static void OnLoad()
        {
            AppDomain.CurrentDomain.UnhandledException +=
                (object sender, UnhandledExceptionEventArgs args) =>
                    Debug.LogError("[AppDomain.CurrentDomain.UnhandledException]: " + (Exception)args.ExceptionObject);
                    TaskScheduler.UnobservedTaskException +=
                (object sender, UnobservedTaskExceptionEventArgs args) =>
                    {
                        args.SetObserved();

                        // Teardown noise: aborted overlapped IO (Win32 995 from killed process
                        // pipes / closed handles), cancelled or disposed background reads, and
                        // connects abandoned after a cancel (Unity's AI Assistant times out its
                        // relay WebSocket connect that way). These surface via the finalizer long
                        // after shutdown started — not actionable.
                        bool benignTeardown = true;
                        foreach (var inner in args.Exception.Flatten().InnerExceptions)
                        {
                            if (!IsTeardownNoise(inner))
                            {
                                benignTeardown = false;
                                break;
                            }
                        }

                        if (benignTeardown)
                            return;

                        Debug.LogError("[TaskScheduler.UnobservedTaskException]: " + args.Exception);
                    };
        }

        // Cancelled, disposed or aborted IO is noise; a transport wrapper (a WebSocket or HTTP failure) says
        // nothing by itself, so what caused it decides. Wrappers are matched by name: no assembly references needed.
        internal static bool IsTeardownNoise(Exception exception)
        {
            for (var e = exception; e != null; e = e.InnerException)
            {
                if (e is System.IO.IOException
                    or System.OperationCanceledException
                    or System.ObjectDisposedException
                    or System.Threading.ThreadAbortException)
                    return true;
                var name = e.GetType().FullName;
                if (name != "System.Net.WebSockets.WebSocketException" && name != "System.Net.Http.HttpRequestException")
                    return false;
            }
            return false;
        }
    }
}
