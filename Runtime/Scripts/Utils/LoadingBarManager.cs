using System.IO;
using System.Threading.Tasks;
using DigitalRuby.Tween;
using UnityEngine;
using UnityEngine.Events;
using BS.Utilities.Async;

namespace BS
{

    enum LoadingState
    {
        Loading,
        Loaded,
        Unloaded
    }
    public class LoadingBarManager : MonoBehaviour
    {
        float loadingpercentage;
        [SerializeField] Texture2D defaultLoadingImage;
        [SerializeField] Renderer loadingProgress;
        [SerializeField] Texture2D[] maskTextures;
        [SerializeField] Transform loadingBarInner;
        [SerializeField] GameObject loadingBar;
        [SerializeField] AnimationClip loadIn;
        [SerializeField] AnimationClip loadOut;
        [SerializeField] Animation loadingSphere;
        [SerializeField] GameObject teleportWall;
        public TMPro.TextMeshPro titleText;
        public TMPro.TextMeshPro cancelText;
        public TMPro.TextMeshPro loadingText;
        public UnityEvent onCancel = new UnityEvent();
        public UnityEvent onDone = new UnityEvent();
        [Tooltip("Fires when the cage begins its load-out (opening) transition. Greenfield uses " +
                 "this to unfreeze player physics as the cage dissolves, so any settle is hidden.")]
        public UnityEvent onLoadOutStarted = new UnityEvent();
        public GameObject loadFailed;
        public Transform spinner;
        BSScene scene;
        public float speed = 1f;
        private bool CanCancel = true;
        private float lastLoadingPercent = 0f;
        private string currentUrl = "";
        private LoadingState state = LoadingState.Unloaded;
        void Update()
        {
            if (!spinner)
            {
                return;
            }
            spinner.Rotate(0, 0, -3 * speed);
        }
        // All script writes go through the renderer's INSTANCED material, never sharedMaterial:
        // sharedMaterial writes mutate the .mat asset in the editor, which is why the loading
        // cage materials kept showing up as modified after every run (a Banter classic). A
        // MaterialPropertyBlock is NOT an option here — the LoadIn/LoadOut/first-load clips
        // animate material._DissolveAmount/._Tint on this same renderer, and block values
        // would override (freeze) the animated ones. The animation instantiates the material
        // at runtime anyway; this just shares that instance.
        Material _progressMaterial;
        Material ProgressMaterial =>
            _progressMaterial != null ? _progressMaterial : _progressMaterial = loadingProgress.material;

        void OnDestroy()
        {
            if (_progressMaterial != null)
                Destroy(_progressMaterial);
        }

        void Awake()
        {
            scene = BSScene.Instance();
            if (feetTransform == null)
                Debug.LogWarning("[Cage] feetTransform is not assigned — falling back to camera position for cage placement.");
            SetCanCancel(false);
            _ = CustomLoadSkybox();
            SetLoadProgress("Welcome", 0, "Getting things ready...", false);
        }

        void Start()
        {
            ShowOpaqueImmediately();
        }

        /// <summary>
        /// Present the cage fully opaque the moment the app starts, so the empty scene is never
        /// visible before the first space load. Marks state Loaded so the first LoadIn is a
        /// no-op — otherwise LoadIn's clip fades _DissolveAmount 1 to 0 (invisible to opaque),
        /// flashing the empty startup scene during that fade. The first LoadOut then reveals
        /// the loaded space as normal.
        /// </summary>
        void ShowOpaqueImmediately()
        {
            MoveToPlayer();

            if (loadingBar != null)
            {
                loadingBar.SetActive(true);
                loadingBar.GetComponent<RotateLoading>()?.MoveInFront();
            }
            loadingSphere.transform.parent.GetComponent<RotateLoading>()?.MoveInFront();

            if (maskTextures != null && maskTextures.Length > 0)
            {
                var mask = maskTextures[UnityEngine.Random.Range(0, maskTextures.Length - 1)];
                ProgressMaterial.SetTexture("_DisolveGuide", mask);
                ProgressMaterial.SetTexture("_ThumbDisolveGuide", mask);
            }
            ProgressMaterial.SetFloat("_DissolveLoadAmount", 0);
            ProgressMaterial.SetFloat("_DissolveAmount", 0); // 0 = fully opaque

            // Block the player in, as LoadIn would.
            loadingProgress.gameObject.GetComponent<BoxCollider>().enabled = true;
            loadingProgress.gameObject.GetComponent<MeshCollider>().enabled = true;
            teleportWall.SetActive(true);

            state = LoadingState.Loaded;
        }

        // Cage follows the player every frame while up (see LateUpdate) — deliberately in
        // LateUpdate, NOT the OnTeleport event: the rig teleports through Rigidbody.position,
        // which lands after the next physics step, so reading position here (post-physics)
        // keeps the cage in sync with what's actually rendered, teleports included.

        /// <summary>Player floor position: centred HORIZONTALLY on the head (camera) so the cage
        /// surrounds where the player actually is — the feet transform is the rig origin, which on
        /// Quest is offset from the head by the room-scale standing position (that offset was
        /// putting the cage off to the side on first load). Height comes from the feet transform
        /// when available, else the head dropped by standing height.</summary>
        bool TryGetPlayerFloorPosition(out Vector3 position)
        {
            var cam = Camera.main;
            if (cam != null)
            {
                var head = cam.transform.position;
                float floorY = feetTransform ? feetTransform.position.y : head.y - 1.55f;
                position = new Vector3(head.x, floorY, head.z);
                return true;
            }

            if (feetTransform)
            {
                position = feetTransform.position;
                return true;
            }

            position = default;
            return false;
        }

        [Tooltip("Cage offset from the player's feet, in the player's facing space " +
                 "(x = right, y = up, z = forward). Negative Z sits the cage back behind the " +
                 "player, negative Y lowers it.")]
        [SerializeField] Vector3 _cageOffset = new Vector3(0f, -0.1f, -0.2f);

        // The cage SNAPS to the player (position + facing) rather than tracking continuously —
        // continuous tracking spun/swam the cage with the head, which is sickness-inducing. Each
        // (re)placement or teleport does one immediate snap. The FIRST space load additionally
        // keeps snapping on an interval for a couple of seconds, because the Quest camera facing
        // isn't settled at spawn and a single snap lands wrong; later teleports get just the one.
        const float k_TeleportDist = 1.5f;
        const float k_SnapInterval = 0.5f;
        const float k_FirstLoadFollowWindow = 2f;
        bool _firstLoadComplete;
        float _followWindowUntil = float.NegativeInfinity;
        float _nextSnapTime = float.NegativeInfinity;
        Vector3 _lastFollowPos;
        bool _hasFollowPos;

        void LateUpdate()
        {
            if (state == LoadingState.Unloaded)
            {
                _hasFollowPos = false;
                return;
            }

            // Detect a teleport (big position jump) and begin a fresh follow from the new spawn.
            if (TryGetPlayerFloorPosition(out var floor))
            {
                if (_hasFollowPos &&
                    (floor - _lastFollowPos).sqrMagnitude > k_TeleportDist * k_TeleportDist)
                    BeginFollow();
                _lastFollowPos = floor;
                _hasFollowPos = true;
            }

            // Periodic re-snap during the follow window (first load only — window is 0 otherwise).
            if (Time.unscaledTime >= _nextSnapTime && Time.unscaledTime <= _followWindowUntil)
            {
                SnapToPlayer();
                _nextSnapTime = Time.unscaledTime + k_SnapInterval;
            }
        }

        /// <summary>Snap the cage to the player now, and (first load only) keep re-snapping on an
        /// interval for a short window so a not-yet-settled camera is caught. Called on
        /// open/preload and on teleport.</summary>
        public void MoveToPlayer() => BeginFollow();

        void BeginFollow()
        {
            SnapToPlayer();
            _followWindowUntil = Time.unscaledTime + (_firstLoadComplete ? 0f : k_FirstLoadFollowWindow);
            _nextSnapTime = Time.unscaledTime + k_SnapInterval;
            if (TryGetPlayerFloorPosition(out var floor))
            {
                _lastFollowPos = floor;
                _hasFollowPos = true;
            }
        }

        void SnapToPlayer()
        {
            UpdateYaw();
            ApplyPosition();
        }

        /// <summary>Yaw the cage to the player's flat look direction (camera), applied only at
        /// snap moments so it doesn't spin as the player turns while caged.</summary>
        void UpdateYaw()
        {
            var cam = Camera.main;
            if (cam == null)
                return;
            var forward = cam.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.LookRotation(forward.normalized);
        }

        void ApplyPosition()
        {
            if (TryGetPlayerFloorPosition(out var floor))
                transform.position = floor + transform.rotation * _cageOffset;
        }
        async Task CustomLoadSkybox()
        {
            try
            {
                var filepath = Path.Combine(Application.persistentDataPath, "custom-skybox.png");
                if (File.Exists(filepath))
                {
                    var tex = await Get.Texture("file://" + filepath);
                    ProgressMaterial.SetTexture("_Pano", tex);
                }
                else
                {
                    ProgressMaterial.SetTexture("_Pano", defaultLoadingImage);
                }
            }
            catch { }
        }
        public void ResetLoadingProgress()
        {
            loadingBarInner.localScale = new Vector3(0, 1, 1);
            ProgressMaterial.SetFloat("_DissolveAmount", 0);
        }
        public void SetCanCancel(bool canCancel)
        {
            CanCancel = canCancel;
            UpdateCancelText();
        }
        public void SetLoadProgress(string loadingTitle, float percentage, string detailMessage, bool canCancel, Texture2D spaceImage = null)
        {
            UnityMainThreadTaskScheduler.Default.Enqueue(TaskRunner.Track(() =>
            {
                SetCanCancel(canCancel);
                if (spaceImage != null)
                {
                    spaceImage = MipMaps.Do(spaceImage);
                    spaceImage.wrapMode = TextureWrapMode.Clamp;
                    ProgressMaterial.SetTexture("_Thumb", spaceImage);
                    TweenFactory.Tween("loadingImage", 0, 1, 6f, TweenScaleFunctions.CubicEaseOut, (f) =>
                    {
                        ProgressMaterial.SetFloat("_DissolveLoadAmount", f.CurrentValue);
                    });
                }
                else
                {
                    loadingpercentage = percentage;
                    if (loadingpercentage < lastLoadingPercent)
                    {
                        TweenFactory.RemoveTweenKey("loadingBar", TweenStopBehavior.DoNotModify);
                        if (loadingBarInner != null)
                        {
                            loadingBarInner.localScale = new Vector3(loadingpercentage, 1, 1);
                        }
                    }
                    else
                    {
                        TweenFactory.Tween("loadingBar", lastLoadingPercent, loadingpercentage, 0.1f, TweenScaleFunctions.CubicEaseOut, (f) =>
                        {
                            if (loadingBarInner != null)
                            {
                                loadingBarInner.localScale = new Vector3(Mathf.Clamp(f.CurrentValue, 0f, 1f), 1, 1);
                            }
                        });
                    }
                    lastLoadingPercent = loadingpercentage;
                    if (loadingText != null)
                    {
                        loadingText.text = detailMessage ?? "";
                    }
                    if (titleText != null)
                    {
                        titleText.text = loadingTitle ?? "Loading...";
                    }
                }
            }, $"{nameof(LoadingBarManager)}.{nameof(SetLoadProgress)}"));
        }

        public string GetCancelButtonText()
        {
#if UNITY_STANDALONE_OSX
            return "Press X";
#elif UNITY_ANDROID
            //TODO: windows VR butons?  Pico VR buttons?
            return "Press both sticks";
#else
            return "Press both sticks or F6";
#endif
        }
        public void UpdateCancelText(bool force = false, bool wasKicked = false)
        {
            if (cancelText != null)
            {
                if (CanCancel)
                {
                    if (scene.HasLoadFailed() || force)
                    {
                        spinner.gameObject.SetActive(false);
                        loadFailed.SetActive(true);
                        cancelText.text = GetCancelButtonText() + (scene.isFallbackHome ? " to retry" : scene.isHome ? " to go to the fallback lobby" : " to go home");
                    }
                    else if (wasKicked)
                    {
                        spinner.gameObject.SetActive(false);
                        loadFailed.SetActive(true);
                        cancelText.text = "Sending you elsewhere";
                    }
                    else
                    {
                        spinner.gameObject.SetActive(true);
                        loadFailed.SetActive(false);
                        cancelText.text = currentUrl == BSScene.CUSTOM_HOME_SPACE ? "Homespace: " + currentUrl : GetCancelButtonText() + " to cancel";
                    }
                }
                else cancelText.text = "";
            }
        }

        // public void MoveToUser(Vector3 offset = default)
        // {
        //     var camPos = Camera.main.transform.position;
        //     var y = camPos.y;
        //     var pos = camPos + offset;
        //     pos.y = y;
        //     transform.position = pos;
        // }
        public Transform feetTransform;
        public void Preload()
        {
            MoveToPlayer();
            ResetLoadingProgress();
            loadingBar.SetActive(true);
            loadingBar.GetComponent<RotateLoading>().MoveInFront();
            loadingSphere.transform.parent.GetComponent<RotateLoading>().MoveInFront();
            var mask = maskTextures[UnityEngine.Random.Range(0, maskTextures.Length - 1)];
            ProgressMaterial.SetTexture("_DisolveGuide", mask);
            ProgressMaterial.SetTexture("_ThumbDisolveGuide", mask);
            ProgressMaterial.SetFloat("_DissolveLoadAmount", 0);
            SetLoadProgress("Loading", 0, scene.LoadingStatus, true);
        }
        public async Task LoadIn(string url)
        {
            if (state == LoadingState.Loaded)
            {
                return;
            }
            state = LoadingState.Loading;
            SetLoadProgress("Loading", 0, scene.LoadingStatus, true);
            currentUrl = url;
            loadingProgress.gameObject.GetComponent<BoxCollider>().enabled = true;
            loadingProgress.gameObject.GetComponent<MeshCollider>().enabled = true;
            teleportWall.SetActive(true);
            loadingSphere.clip = loadIn;
            loadingSphere.Play();
            await new WaitUntil(() => !loadingSphere.isPlaying);
            state = LoadingState.Loaded;
        }

        public async Task LoadOut()
        {
            LogLine.Do($"[LOADING] LoadOut state={state} scene.state={scene.state}");
            if (state == LoadingState.Loading || scene.state==SceneState.LOAD_FAILED)
            {
                return;
            }
            state = LoadingState.Loading;
            _firstLoadComplete = true; // camera is long settled by the first open; short grace hereafter
            // Disable the cage floor/geometry colliders BEFORE unfreeze (onLoadOutStarted) so the
            // freshly-dynamic loco-ball settles on the real world floor instead of squishing against
            // the cage floor for the whole ~1.5s dissolve. (Previously disabled 1.5s later in
            // HideCollider, which was the load-out squish source.) The teleport wall still lingers.
            loadingProgress.gameObject.GetComponent<BoxCollider>().enabled = false;
            loadingProgress.gameObject.GetComponent<MeshCollider>().enabled = false;
            onLoadOutStarted.Invoke();
            loadingSphere.clip = loadOut;
            loadingSphere.Play();
            loadingBar.SetActive(false);
            if (loadingText != null)
            {
                loadingText.text = "";
            }
            lastLoadingPercent = 0f;
            _ = HideCollider();
            await new WaitUntil(() => !loadingSphere.isPlaying);  // 
            state = LoadingState.Unloaded;
        }

        async Task HideCollider()
        {
            // Cage floor/geometry colliders are now disabled up-front in LoadOut (before unfreeze) so
            // they can't squish the freshly-dynamic rig; only the teleport wall lingers through the dissolve.
            await new WaitForSeconds(1.5f);
            teleportWall.SetActive(false);
        }
    }
}

