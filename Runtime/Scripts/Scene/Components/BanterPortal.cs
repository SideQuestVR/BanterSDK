using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
namespace Banter
{
/* 
#### Banter Portal
This component will add a portal to the object and set the url and instance of the portal.

**Properties**
 - `url` - The url of the space to link to.
 - `instance` - The instance of the space to link to.

**Code Example**
```js
    const url = "https://banter.host/space/5f9b4";
    const instance = "5f9b4";
    const gameObject = new BS.GameObject("MyPortal");
    const portal = await gameObject.AddComponent(new BS.BanterPortal(url, instance));
```

*/
    [RequireComponent(typeof(BanterObjectId))]
    [WatchComponent]
    public class BanterPortal : BanterComponentBase
    {

        public static string[] defaultPortalLandscapes = new string[]{
            "https://cdn.sidequestvr.com/file/567294/portalplaceholder_1.png",
            "https://cdn.sidequestvr.com/file/567295/portalplaceholder_2.png",
            "https://cdn.sidequestvr.com/file/567296/portalplaceholder_3.png",
            "https://cdn.sidequestvr.com/file/567297/portalplaceholder_4.png",
            "https://cdn.sidequestvr.com/file/567299/portalplaceholder_5.png",
            "https://cdn.sidequestvr.com/file/567300/portalplaceholder_6.png",
            "https://cdn.sidequestvr.com/file/567302/portalplaceholder_7.png",
            "https://cdn.sidequestvr.com/file/567303/portalplaceholder_8.png",
            "https://cdn.sidequestvr.com/file/567304/portalplaceholder_9.png",
            "https://cdn.sidequestvr.com/file/567305/portalplaceholder_10.png",
            "https://cdn.sidequestvr.com/file/567306/portalplaceholder_11.png",
            "https://cdn.sidequestvr.com/file/567307/portalplaceholder_12.png",
        };
        [See(initial = "")] public string url = "";
        [See(initial = "")] public string instance = "";
        MaterialPropertyBlock block;
        GameObject portal;
        BanterSceneEvents sceneEvents;
        public override void StartStuff() {
            sceneEvents = BanterScene.Instance().events;
            block = new MaterialPropertyBlock();
        }

        public static async Task SetTexture(string texture, MeshRenderer rend) {
            var tex = await Get.Texture(texture);
            if(tex != null && rend != null) {
                rend.material.SetTexture("_MainTex", MipMaps.Do(tex));
            }
        }

        private async Task SetupPortal()
        {
            if(portal != null) {
                Destroy(portal);
            }
            portal = Instantiate(Resources.Load<GameObject>("Prefabs/BanterPortal"), transform);
            portal.name = "BanterPortal";
            var rend = portal.transform.Find("PortalMesh").GetComponent<MeshRenderer>();
            portal.GetComponentInChildren<Portal>().url = url;
            var space = await Get.SpaceMeta(url);
            var defaultTex = defaultPortalLandscapes[UnityEngine.Random.Range(0, defaultPortalLandscapes.Length - 1)];
            SetLoadedIfNot();
            if(space != null) {
                try{
                    if(!string.IsNullOrEmpty(space.icon)) {
                        await SetTexture(space.icon + "?size=2048", rend);
                    }else{
                        await SetTexture(defaultTex + "?size=2048", rend);
                    }
                }catch(Exception e) {
                    Debug.Log(e);
                    await SetTexture(defaultTex, rend);
                }
                portal.transform.Find("Info/Titel").GetComponent<TMPro.TextMeshPro>().text = space.name;
            }else{
                portal.transform.Find("Info/Titel").GetComponent<TMPro.TextMeshPro>().text = "Unknown Space";
                await SetTexture(defaultTex, rend);
            }
        }

        public override void DestroyStuff() { }
        public void UpdateCallback(List<PropertyName> changedProperties) {
            _ = SetupPortal();
        }
// BANTER COMPILED CODE 
        BanterScene scene;
    
        bool alreadyStarted = false;
    
        void Start() { 
            Init(); 
            StartStuff();
        }
        public override void ReSetup() {
                   List<PropertyName> changedProperties = new List<PropertyName>(){PropertyName.url,PropertyName.instance,};
            UpdateCallback(changedProperties);
        }
        
    
        public override void Init()
        {
            scene = BanterScene.Instance();
            if(alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(gameObject.GetInstanceID(), GetInstanceID(), ComponentType.BanterPortal);
            
            
            oid = gameObject.GetInstanceID();
            cid = GetInstanceID();
            SyncProperties(true);
            
        
        }
     
        void Awake() {
            BanterScene.Instance().RegisterComponentOnMainThread(gameObject, this);
        }
    
        void OnDestroy()
        {
            scene.UnregisterComponentOnMainThread(gameObject, this);
            
            DestroyStuff();
        }
        public override object CallMethod(string methodName, List<object> parameters){
            return null;
        }
    
        public override void Deserialise(List<object> values) {
            List<PropertyName> changedProperties = new List<PropertyName>();
            for(int i = 0; i < values.Count; i++) {
                if(values[i] is BanterString){
                    var valurl = (BanterString)values[i];
                    if(valurl.n == PropertyName.url) {
                        url = valurl.x;
                        changedProperties.Add(PropertyName.url);
                    }
                }
                if(values[i] is BanterString){
                    var valinstance = (BanterString)values[i];
                    if(valinstance.n == PropertyName.instance) {
                        instance = valinstance.x;
                        changedProperties.Add(PropertyName.instance);
                    }
                }
            }
            if(values.Count > 0 ) { UpdateCallback(changedProperties);}
        }
        public override void SyncProperties(bool force = false, Action callback = null)
            {
            var updates = new List<BanterComponentPropertyUpdate>();
           if(force) { 
                updates.Add(new BanterComponentPropertyUpdate(){
                    name = PropertyName.url,
                    type = PropertyType.String,
                    value = url,
                    componentType = ComponentType.BanterPortal,
                    oid = oid,
                    cid = cid
                });
            }
           if(force) { 
                updates.Add(new BanterComponentPropertyUpdate(){
                    name = PropertyName.instance,
                    type = PropertyType.String,
                    value = instance,
                    componentType = ComponentType.BanterPortal,
                    oid = oid,
                    cid = cid
                });
            }
            scene.SetFromUnityProperties(updates, callback);
        }
        public override void WatchProperties(PropertyName[] properties) {
        }
// END BANTER COMPILED CODE 
    }
}