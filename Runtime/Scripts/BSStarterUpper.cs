using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SpatialTracking;
using BS.Utilities.Async;
using Debug = UnityEngine.Debug;
using UnityEngine.UI;
using System.Collections;
using SideQuest.Ora;
using SideQuest.Ora.WebRTC;
using Unity.VisualScripting;

namespace BS
{
    [DefaultExecutionOrder(-1001)]
    public class BSStarterUpper : MonoBehaviour
    {
        [SerializeField] int numberOfRemotePlayers = 1;
        [SerializeField] Vector3 spawnPoint;
        [SerializeField] float spawnRotation;
        public bool openBrowser;
        [SerializeField] Transform _feetTransform;
        [SerializeField] RawImage _browserRenderer;
        public static bool SafeMode = false;
        public static float voiceVolume = 0;
        private GameObject localPlayerPrefab;
        private object process;
        public BSScene scene;
        public static string WEB_ROOT = "WebRoot";
        public static int mainWWindowId;
        public static int mainWWindowPort = -2;
        private int processId;
        private static bool initialized = false;
        private Coroutine currentCoroutine;

        private const string BANTER_DEVTOOLS_ENABLED = "BANTER_DEVTOOLS_ENABLED";
        private const string BANTER_AUTOSTART_DISABLED = "BANTER_AUTOSTART_DISABLED";

        // Editor-only convenience toggle: when on, skips local/remote player spawning and the
        // HardwareKeyboardInput setup that spams the console when Active Input Handling is set
        // to the new Input System. Never matters outside the Editor (always false in a build).
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
            LogLine.Do("Banter auto-start (local/remote players, hardware keyboard input) " + (newValue ? "disabled." : "enabled."));
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
            if (!AutoStartDisabled)
            {
                localPlayerPrefab = Resources.Load<GameObject>("Prefabs/BanterPlayer");
                SetupExtraEvents();
                SetupCamera();
                SpawnPlayers();
                StartCoroutine(OpenPageDev());
            }
#endif
#if UNITY_EDITOR
            CreateWebRoot();
#endif
            var oraManager = gameObject.GetComponent<OraManager>();
            if (!oraManager)
            {
                oraManager = gameObject.AddComponent<OraManager>();
            }
            oraManager.oraAudioManager = gameObject.GetComponent<OraAudioManager>();
            if (!oraManager.oraAudioManager)
            {
                oraManager.oraAudioManager = gameObject.AddComponent<OraAudioManager>();
            }
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
            var oraView = gameObject.GetComponent<OraView>();
            if (!oraView)
            {
                oraView = gameObject.AddComponent<OraView>();
                oraView.customInjectedJavascript = Resources.Load<TextAsset>("injection");
            }

            oraView.openBrowser = openBrowser;
            SetupBrowserLink(oraView, oraManager);

#if GREENFIELD_PROJECT
            // Hand our feet reference to the loading cage — but never clobber a reference
            // the cage already has with null when ours isn't wired up in the scene.
            if (scene.loadingManager != null && _feetTransform != null)
                scene.loadingManager.feetTransform = _feetTransform;
#endif
            scene.ResetLoadingProgress();
        }
        
        IEnumerator OpenPageDev()
        {
            yield return new WaitForSeconds(2);
            scene.link.pipe.view.LoadUrl("http://localhost:42068");
        }

        Vector3 RandomSpawnPoint()
        {
            return new Vector3(UnityEngine.Random.Range(-0.5f, 0.5f), 0, UnityEngine.Random.Range(-0.5f, 0.5f)) + spawnPoint;
        }

        void SpawnPlayers()
        {
            var spawn = Resources.Load<GameObject>("Prefabs/BanterSpawnPoint");
            if (spawn != null)
            {
                var spawnGo = Instantiate(spawn).transform;
                spawnGo.name = "SpawnPoint";
                spawnGo.position = spawnPoint;
                spawnGo.eulerAngles = new Vector3(0, spawnRotation, 0);
            }
            for (int i = 0; i < numberOfRemotePlayers; i++)
            {
                var player = Instantiate(localPlayerPrefab).transform;
                player.name = "RemotePlayer" + i;
                player.position = RandomSpawnPoint();
                player.eulerAngles = new Vector3(0, spawnRotation, 0);
                GameObject.Destroy(player.Find("TrackedLeftHand").gameObject);
                GameObject.Destroy(player.Find("TrackedRightHand").gameObject);
                GameObject.Destroy(player.Find("Head").GetComponent<TrackedPoseDriver>());
                GameObject.Destroy(player.Find("Head").GetComponent<Camera>());
                GameObject.Destroy(player.Find("Head").GetComponent<AudioListener>());
                GameObject.Destroy(player.GetComponent<PlayerEmulator>());
                player.Find("LeftHand").GetComponent<Rigidbody>().isKinematic = true;
                player.Find("RightHand").GetComponent<Rigidbody>().isKinematic = true;
                GameObject.Destroy(player.Find("RightHand").GetComponent<HandGrabber>());
                GameObject.Destroy(player.Find("LeftHand").GetComponent<HandGrabber>());
                GameObject.Destroy(player.Find("RightHand").GetComponent<PhysicsHandFollow>());
                GameObject.Destroy(player.Find("LeftHand").GetComponent<PhysicsHandFollow>());
            }
        }

        void SetupCamera()
        {
            var player = Instantiate(localPlayerPrefab).transform;
            player.name = "LocalPlayer";
            player.Find("RightHand").transform.SetParent(null);
            player.Find("LeftHand").transform.SetParent(null);

            var localUserData = player.GetComponent<UserData>();
            localUserData.isLocal = true;
#if !GREENFIELD_PROJECT
            localUserData.nameTag = player.GetComponentInChildren<TMPro.TextMeshPro>();
#endif
            player.transform.position = spawnPoint;
            player.transform.eulerAngles = new Vector3(0, spawnRotation, 0);
        }

        void SetupExtraEvents()
        {
            scene.events.OnTeleport.AddListener((position, rotation, _, _) =>
            {
                var player = BSScene.Instance().users.First(user => user.isLocal);
                player.transform.position = position;
                player.transform.eulerAngles = rotation;
            });
            scene.events.OnPublicSpaceStateChanged.AddListener((key, value) =>
            {
                EventBus.Trigger("OnSpaceStatePropsChanged", new CustomEventArgs(key, new object[] { value, false }));
            });
            scene.events.OnProtectedSpaceStateChanged.AddListener((key, value) =>
            {
                EventBus.Trigger("OnSpaceStatePropsChanged", new CustomEventArgs(key, new object[] { value, true }));
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
                        // pipes / closed handles), cancelled or disposed background reads. These
                        // surface via the finalizer long after shutdown started — not actionable.
                        bool benignTeardown = true;
                        foreach (var inner in args.Exception.Flatten().InnerExceptions)
                        {
                            if (inner is not (System.IO.IOException
                                or System.OperationCanceledException
                                or System.ObjectDisposedException
                                or System.Threading.ThreadAbortException))
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
    }
}
