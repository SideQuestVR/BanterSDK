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
    [RequireComponent(typeof(BanterObjectId))]
    [WatchComponent]
    public class BanterAssetBundle : BanterComponentBase
    {
        [See(initial = "")] public string windowsUrl = "";
        [See(initial = "")] public string osxUrl = "";
        [See(initial = "")] public string linuxUrl = "";
        [See(initial = "")] public string androidUrl = "";
        [See(initial = "")] public string iosUrl = "";
        [See(initial = "")] public string vosUrl = "";
        [See(initial = "false")] public bool isScene = false;
        [See(initial = "false")] public bool legacyShaderFix = false;
        public AssetBundle assetBundle;
        List<string> assetPaths;
        bool isLoading = false;
        public override void StartStuff()
        {
            // _ = SetupBundle();
        }
        private async Task SetupBundle()
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
            else
            {
                if (scene.settings.KitBundles.Count > 2)
                {
                    SetLoadedIfNot(false, "Three kit bundles already registered!");
                    return;
                }
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
                await LoadBundle();
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
                SetLoadedIfNot();
            }
            catch (Exception e)
            {
                SetLoadedIfNot(false, e.Message);
                LogLine.Do(Color.red, LogTag.Banter, e.Message);
            }
        }

        public async Task LoadBundle()
        {
            isLoading = true;
            try
            {
#if UNITY_STANDALONE_WIN
            assetBundle = await Get.AssetBundle(windowsUrl, progress: progress => this.progress?.Invoke(progress));
#elif UNITY_STANDALONE_OSX
            assetBundle = await Get.AssetBundle(osxUrl, progress: progress => this.progress?.Invoke(progress));
#elif UNITY_STANDALONE_LINUX
            assetBundle = await Get.AssetBundle(linuxUrl, progress: progress => this.progress?.Invoke(progress));
#elif UNITY_ANDROID
            assetBundle = await Get.AssetBundle(androidUrl, progress: progress => this.progress?.Invoke(progress));
#elif UNITY_VISIONOS
            assetBundle = await Get.AssetBundle(vosUrl, progress: progress => this.progress?.Invoke(progress));
#elif UNITY_IOS
            assetBundle = await Get.AssetBundle(iosUrl, progress: progress => this.progress?.Invoke(progress));
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
                        if (legacyShaderFix)
                        {
                            transform.gameObject.AddComponent<FixLegacyShaders>();
                        }
                        // if(shaders != null) {
                        //     applyShader.shaders = shaders;
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
                        // if(!PlatformCompat.Instance.Is2DMode){
                        //     var cam = transform.gameObject.GetComponent<Camera>();
                        //     if(cam != null && cam.CompareTag("MainCamera")) {
                        //         Destroy(cam.gameObject);
                        //     }
                        // }
                    }
                }
                // sceneParser.CurrentSceneName = sceneName;
                // Debug.Log("[AssetBundleComponent] " + sceneParser.CurrentSceneName);
                // item.isLoaded = true;
            }
        }
        async Task _Unload()
        {
            await assetBundle.UnloadAsync(true);
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

        public override void DestroyStuff() { }
        public void UpdateCallback(List<PropertyName> changedProperties)
        {
            _ = SetupBundle();
        }
        // BANTER COMPILED CODE 
        BanterScene scene;
        bool alreadyStarted = false;
        void Start()
        {
            Init();
            StartStuff();
        }

        public override void ReSetup()
        {
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.windowsUrl, PropertyName.osxUrl, PropertyName.linuxUrl, PropertyName.androidUrl, PropertyName.iosUrl, PropertyName.vosUrl, PropertyName.isScene, PropertyName.legacyShaderFix, };
            UpdateCallback(changedProperties);
        }

        public override void Init(List<object> constructorProperties = null)
        {
            scene = BanterScene.Instance();
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

        public override object CallMethod(string methodName, List<object> parameters)
        {
            return null;
        }

        public override void Deserialise(List<object> values)
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

        public override void SyncProperties(bool force = false, Action callback = null)
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

        public override void WatchProperties(PropertyName[] properties)
        {
        }
        // END BANTER COMPILED CODE 
    }
}