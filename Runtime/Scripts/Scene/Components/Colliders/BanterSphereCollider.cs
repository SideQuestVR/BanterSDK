using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Banter
{
/* 
#### Sphere Collider
Add a sphere shaped physics collider to the object.

**Properties**
- `isTrigger` - If the collider is a trigger.
- `radius` - The radius of the sphere.

**Code Example**
```js
    const isTrigger = false;
    const radius = 0.5;
    const gameObject = new BS.GameObject("MySphereCollider"); 
    const sphereCollider = await gameObject.AddComponent(new BS.BanterSphereCollider(isTrigger, radius));
```
*/
    [WatchComponent(typeof(SphereCollider))]
    [RequireComponent(typeof(SphereCollider))]
    [RequireComponent(typeof(BanterObjectId))]
    public class BanterSphereCollider : UnityComponentBase
    {
        [See(initial = "false")] public bool isTrigger;
        [See(initial = "0.5")] public float radius;
// BANTER COMPILED CODE 
        public SphereCollider _componentType;
        public SphereCollider componentType {
            get{
                if(_componentType == null) {
                    _componentType = GetComponent<SphereCollider>();;
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
                    componentType = ComponentType.SphereCollider,
                    oid = oid,
                    cid = cid
                });
            }
           if(force) { 
                updates.Add(new BanterComponentPropertyUpdate(){
                    name = PropertyName.radius,
                    type = PropertyType.Float,
                    value = componentType.radius,
                    componentType = ComponentType.SphereCollider,
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