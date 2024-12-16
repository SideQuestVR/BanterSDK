using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SpatialTracking;
using Banter.Utilities.Async;
using Banter.Utilities;
using Debug = UnityEngine.Debug;

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
        [SerializeField] Transform _feetTransform;
        public static float voiceVolume = 0;
        private GameObject localPlayerPrefab;
        private object process;
        public BanterScene scene;
        public static string WEB_ROOT = "WebRoot";
        private int processId;
        private static bool initialized = false;
        private Coroutine currentCoroutine;

        private const string BANTER_DEVTOOLS_ENABLED = "BANTER_DEVTOOLS_ENABLED";

        void Awake()
        {
            //if (!initialized)
            //{
                UnityGame.SetMainThread();
                var unitySched = UnityMainThreadTaskScheduler.Default as UnityMainThreadTaskScheduler;
                unitySched.SetMonoBehaviour(this);
                if (!unitySched.IsRunning)
                {
                    currentCoroutine = StartCoroutine(unitySched.Coroutine());
                }
                initialized = true;
            //}
            
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
            StartBrowser();
#endif
            SetupBrowserLink();
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
                if(currentCoroutine!=null)
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
            scene.link.Connected += (arg0, arg1) => UnityMainThreadTaskScheduler.Default.Enqueue(() => scene.LoadSpaceState());
        }
        public void CancelLoading()
        {
            if (scene.HasLoadFailed())
            {
                scene.LoadingStatus = "Couldn't load home space, loading fallback...";
                UnityMainThreadTaskScheduler.Default.Enqueue(() => scene.events.OnLoadUrl.Invoke(BanterScene.ORIGINAL_HOME_SPACE));
            }
            else
            {
                // Allow cancelling and going back to lobby, only if loading
                if (scene.loading)
                {
                    scene.LoadingStatus = "Loading canceled, falling back to lobby";
                    LogLine.Do("Taking you to your home...");
                    scene.Cancel("User cancelled loading", true);
                    UnityMainThreadTaskScheduler.Default.Enqueue(() => scene.events.OnLoadUrl.Invoke(BanterScene.ORIGINAL_HOME_SPACE));
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

            LogLine.Do($"Banter DevTools " + (devToolsEnabled?"enabled.":"disabled.") );
#endif
            if (Application.isPlaying)
            {
                BanterScene.Instance().link.ToggleDevTools();
            }
        }

        private void StartBrowser()
        {
#if !BANTER_EDITOR
            var isProd = false;
#else
            var isProd = true;
#endif
#if UNITY_EDITOR
            var injectFile = "\"" + Path.GetFullPath("Packages\\com.sidequest.banter\\Editor\\banter-link\\inject.txt")+"\"";
            processId = StartProcess.Do(LogLine.browserColor, Path.GetFullPath("Packages\\com.sidequest.banter\\Editor\\banter-link"), 
                Path.GetFullPath("Packages\\com.sidequest.banter\\Editor\\banter-link\\banter-link.exe"),
                (isProd ? "--prod true " : "") + "--bebug" + (UnityEditor.EditorPrefs.GetBool(BANTER_DEVTOOLS_ENABLED, false) ? " --devtools" : "") + " --pipename " + BanterLink.pipeName + " --inject " + injectFile + " --root " + "\"" + Path.Join(Application.dataPath, WEB_ROOT) + "\"",
                LogTag.BanterBrowser);
#else
            var injectFile = "\"" + Path.Combine(Directory.GetCurrentDirectory(), "banter-link", "inject.txt") + "\"";
            processId = StartProcess.Do(LogLine.browserColor, Directory.GetCurrentDirectory() + "\\banter-link",
                Directory.GetCurrentDirectory() + "\\banter-link\\banter-link.exe",
                "--bebug --prod true --pipename " + BanterLink.pipeName + " --inject " + injectFile,
                LogTag.BanterBrowser);
#endif
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
            await Task.Delay(100);
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
    }
}
