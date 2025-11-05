using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
namespace Banter.SDK
{
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BanterObjectId))]
    [WatchComponent]
    public class BanterKitItem : BanterComponentBase
    {
        [Tooltip("The location of the prefab in the kit object. Must match the path in the asset bundle (always lowercase).")]
        [See(initial = "")][SerializeField] internal string path = "";
        [See(initial = "false")] public bool resetTransform = false;

        GameObject item;
        public AssetBundle KitBundle;
        private async Task SetupKitItem()
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
                }
            }
            if (item != null)
            {
                Destroy(item);
            }
            try
            {
                GameObject asset = KitBundle.LoadAsset<GameObject>(path);
                if(resetTransform) {
                    asset.transform.localPosition = Vector3.zero;
                    asset.transform.localRotation = Quaternion.identity;
                }
                item = Instantiate(asset, transform, false);
                scene.kitItems.Add(item);
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
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.path, PropertyName.resetTransform, };
            UpdateCallback(changedProperties);
        }

        internal override void Init(List<object> constructorProperties = null)
        {
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(gameObject.GetInstanceID(), GetInstanceID(), ComponentType.BanterKitItem);


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
                    var valpath = (BanterString)values[i];
                    if (valpath.n == PropertyName.path)
                    {
                        path = valpath.x;
                        changedProperties.Add(PropertyName.path);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valresetTransform = (BanterBool)values[i];
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
            var updates = new List<BanterComponentPropertyUpdate>();
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.path,
                    type = PropertyType.String,
                    value = path,
                    componentType = ComponentType.BanterKitItem,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.resetTransform,
                    type = PropertyType.Bool,
                    value = resetTransform,
                    componentType = ComponentType.BanterKitItem,
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