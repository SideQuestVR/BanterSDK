using System.IO;
using System.Threading.Tasks;
using DigitalRuby.Tween;
using UnityEngine;
using UnityEngine.Events;
using Banter.Utilities.Async;

namespace Banter.SDK
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
        public GameObject loadFailed;
        public Transform spinner;
        BanterScene scene;
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
            scene = BanterScene.Instance();
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

        // Teleport following via feet-jump detection ONLY — deliberately NOT via the
        // OnTeleport scene event. The rig teleports through Rigidbody.position, which lands
        // after the next physics step; moving the cage eagerly on the event put it at the
        // destination frames BEFORE the player visibly moved, flashing the scene around them.
        // A feet jump beyond what locomotion can do in one frame is a teleport of some kind
        // (script/JS, controller arc, respawn safety net), and it is detected in LateUpdate
        // of exactly the frame the rig visibly moves — cage and player render together.
        const float k_TeleportSnapDistance = 1.5f;
        Vector3 _lastFeetPosition;
        bool _hasLastFeetPosition;

        /// <summary>Player floor position: feet transform when assigned AND alive at runtime,
        /// else the camera dropped to floor height. The feet reference has proven fragile
        /// (null at runtime in the scene setup), and the camera jumps with the rig anyway.</summary>
        bool TryGetPlayerFloorPosition(out Vector3 position)
        {
            if (feetTransform)
            {
                position = feetTransform.position;
                return true;
            }

            var cam = Camera.main;
            if (cam != null)
            {
                position = cam.transform.position + Vector3.down * 1.55f;
                return true;
            }

            position = default;
            return false;
        }

        void LateUpdate()
        {
            if (!TryGetPlayerFloorPosition(out var feet))
                return;
            // Only carry the cage while it's actually in use (loading in/up/out) — but keep
            // tracking the feet while idle, so the position isn't stale when the next load
            // starts (a stale value would read as a phantom jump on the first frame).
            if (state != LoadingState.Unloaded &&
                _hasLastFeetPosition &&
                (feet - _lastFeetPosition).sqrMagnitude > k_TeleportSnapDistance * k_TeleportSnapDistance)
            {
                MoveToPlayer();
            }
            _lastFeetPosition = feet;
            _hasLastFeetPosition = true;
        }

        [Tooltip("Cage offset from the player's feet, in the player's facing space " +
                 "(x = right, y = up, z = forward). Negative Z sits the cage back behind the " +
                 "player, negative Y lowers it.")]
        [SerializeField] Vector3 _cageOffset = new Vector3(0f, -0.1f, -0.2f);

        /// <summary>Centers the cage on the player (feet transform when assigned, else camera
        /// position dropped to floor height), yaws it so the player faces its front, then applies
        /// _cageOffset in that facing space. Teleports can change rotation, not just position.</summary>
        public void MoveToPlayer()
        {
            // Resolve facing first so the offset is applied in the player's yaw space.
            var yaw = transform.rotation;
            var cam = Camera.main;
            if (cam != null)
            {
                var flatForward = cam.transform.forward;
                flatForward.y = 0;
                if (flatForward.sqrMagnitude > 0.001f)
                    yaw = Quaternion.LookRotation(flatForward.normalized);
            }
            transform.rotation = yaw;

            if (TryGetPlayerFloorPosition(out var floor))
                transform.position = floor + yaw * _cageOffset;
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
                        cancelText.text = currentUrl == BanterScene.CUSTOM_HOME_SPACE ? "Homespace: " + currentUrl : GetCancelButtonText() + " to cancel";
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
            await new WaitForSeconds(1.5f);
            loadingProgress.gameObject.GetComponent<BoxCollider>().enabled = false;
            loadingProgress.gameObject.GetComponent<MeshCollider>().enabled = false;
            teleportWall.SetActive(false);
        }
    }
}

