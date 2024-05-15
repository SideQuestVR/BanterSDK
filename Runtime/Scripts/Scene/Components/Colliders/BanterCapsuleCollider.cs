using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Banter
{
/* 
#### Capsule Collider
Add a capsule shaped physics collider to the object.

**Properties**
- `isTrigger` - If the collider is a trigger.
- `radius` - The radius of the capsule.
- `height` - The height of the capsule.

**Code Example**
```js
    const isTrigger = false;
    const radius = 0.5;
    const height = 2;
    const gameObject = new GameObject("MyCapsuleCollider"); 
    const capsuleCollider = await gameObject.AddComponent(new BS.BanterCapsuleCollider(isTrigger, radius, height));
```
*/
    [WatchComponent(typeof(CapsuleCollider))]
    [RequireComponent(typeof(CapsuleCollider))]
    [RequireComponent(typeof(BanterObjectId))]
    public class BanterCapsuleCollider : UnityComponentBase
    {
        [See(initial = "false")] public bool isTrigger;
        [See(initial = "0.5")] public float radius;
        [See(initial = "2")] public float height;
// BANTER COMPILED CODE 
        public CapsuleCollider _componentType;
        public CapsuleCollider componentType {
            get{
                if(_componentType == null) {
                    _componentType = GetComponent<CapsuleCollider>();;
                }
                return _componentType;
            }
        }
        BanterScene scene;
    
        bool alreadyStarted = false;
    
        void Start() { 
            Init(); 
            StartStuff();
        }
        public override void ReSetup() {
            
        }
        
    
        public override void Init()
        {
            scene = BanterScene.Instance();
            if(alreadyStarted) { return; }
            alreadyStarted = true;
            
            
            
            oid = gameObject.GetInstanceID();
            cid = GetInstanceID();
            SyncProperties(true);
            SetLoadedIfNot();
        
        }
     
        void Awake() {
            BanterScene.Instance().RegisterComponentOnMainThread(gameObject, this);
        }
    
        void OnDestroy()
        {
            scene.UnregisterComponentOnMainThread(gameObject, this);
            
            Destroy(componentType);
        }
        public override object CallMethod(string methodName, List<object> parameters){
            return null;
        }
    
        public override void Deserialise(List<object> values) {
            List<PropertyName> changedProperties = new List<PropertyName>();
            for(int i = 0; i < values.Count; i++) {
                if(values[i] is BanterBool){
                    var valisTrigger = (BanterBool)values[i];
                    if(valisTrigger.n == PropertyName.isTrigger) {
                        componentType.isTrigger = valisTrigger.x;
                        changedProperties.Add(PropertyName.isTrigger);
                    }
                }
                if(values[i] is BanterFloat){
                    var valradius = (BanterFloat)values[i];
                    if(valradius.n == PropertyName.radius) {
                        componentType.radius = valradius.x;
                        changedProperties.Add(PropertyName.radius);
                    }
                }
                if(values[i] is BanterFloat){
                    var valheight = (BanterFloat)values[i];
                    if(valheight.n == PropertyName.height) {
                        componentType.height = valheight.x;
                        changedProperties.Add(PropertyName.height);
                    }
                }
            }
        }
        public override void SyncProperties(bool force = false, Action callback = null)
            {
            var updates = new List<BanterComponentPropertyUpdate>();
           if(force) { 
                updates.Add(new BanterComponentPropertyUpdate(){
                    name = PropertyName.isTrigger,
                    type = PropertyType.Bool,
                    value = componentType.isTrigger,
                    componentType = ComponentType.CapsuleCollider,
                    oid = oid,
                    cid = cid
                });
            }
           if(force) { 
                updates.Add(new BanterComponentPropertyUpdate(){
                    name = PropertyName.radius,
                    type = PropertyType.Float,
                    value = componentType.radius,
                    componentType = ComponentType.CapsuleCollider,
                    oid = oid,
                    cid = cid
                });
            }
           if(force) { 
                updates.Add(new BanterComponentPropertyUpdate(){
                    name = PropertyName.height,
                    type = PropertyType.Float,
                    value = componentType.height,
                    componentType = ComponentType.CapsuleCollider,
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