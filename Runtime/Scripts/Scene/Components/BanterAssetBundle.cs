using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
namespace Banter.SDK
{

    /* 
    #### Banter Asset Bundle
    Load an asset bundle into Banter which contains a scene or a collection of prefabs.

    **Properties**
    - `windowsUrl` - The URL to the Windows asset bundle.
    - `osxUrl` - The URL to the OSX asset bundle.
    - `linuxUrl` - The URL to the Linux asset bundle.
    - `androidUrl` - The URL to the Android asset bundle.
    - `iosUrl` - The URL to the iOS asset bundle.
    - `vosUrl` - The URL to the Vision OS asset bundle.
    - `isScene` - If the asset bundle is a scene or a collection of prefabs.
    - `legacyShaderFix` - If the asset bundle requires a legacy shader/lighting fix like we had in the past with AFRAME.

    **Code Example**
    ```js
        const windowsUrl = "https://example.bant.ing/windows.banter";
        const osxUrl = null; // Not implemented yet...
        const linuxUrl = null; // Not implemented yet...
        const androidUrl = "https://example.bant.ing/android.banter";
        const iosUrl = null; // Not implemented yet...
        const vosUrl = null; // Not implemented yet...
        const isScene = true;
        const legacyShaderFix = false;
        const gameObject = new BS.GameObject("MyAssetBundle"); 
        const assetBundle = await gameObject.AddComponent(new BS.BanterAssetBundle(windowsUrl, osxUrl, linuxUrl, androidUrl, iosUrl, vosUrl, isScene, legacyShaderFix));
    ```

    */
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BanterObjectId))]
    [WatchComponent]
    public class BanterAssetBundle : BanterComponentBase
    {
        [Tooltip("The URL to the Windows asset bundle.")]
        [See(initial = "")][SerializeField] internal string windowsUrl = "";

        [Tooltip("The URL to the OSX asset bundle.")]
        [See(initial = "")][SerializeField] internal string osxUrl = "";

        [Tooltip("The URL to the Linux asset bundle.")]
        [See(initial = "")][SerializeField] internal string linuxUrl = "";

        [Tooltip("The URL to the Android asset bundle.")]
        [See(initial = "")][SerializeField] internal string androidUrl = "";

        [Tooltip("The URL to the iOS asset bundle.")]
        [See(initial = "")][SerializeField] internal string iosUrl = "";

        [Tooltip("The URL to the Vision OS asset bundle.")]
        [See(initial = "")][SerializeField] internal string vosUrl = "";

        [Tooltip("Indicates whether this asset bundle contains a scene or a collection of prefabs.")]
        [See(initial = "false")][SerializeField] internal bool isScene = false;

        [Tooltip("Enables a legacy shader fix for compatibility with older lighting models.")]
        [See(initial = "false")][SerializeField] internal bool legacyShaderFix = false;

        [Tooltip("The loaded asset bundle.")]
        public AssetBundle assetBundle;
        List<string> assetPaths;
        bool isLoading = false;
        internal override void StartStuff()
        {
            // _ = SetupBundle();
        }
        private async Task SetupBundle(List<PropertyName> changedProperties)
        {
            if (isScene)
            {

                if (scene.settings.SceneAssetBundle)
                {
                    SetLoadedIfNot(false, "Scene bundle already registered!");
                    return;
                }
                scene.settings.SceneAssetBundle = this;
            }
#if !BANTER_EDITOR
            if (isScene)
            {
                SetLoadedIfNot();
                return;
            }
#endif
            try
            {
                await LoadBundle(changedProperties);
                SetLoadedIfNot();
            }
            catch (Exception e)
            {
                SetLoadedIfNot(false, e.Message);
                LogLine.Do(Color.red, LogTag.Banter, e.Message);
            }
        }

        internal async Task AfterBundleLoad() {
            
                if (isScene)
                {
                    await SetupSceneBundle();
                }
                else
                {
                    assetPaths = assetBundle.GetAllAssetNames().ToList();
                    scene.settings.KitBundles.Add(this);
                    foreach (var path in assetPaths)
                    {
                        if (path.EndsWith(".prefab"))
                        {
                            scene.settings.KitPaths.Add(path, this);
                        }
                    }
                }
        }

        internal async Task LoadBundle(List<PropertyName> changedProperties)
        {
            if(isLoading)
            {
                LogLine.Do("Waiting for bundle to load before loading again...");
                await new WaitUntil(() => !isLoading);
            }
            try
            {
#if UNITY_STANDALONE_WIN
            if(changedProperties.Contains(PropertyName.windowsUrl))
            {
                isLoading = true;
                assetBundle = await Get.AssetBundle(windowsUrl, progress: progress => this.progress?.Invoke(progress));
                await AfterBundleLoad();
            }
#elif UNITY_STANDALONE_OSX
            if(changedProperties.Contains(PropertyName.osxUrl))
            {
                isLoading = true;
                assetBundle = await Get.AssetBundle(osxUrl, progress: progress => this.progress?.Invoke(progress));
                await AfterBundleLoad();
            }
#elif UNITY_STANDALONE_LINUX
            if(changedProperties.Contains(PropertyName.linuxUrl))
            {
                isLoading = true;
                assetBundle = await Get.AssetBundle(linuxUrl, progress: progress => this.progress?.Invoke(progress));
                await AfterBundleLoad();
            }
#elif UNITY_ANDROID
            if(changedProperties.Contains(PropertyName.androidUrl))
            {
                isLoading = true;
                assetBundle = await Get.AssetBundle(androidUrl, progress: progress => this.progress?.Invoke(progress));
                await AfterBundleLoad();
            }
#elif UNITY_VISIONOS
            if(changedProperties.Contains(PropertyName.vosUrl))
            {
                isLoading = true;
                assetBundle = await Get.AssetBundle(vosUrl, progress: progress => this.progress?.Invoke(progress));
                await AfterBundleLoad();
            }
#elif UNITY_IOS
            if(changedProperties.Contains(PropertyName.iosUrl))
            {
                isLoading = true;
                assetBundle = await Get.AssetBundle(iosUrl, progress: progress => this.progress?.Invoke(progress));
                await AfterBundleLoad();
            }
#endif      
            }
            catch (Exception e)
            {
                SetLoadedIfNot(false, e.Message);
                LogLine.Do(Color.red, LogTag.Banter, androidUrl + ": " + e.Message);
            }
            isLoading = false;
        }


        private async Task SetupSceneBundle()
        {
            var scenes = assetBundle.GetAllScenePaths();
            if (scenes.Length > 0)
            {
                string sceneName = System.IO.Path.GetFileNameWithoutExtension(scenes[0]);
                await SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
                if (legacyShaderFix)
                {
                    RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
                    RenderSettings.ambientSkyColor = Color.white;
                    RenderSettings.ambientEquatorColor = Color.white;
                    RenderSettings.ambientGroundColor = new Color(0.1f, 0.1f, 0.1f, 1f);
                }
                var reference = SceneManager.GetSceneByName(sceneName);

                foreach (GameObject thisgo in reference.GetRootGameObjects())
                {
                    foreach (Transform transform in thisgo.GetComponentsInChildren<Transform>(true))
                    {
                        // if (legacyShaderFix)
                        // {
                        //     transform.gameObject.AddComponent<FixLegacyShaders>();
                        // }
                        var inputModule = transform.gameObject.GetComponent<StandaloneInputModule>();
                        if (inputModule != null)
                        {
                            Destroy(inputModule);
                        }
                        var eventSystem = transform.gameObject.GetComponent<EventSystem>();
                        if (eventSystem != null)
                        {
                            Destroy(eventSystem);
                        }
                        var starterUpper = transform.gameObject.GetComponent<BanterStarterUpper>();
                        if (starterUpper != null)
                        {
                            Destroy(starterUpper);
                        }
                        var audioListener = transform.gameObject.GetComponent<AudioListener>();
                        if (audioListener != null)
                        {
                            Destroy(audioListener);
                        }
                        var canvas = transform.gameObject.GetComponent<Canvas>();
                        if (canvas != null)
                        {
                            if (canvas.renderMode == RenderMode.WorldSpace)
                            {
                                canvas.worldCamera = Camera.main;
                            }
                        }
                    }
                }
            }
        }
        async Task _Unload()
        {
            await assetBundle.UnloadAsync(true);
            assetBundle = null;
            if (isScene)
            {
                scene.settings.SceneAssetBundle = null;
                await SceneManager.LoadSceneAsync("Void", LoadSceneMode.Single);
            }
            else
            {
                scene.settings.KitBundles.Remove(this);
            }
        }

        public async Task Unload()
        {
            if (assetBundle != null)
            {
                await _Unload();
            }
            else if (isLoading)
            {
                LogLine.Do("Waiting for bundle to load before unloading:" + windowsUrl);
                await new WaitUntil(() => !isLoading);
                LogLine.Do("Bundle loaded, unloading:" + windowsUrl);
                await _Unload();
            }
        }

        internal override void DestroyStuff() { }
        internal void UpdateCallback(List<PropertyName> changedProperties)
        {
            _ = SetupBundle(changedProperties);
        }
        // BANTER COMPILED CODE 
        public System.String WindowsUrl { get { return windowsUrl; } set { windowsUrl = value; UpdateCallback(new List<PropertyName> { PropertyName.windowsUrl }); } }
        public System.String OsxUrl { get { return osxUrl; } set { osxUrl = value; UpdateCallback(new List<PropertyName> { PropertyName.osxUrl }); } }
        public System.String LinuxUrl { get { return linuxUrl; } set { linuxUrl = value; UpdateCallback(new List<PropertyName> { PropertyName.linuxUrl }); } }
        public System.String AndroidUrl { get { return androidUrl; } set { androidUrl = value; UpdateCallback(new List<PropertyName> { PropertyName.androidUrl }); } }
        public System.String IosUrl { get { return iosUrl; } set { iosUrl = value; UpdateCallback(new List<PropertyName> { PropertyName.iosUrl }); } }
        public System.String VosUrl { get { return vosUrl; } set { vosUrl = value; UpdateCallback(new List<PropertyName> { PropertyName.vosUrl }); } }
        public System.Boolean IsScene { get { return isScene; } set { isScene = value; UpdateCallback(new List<PropertyName> { PropertyName.isScene }); } }
        public System.Boolean LegacyShaderFix { get { return legacyShaderFix; } set { legacyShaderFix = value; UpdateCallback(new List<PropertyName> { PropertyName.legacyShaderFix }); } }

        BanterScene _scene;
        public BanterScene scene
        {
            get
            {
                if (_scene == null)
                {
                    _scene = BanterScene.Instance();
                }
                return _scene;
            }
        }
        bool alreadyStarted = false;
        void Start()
        {
            Init();
            StartStuff();
        }

        internal override void ReSetup()
        {
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.windowsUrl, PropertyName.osxUrl, PropertyName.linuxUrl, PropertyName.androidUrl, PropertyName.iosUrl, PropertyName.vosUrl, PropertyName.isScene, PropertyName.legacyShaderFix, };
            UpdateCallback(changedProperties);
        }

        internal override void Init(List<object> constructorProperties = null)
        {
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(gameObject.GetInstanceID(), GetInstanceID(), ComponentType.BanterAssetBundle);


            oid = gameObject.GetInstanceID();
            cid = GetInstanceID();

            if (constructorProperties != null)
            {
                Deserialise(constructorProperties);
            }

            SyncProperties(true);

        }

        void Awake()
        {
            BanterScene.Instance().RegisterComponentOnMainThread(gameObject, this);
        }

        void OnDestroy()
        {
            scene.UnregisterComponentOnMainThread(gameObject, this);

            DestroyStuff();
        }

        internal override object CallMethod(string methodName, List<object> parameters)
        {
            return null;
        }

        internal override void Deserialise(List<object> values)
        {
            List<PropertyName> changedProperties = new List<PropertyName>();
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] is BanterString)
                {
                    var valwindowsUrl = (BanterString)values[i];
                    if (valwindowsUrl.n == PropertyName.windowsUrl)
                    {
                        windowsUrl = valwindowsUrl.x;
                        changedProperties.Add(PropertyName.windowsUrl);
                    }
                }
                if (values[i] is BanterString)
                {
                    var valosxUrl = (BanterString)values[i];
                    if (valosxUrl.n == PropertyName.osxUrl)
                    {
                        osxUrl = valosxUrl.x;
                        changedProperties.Add(PropertyName.osxUrl);
                    }
                }
                if (values[i] is BanterString)
                {
                    var vallinuxUrl = (BanterString)values[i];
                    if (vallinuxUrl.n == PropertyName.linuxUrl)
                    {
                        linuxUrl = vallinuxUrl.x;
                        changedProperties.Add(PropertyName.linuxUrl);
                    }
                }
                if (values[i] is BanterString)
                {
                    var valandroidUrl = (BanterString)values[i];
                    if (valandroidUrl.n == PropertyName.androidUrl)
                    {
                        androidUrl = valandroidUrl.x;
                        changedProperties.Add(PropertyName.androidUrl);
                    }
                }
                if (values[i] is BanterString)
                {
                    var valiosUrl = (BanterString)values[i];
                    if (valiosUrl.n == PropertyName.iosUrl)
                    {
                        iosUrl = valiosUrl.x;
                        changedProperties.Add(PropertyName.iosUrl);
                    }
                }
                if (values[i] is BanterString)
                {
                    var valvosUrl = (BanterString)values[i];
                    if (valvosUrl.n == PropertyName.vosUrl)
                    {
                        vosUrl = valvosUrl.x;
                        changedProperties.Add(PropertyName.vosUrl);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valisScene = (BanterBool)values[i];
                    if (valisScene.n == PropertyName.isScene)
                    {
                        isScene = valisScene.x;
                        changedProperties.Add(PropertyName.isScene);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var vallegacyShaderFix = (BanterBool)values[i];
                    if (vallegacyShaderFix.n == PropertyName.legacyShaderFix)
                    {
                        legacyShaderFix = vallegacyShaderFix.x;
                        changedProperties.Add(PropertyName.legacyShaderFix);
                    }
                }
            }
            if (values.Count > 0) { UpdateCallback(changedProperties); }
        }

        internal override void SyncProperties(bool force = false, Action callback = null)
        {
            var updates = new List<BanterComponentPropertyUpdate>();
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.windowsUrl,
                    type = PropertyType.String,
                    value = windowsUrl,
                    componentType = ComponentType.BanterAssetBundle,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.osxUrl,
                    type = PropertyType.String,
                    value = osxUrl,
                    componentType = ComponentType.BanterAssetBundle,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.linuxUrl,
                    type = PropertyType.String,
                    value = linuxUrl,
                    componentType = ComponentType.BanterAssetBundle,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.androidUrl,
                    type = PropertyType.String,
                    value = androidUrl,
                    componentType = ComponentType.BanterAssetBundle,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.iosUrl,
                    type = PropertyType.String,
                    value = iosUrl,
                    componentType = ComponentType.BanterAssetBundle,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.vosUrl,
                    type = PropertyType.String,
                    value = vosUrl,
                    componentType = ComponentType.BanterAssetBundle,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.isScene,
                    type = PropertyType.Bool,
                    value = isScene,
                    componentType = ComponentType.BanterAssetBundle,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.legacyShaderFix,
                    type = PropertyType.Bool,
                    value = legacyShaderFix,
                    componentType = ComponentType.BanterAssetBundle,
                    oid = oid,
                    cid = cid
                });
            }
            scene.SetFromUnityProperties(updates, callback);
        }

        internal override void WatchProperties(PropertyName[] properties)
        {
        }
        // END BANTER COMPILED CODE 
    }
}