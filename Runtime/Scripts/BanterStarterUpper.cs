using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SpatialTracking;
using Banter.Utilities.Async;
using Debug = UnityEngine.Debug;
using TLab.WebView;
using UnityEngine.UI;



#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
#endif

namespace Banter.SDK
{
    [DefaultExecutionOrder(-1001)]
    public class BanterStarterUpper : MonoBehaviour
    {
        [SerializeField] int numberOfRemotePlayers = 1;
        [SerializeField] Vector3 spawnPoint;
        [SerializeField] float spawnRotation;
        [SerializeField] bool openBrowser;
        [SerializeField] Transform _feetTransform;
        [SerializeField] RawImage _browserRenderer;
        public static bool SafeMode = false;
        public static float voiceVolume = 0;
        private GameObject localPlayerPrefab;
        private object process;
        public BanterScene scene;
        public static string WEB_ROOT = "WebRoot";
        public static int mainWWindowId;
        public static int mainWWindowPort = -2;
        private int processId;
        private static bool initialized = false;
        private Coroutine currentCoroutine;

        private const string BANTER_DEVTOOLS_ENABLED = "BANTER_DEVTOOLS_ENABLED";
        
        public static Browser browser;
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
            
#if BASIS_BUNDLE_MANAGEMENT
            BasisLoadHandler.IsInitialized = false;
            BasisLoadHandler.OnGameStart();
#endif
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

            scene = BanterScene.Instance();
            gameObject.AddComponent<DontDestroyOnLoad>();
#if !BANTER_EDITOR
            localPlayerPrefab = Resources.Load<GameObject>("Prefabs/BanterPlayer");
            SetupExtraEvents();
            SetupCamera();
            SpawnPlayers();
#endif
#if UNITY_EDITOR
            CreateWebRoot();
#endif
#if UNITY_STANDALONE || UNITY_EDITOR
            Kill();
            StartElectronBrowser();
#else
            StartBrowserWindow();
#endif
            SetupBrowserLink();
#if UNITY_STANDALONE || UNITY_EDITOR
    EventHandler args = null;
    args = (arg0, arg1) =>
    {
        scene.link.Connected -= args;
        UnityMainThreadTaskScheduler.Default.Enqueue(() =>
        {
            StartBrowserWindow();
        });
    };
    scene.link.Connected += args;
#endif

#if BANTER_EDITOR
            scene.loadingManager.feetTransform = _feetTransform;
#endif
            scene.ResetLoadingProgress();
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
#if !BANTER_EDITOR
            localUserData.nameTag = player.GetComponentInChildren<TMPro.TextMeshPro>();
#endif
            player.transform.position = spawnPoint;
            player.transform.eulerAngles = new Vector3(0, spawnRotation, 0);
        }

        void SetupExtraEvents()
        {
            scene.events.OnTeleport.AddListener((position, rotation, _, _) =>
            {
                var player = BanterScene.Instance().users.First(user => user.isLocal);
                player.transform.position = position;
                player.transform.eulerAngles = rotation;
            });
            scene.events.OnPublicSpaceStateChanged.AddListener((key, value) =>
            {
#if BANTER_VISUAL_SCRIPTING
                EventBus.Trigger("OnSpaceStatePropsChanged", new CustomEventArgs(key, new object[] { value, false }));
#endif
            });
            scene.events.OnProtectedSpaceStateChanged.AddListener((key, value) =>
            {
#if BANTER_VISUAL_SCRIPTING
                EventBus.Trigger("OnSpaceStatePropsChanged", new CustomEventArgs(key, new object[] { value, true }));
#endif
            });
        }

        private void OnApplicationQuit()
        {
            Kill(true);
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
        private void SetupBrowserLink()
        {
            scene.link = gameObject.AddComponent<BanterLink>();
            scene.link.Connected += (arg0, arg1) => UnityMainThreadTaskScheduler.Default.Enqueue(TaskRunner.Track(() => scene.LoadSpaceState(), $"{nameof(BanterStarterUpper)}.{nameof(SetupBrowserLink)}"));
        }
        public void CancelLoading()
        {
            if (scene.HasLoadFailed())
            {
                scene.LoadingStatus = "Couldn't load home space, loading fallback...";
                UnityMainThreadTaskScheduler.Default.Enqueue(TaskRunner.Track(() => scene.events.OnLoadUrl.Invoke(BanterScene.ORIGINAL_HOME_SPACE), $"{nameof(BanterStarterUpper)}.{nameof(CancelLoading)}.Failed"));
            }
            else
            {
                // Allow cancelling and going back to lobby, only if loading
                if (scene.loading)
                {
                    scene.LoadingStatus = "Loading canceled, falling back to lobby";
                    LogLine.Do("Taking you to your home...");
                    scene.Cancel("User cancelled loading", true);
                    UnityMainThreadTaskScheduler.Default.Enqueue(TaskRunner.Track(() => scene.events.OnLoadUrl.Invoke(BanterScene.ORIGINAL_HOME_SPACE), $"{nameof(BanterStarterUpper)}.{nameof(CancelLoading)}.LoadingCanceled"));
                }

                // The below allows canceling from outside loading screen
                // if (!(scene.loading && scene.CurrentUrl == BanterScene.CUSTOM_HOME_SPACE))
                // {
                //     scene.LoadingStatus = "Taking you to your home...";
                //     LogLine.Do("Taking you to your home...");
                //     scene.Cancel("User cancelled loading", true);
                //     UnityMainThreadTaskScheduler.Default.QueueAction(() => scene.events.OnLoadUrl.Invoke(BanterScene.CUSTOM_HOME_SPACE));
                // }
            }
        }

        public static void ToggleDevTools()
        {
#if UNITY_EDITOR
            var devToolsEnabled = UnityEditor.EditorPrefs.GetBool(BANTER_DEVTOOLS_ENABLED, false);
            devToolsEnabled = !devToolsEnabled;
            UnityEditor.EditorPrefs.SetBool(BANTER_DEVTOOLS_ENABLED, devToolsEnabled);

            LogLine.Do($"Banter DevTools " + (devToolsEnabled ? "enabled." : "disabled."));
#endif
            if (Application.isPlaying)
            {
                BanterScene.Instance().link.ToggleDevTools();
            }
        }

        private void StartElectronBrowser()
        {
            Kill();
#if !BANTER_EDITOR
            var isProd = false;
#else
            var isProd = true;
#endif

#if UNITY_EDITOR
            var Eargs = (isProd ? "--prod true " : "") + "--bebug --pipename " +
                BanterLink.pipeName + " --root " + "\"" + Path.Join(Application.dataPath, WEB_ROOT) + "\"";
            processId = StartProcess.Do(LogLine.browserColor, Path.GetFullPath("Packages\\com.sidequest.banter\\Editor\\banter-link"),
                Path.GetFullPath("Packages\\com.sidequest.banter\\Editor\\banter-link\\banter-link.exe"),
                Eargs,
                LogTag.BanterBrowser);
#else
            processId = StartProcess.Do(LogLine.browserColor, Directory.GetCurrentDirectory() + "\\banter-link",
                Directory.GetCurrentDirectory() + "\\banter-link\\banter-link.exe",
                "--bebug --prod true --pipename " + BanterLink.pipeName,
                LogTag.BanterBrowser);
#endif
                   
        }
        public static int spaceBrowserWidth = 1024;
        public static int spaceBrowserHeight = 768;

        Vector2Int lastSize = new Vector2Int(spaceBrowserWidth, spaceBrowserHeight);
        public static Texture2D browserTexture;
        void StartBrowserWindow()
        {
#if UNITY_EDITOR
            var injectFile = Path.GetFullPath("Packages\\com.sidequest.banter\\Editor\\banter-link\\inject.js");
#else
            var injectFile = Path.Combine(Directory.GetCurrentDirectory(), "banter-link", "inject.js");
#endif
           
#if UNITY_ANDROID
            browser = gameObject.AddComponent<WebView>();
#else
            browser = gameObject.AddComponent<ElectronView>();
#endif
            browser.texSize = new Vector2Int(spaceBrowserWidth, spaceBrowserHeight);
            browser.viewSize = new Vector2Int(spaceBrowserWidth, spaceBrowserHeight);
            browser.fps = 30;
            browser.preloadScript = injectFile;
            browser.captureMode = CaptureMode.HardwareBuffer;
            var downloadOption = new Download.Option();
            downloadOption.Update(Download.Directory.Download, "BanterLink");
            browser.downloadOption = downloadOption;
            browser.onCapture.AddListener((tex) =>
            {
                mainWWindowId = browser.winId;
                browserTexture = tex;
                if (_browserRenderer != null)
                {
                    _browserRenderer.texture = browserTexture;
                    var inputlistener = _browserRenderer.gameObject.GetComponent<BrowserInputListener>();
                    if( inputlistener)
                        inputlistener.browser = browser;
                    
                }
#if BANTER_VISUAL_SCRIPTING
                EventBus.Trigger("OnSpaceBrowserTexture", new CustomEventArgs(null, new object[] { tex }));
#endif
            });
            browser.Init();
        }

        public static async Task SetMainWindowPort(Action<int> portCallback)
        {
            if (browser == null)
            {
                await new WaitUntil(() => browser != null);
            }
            browser.onNativeReady.AddListener(() => {
                mainWWindowPort = browser.StartSocketServer();
                portCallback?.Invoke(mainWWindowPort);
            });
        }

        private void Kill(bool force = false)
        {
            if (processId > 0)
            {
                try
                {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR && BANTER_EDITOR
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
#if !BANTER_EDITOR
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

        void Update()
        {
            if (spaceBrowserWidth != lastSize.x || spaceBrowserHeight != lastSize.y)
            {
                lastSize = new Vector2Int(spaceBrowserWidth, spaceBrowserHeight);
                browser.Resize(lastSize, lastSize);
            }
            browser?.UpdateFrame();
            browser?.DispatchMessageQueue();
            FragmentCapture.GarbageCollect();
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
                        Debug.LogError("[TaskScheduler.UnobservedTaskException]: " + args.Exception);
                        args.SetObserved();
                    };
        }
    }
}
