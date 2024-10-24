using System.IO;
using System.Threading.Tasks;
using DigitalRuby.Tween;
using UnityEngine;
using UnityEngine.Events;

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
        void Awake()
        {
            scene = BanterScene.Instance();
            SetCanCancel(false);
            _ = CustomLoadSkybox();
            Preload();
            SetLoadProgress("Welcome to Banter", 0, "Getting things ready...", false);
        }
        async Task CustomLoadSkybox()
        {
            try
            {
                var filepath = Path.Combine(Application.persistentDataPath, "custom-skybox.png");
                if (File.Exists(filepath))
                {
                    var tex = await Get.Texture("file://" + filepath);
                    loadingProgress.sharedMaterial.SetTexture("_Pano", tex);
                }
                else
                {
                    loadingProgress.sharedMaterial.SetTexture("_Pano", defaultLoadingImage);
                }
            }
            catch { }
        }
        public void ResetLoadingProgress()
        {
            loadingBarInner.localScale = new Vector3(0, 1, 1);
            loadingProgress.sharedMaterial.SetFloat("_DissolveAmount", 0);
        }
        public void SetCanCancel(bool canCancel)
        {
            CanCancel = canCancel;
            UpdateCancelText();
        }
        public void SetLoadProgress(string loadingTitle, float percentage, string detailMessage, bool canCancel, Texture2D spaceImage = null)
        {
            if (!UnityMainThreadDispatcher.Exists())
            {
                return;
            }
            UnityMainThreadDispatcher.Instance().Enqueue(() =>
            {
                SetCanCancel(canCancel);
                if (spaceImage != null)
                {
                    spaceImage = MipMaps.Do(spaceImage);
                    spaceImage.wrapMode = TextureWrapMode.Clamp;
                    loadingProgress.sharedMaterial.SetTexture("_Thumb", spaceImage);
                    TweenFactory.Tween("loadingImage", 0, 1, 6f, TweenScaleFunctions.CubicEaseOut, (f) =>
                    {
                        loadingProgress.sharedMaterial.SetFloat("_DissolveLoadAmount", f.CurrentValue);
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
            });
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

        public void Preload()
        {
            var pos = Camera.main.transform.position;
            pos.y -= 1.7f;
            transform.position = pos;
            // MoveToUser();
            ResetLoadingProgress();
            loadingBar.SetActive(true);
            loadingBar.GetComponent<RotateLoading>().MoveInFront();
            loadingSphere.transform.parent.GetComponent<RotateLoading>().MoveInFront();
            var mask = maskTextures[UnityEngine.Random.Range(0, maskTextures.Length - 1)];
            loadingProgress.sharedMaterial.SetTexture("_DisolveGuide", mask);
            loadingProgress.sharedMaterial.SetTexture("_ThumbDisolveGuide", mask);
            loadingProgress.sharedMaterial.SetFloat("_DissolveLoadAmount", 0);
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
            loadingProgress.gameObject.GetComponent<Collider>().enabled = true;
            loadingSphere.clip = loadIn;
            loadingSphere.Play();
            await new WaitUntil(() => !loadingSphere.isPlaying);
            state = LoadingState.Loaded;
        }

        public async Task LoadOut()
        {
            LogLine.Do($"[LOADING] LoadOut state={state} scene.state={scene.state}");
            if (state == LoadingState.Loading)
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
            loadingProgress.gameObject.GetComponent<Collider>().enabled = false;
        }
    }
}

