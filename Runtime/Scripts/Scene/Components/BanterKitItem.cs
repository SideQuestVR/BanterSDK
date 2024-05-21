using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
namespace Banter.SDK
{
    /* 
    #### Banter Kit Item
    Add a kit item to the scene. This component will wait until all asset bundles are loaded inthe scene before trying to instantiate a kit item (prefab).

    **Properties**
     - `path` - The location of the preaf in the kit object - the same as the path in the asset bundle (always lower case).

    **Code Example**
    ```js
        const path = "assets/prefabs/mykititem.prefab";
        const gameObject = new BS.GameObject("MyKitItem"); 
        const kitItem = await gameObject.AddComponent(new BS.BanterKitItem(path));
    ```

    */
    [RequireComponent(typeof(BanterObjectId))]
    [WatchComponent]
    public class BanterKitItem : BanterComponentBase
    {
        [See(initial = "")] public string path = "";
        GameObject item;
        public AssetBundle KitBundle;
        private async Task SetupKitItem()
        {
            await new WaitUntil(() => scene.bundlesLoaded);
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
                GameObject asset = (GameObject)await KitBundle.LoadAssetAsync<GameObject>(path);
                item = Instantiate(asset, transform, false);
                scene.kitItems.Add(item);
                SetLoadedIfNot();
            }
            catch (Exception e)
            {
                SetLoadedIfNot(false, e.Message);
            }
        }

        public override void DestroyStuff()
        {
            if (item != null && scene.kitItems.Contains(item))
            {
                scene.kitItems.Remove(item);
            }
        }
        public override void StartStuff() { }
        public void UpdateCallback(List<PropertyName> changedProperties)
        {
            _ = SetupKitItem();
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
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.path, };
            UpdateCallback(changedProperties);
        }


        public override void Init()
        {
            scene = BanterScene.Instance();
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(gameObject.GetInstanceID(), GetInstanceID(), ComponentType.BanterKitItem);


            oid = gameObject.GetInstanceID();
            cid = GetInstanceID();
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
                    var valpath = (BanterString)values[i];
                    if (valpath.n == PropertyName.path)
                    {
                        path = valpath.x;
                        changedProperties.Add(PropertyName.path);
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
                    name = PropertyName.path,
                    type = PropertyType.String,
                    value = path,
                    componentType = ComponentType.BanterKitItem,
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