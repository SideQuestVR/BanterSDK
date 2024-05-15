using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
namespace Banter
{
/* 
#### Banter Mirror
Add a mirror to the object.

**Code Example**
```js
    const gameObject = new BS.GameObject("MyMirror");
    const mirror = await gameObject.AddComponent(new BS.BanterMirror());
```
*/
    [RequireComponent(typeof(BanterObjectId))]
    [WatchComponent]
    public class BanterMirror : BanterComponentBase
    {
        public override void StartStuff() {
            SetLoadedIfNot();
            var obj = Instantiate(Resources.Load<GameObject>("Prefabs/BanterMirror"));
            obj.transform.SetParent(transform, false);
        }
        public override void DestroyStuff() { }
        public void UpdateCallback(List<PropertyName> changedProperties) {}
// BANTER COMPILED CODE 
        BanterScene scene;
    
        bool alreadyStarted = false;
    
        void Start() { 
            Init(); 
            StartStuff();
        }
        public override void ReSetup() {
                   List<PropertyName> changedProperties = new List<PropertyName>(){};
            UpdateCallback(changedProperties);
        }
        
    
        public override void Init()
        {
            scene = BanterScene.Instance();
            if(alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(gameObject.GetInstanceID(), GetInstanceID(), ComponentType.BanterMirror);
            
            
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
            }
            if(values.Count > 0 ) { UpdateCallback(changedProperties);}
        }
        public override void SyncProperties(bool force = false, Action callback = null)
            {
            var updates = new List<BanterComponentPropertyUpdate>();
            scene.SetFromUnityProperties(updates, callback);
        }
        public override void WatchProperties(PropertyName[] properties) {
        }
// END BANTER COMPILED CODE 
    }
}