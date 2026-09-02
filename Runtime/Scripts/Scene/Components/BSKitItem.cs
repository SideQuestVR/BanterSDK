using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem.UI;
using UnityEngine.Networking;
namespace BS
{
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BSObjectId))]
    [WatchComponent]
    public class BSKitItem : BSComponentBase
    {
        [Tooltip("The location of the prefab in the kit object. Must match the path in the asset bundle (always lowercase).")]
        [See(initial = "")][SerializeField] internal string path = "";
        [See(initial = "false")] public bool resetTransform = false;

        GameObject item;
        public AssetBundle KitBundle;

        /*
         * Sentinel path meaning "the first prefab of MY bundle" — the BSAssetBundle on this same
         * GameObject, not the scene-wide KitPaths registry (a sentinel is never registered there,
         * and the registry can hold many bundles). Used by the injection runtime's <bs-snippet
         * asset="..."> element, which adds an AssetBundle then a KitItem("*") to one object.
         */
        public const string FirstPrefabPath = "*";

        private async Task SetupKitItem()
        {
            string loadPath = path;
            if (path == FirstPrefabPath)
            {
                // No scene.bundlesLoaded gate here: snippets are added after scene load and must
                // not depend on the initial-load latch.
                var bundleComponent = GetComponent<BSAssetBundle>();
                if (bundleComponent == null)
                {
                    Debug.LogWarning($"[BSKitItem] '{name}' path is '{FirstPrefabPath}' but no BSAssetBundle sits on this object.");
                    SetLoadedIfNot(false, "KitItem '*' requires a sibling BSAssetBundle.");
                    return;
                }
                await new WaitUntil(() => bundleComponent.IsLoaded); // latches on success AND failure
                KitBundle = bundleComponent.assetBundle;
                if (KitBundle == null)
                {
                    // Same-bytes-already-loaded case: a second snippet sharing this asset URL
                    // fails its own load; fall back to the already-loaded twin (identical
                    // signature = identical URLs).
                    KitBundle = scene.settings.KitBundles.FirstOrDefault(b =>
                        b != bundleComponent && b.assetBundle != null &&
                        b.GetSignature() == bundleComponent.GetSignature())?.assetBundle;
                }
                if (KitBundle == null)
                {
                    // The latch on this component reports only once, ever — warn as well so later
                    // failures stay visible (see BSKitAsset for the precedent).
                    Debug.LogWarning($"[BSKitItem] '{name}' sibling bundle failed to load - cannot resolve first prefab.");
                    SetLoadedIfNot(false, "KitItem '*': sibling bundle failed to load.");
                    return;
                }
                loadPath = KitBundle.GetAllAssetNames()
                    .Where(p => p.EndsWith(".prefab"))
                    .OrderBy(p => p, StringComparer.Ordinal) // GetAllAssetNames order is unspecified; make "first" deterministic
                    .FirstOrDefault();
                if (loadPath == null)
                {
                    Debug.LogWarning($"[BSKitItem] '{name}' bundle contains no .prefab assets.");
                    SetLoadedIfNot(false, "KitItem '*': bundle contains no prefabs.");
                    return;
                }
            }
            else
            {
                if(!scene.bundlesLoaded) {
                    await new WaitUntil(() => scene.bundlesLoaded);
                }
                if (KitBundle == null)
                {
                    if (scene.settings.KitPaths.ContainsKey(path))
                    {
                        KitBundle = scene.settings.KitPaths[path].assetBundle;
                    }
                    else
                    {
                        SetLoadedIfNot(false, "Kititem not found at path: " + path);
                        return; // previously fell through into a NullReferenceException on LoadAsset
                    }
                }
            }
            if (item != null)
            {
                Destroy(item);
            }
            try
            {
                GameObject asset = KitBundle.LoadAsset<GameObject>(loadPath);
                if(resetTransform) {
                    asset.transform.localPosition = Vector3.zero;
                    asset.transform.localRotation = Quaternion.identity;
                }
                item = Instantiate(asset, transform, false);
                scene.kitItems.Add(item);
                
                foreach (Transform transform in item.GetComponentsInChildren<Transform>(true))
                {
                    var canvas = transform.gameObject.GetComponent<Canvas>();
                    if (canvas != null)
                    {
                        if (canvas.renderMode == RenderMode.WorldSpace)
                        {
                            canvas.worldCamera = Camera.main;
                            if (!canvas.GetComponent<CanvasCameraBinder>())
                                canvas.gameObject.AddComponent<CanvasCameraBinder>();
                            if (!canvas.GetComponent<BoxCollider>())
                            {
                                var box = canvas.gameObject.AddComponent<BoxCollider>();
                                var rt = canvas.GetComponent<RectTransform>();
                                box.size = new Vector3(rt.rect.width, rt.rect.height, 0.01f);
                                box.center = new Vector3(0f, 0f, 0.015f);
                            }
                            var trackedDeviceRaycaster = canvas.gameObject.GetComponent<TrackedDeviceRaycaster>();
                            if(trackedDeviceRaycaster)
                                Destroy(trackedDeviceRaycaster);
                        }
                    }
                }
                
                SetLoadedIfNot();
            }
            catch (Exception e)
            {
                SetLoadedIfNot(false, e.Message);
            }
        }

        internal override void DestroyStuff()
        {
            if (item != null && scene.kitItems.Contains(item))
            {
                scene.kitItems.Remove(item);
            }
        }

        internal override void UpdateStuff()
        {
            
        }
        internal override void StartStuff() { }
        internal void UpdateCallback(List<PropertyName> changedProperties)
        {
            _ = SetupKitItem();
        }
        // BANTER COMPILED CODE 
        public System.String Path { get { return path; } set { path = value; UpdateCallback(new List<PropertyName> { PropertyName.path }); } }
        public System.Boolean ResetTransform { get { return resetTransform; } set { resetTransform = value; UpdateCallback(new List<PropertyName> { PropertyName.resetTransform }); } }

        BSScene _scene;
        public BSScene scene
        {
            get
            {
                if (_scene == null)
                {
                    _scene = BSScene.Instance();
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
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.path, PropertyName.resetTransform, };
            UpdateCallback(changedProperties);
        }
        internal override string GetSignature()
        {
            return "KitItem" +  PropertyName.path + path + PropertyName.resetTransform + resetTransform;
        }

        internal override void Init(List<object> constructorProperties = null)
        {
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(gameObject.GetInstanceID(), GetInstanceID(), ComponentType.KitItem);


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
            BSScene.Instance().RegisterComponentOnMainThread(gameObject, this);
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
                if (values[i] is BSString)
                {
                    var valpath = (BSString)values[i];
                    if (valpath.n == PropertyName.path)
                    {
                        path = valpath.x;
                        changedProperties.Add(PropertyName.path);
                    }
                }
                if (values[i] is BSBool)
                {
                    var valresetTransform = (BSBool)values[i];
                    if (valresetTransform.n == PropertyName.resetTransform)
                    {
                        resetTransform = valresetTransform.x;
                        changedProperties.Add(PropertyName.resetTransform);
                    }
                }
            }
            if (values.Count > 0) { UpdateCallback(changedProperties); }
        }

        internal override void SyncProperties(bool force = false, Action callback = null)
        {
            var updates = new List<BSComponentPropertyUpdate>();
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.path,
                    type = PropertyType.String,
                    value = path,
                    componentType = ComponentType.KitItem,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.resetTransform,
                    type = PropertyType.Bool,
                    value = resetTransform,
                    componentType = ComponentType.KitItem,
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