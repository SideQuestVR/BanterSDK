using System;
using System.Collections.Generic;
using UnityEngine;

namespace Banter{
/* 
#### Banter Configurable Joint
A configurable joint allows you to create a joint between two rigidbodies and control the motion/options of the joint.

**Properties**
- `targetPosition` - The target position of the joint.
- `autoConfigureConnectedAnchor` - If the connected anchor should be auto configured.
- `xMotion` - The x motion of the joint.
- `yMotion` - The y motion of the joint.
- `zMotion` - The z motion of the joint.

**Code Example**
```js
    const targetPosition = new BS.Vector3(0,0,0);
    const autoConfigureConnectedAnchor = false;
    const xMotion = 0;
    const yMotion = 0;
    const zMotion = 0;
    
    const gameObject = new BS.GameObject("MyConfigurableJoint"); 
    const configurableJoint = await gameObject.AddComponent(new BS.BanterConfigurableJoint(targetPosition, autoConfigureConnectedAnchor, xMotion, yMotion, zMotion));
```

*/
    [WatchComponent(typeof(ConfigurableJoint))]
    [RequireComponent(typeof(ConfigurableJoint))]
    [RequireComponent(typeof(BanterObjectId))]
    public class BanterConfigurableJoint : UnityComponentBase
    {
        [See(initial = "0,0,0")] public Vector3 targetPosition;
        [See(initial = "false")] public bool autoConfigureConnectedAnchor;
        [See(initial = "0")] public ConfigurableJointMotion xMotion;
        [See(initial = "0")] public ConfigurableJointMotion yMotion;
        [See(initial = "0")] public ConfigurableJointMotion zMotion;
// BANTER COMPILED CODE 
        public ConfigurableJoint _componentType;
        public ConfigurableJoint componentType {
            get{
                if(_componentType == null) {
                    _componentType = GetComponent<ConfigurableJoint>();;
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
                if(values[i] is BanterVector3){
                    var valtargetPosition = (BanterVector3)values[i];
                    if(valtargetPosition.n == PropertyName.targetPosition) {
                        componentType.targetPosition = new Vector3(valtargetPosition.x,valtargetPosition.y,valtargetPosition.z);
                        changedProperties.Add(PropertyName.targetPosition);
                    }
                }
                if(values[i] is BanterBool){
                    var valautoConfigureConnectedAnchor = (BanterBool)values[i];
                    if(valautoConfigureConnectedAnchor.n == PropertyName.autoConfigureConnectedAnchor) {
                        componentType.autoConfigureConnectedAnchor = valautoConfigureConnectedAnchor.x;
                        changedProperties.Add(PropertyName.autoConfigureConnectedAnchor);
                    }
                }
                if(values[i] is BanterInt){
                    var valxMotion = (BanterInt)values[i];
                    if(valxMotion.n == PropertyName.xMotion) {
                        componentType.xMotion = (ConfigurableJointMotion)valxMotion.x;
                        changedProperties.Add(PropertyName.xMotion);
                    }
                }
                if(values[i] is BanterInt){
                    var valyMotion = (BanterInt)values[i];
                    if(valyMotion.n == PropertyName.yMotion) {
                        componentType.yMotion = (ConfigurableJointMotion)valyMotion.x;
                        changedProperties.Add(PropertyName.yMotion);
                    }
                }
                if(values[i] is BanterInt){
                    var valzMotion = (BanterInt)values[i];
                    if(valzMotion.n == PropertyName.zMotion) {
                        componentType.zMotion = (ConfigurableJointMotion)valzMotion.x;
                        changedProperties.Add(PropertyName.zMotion);
                    }
                }
            }
        }
        public override void SyncProperties(bool force = false, Action callback = null)
            {
            var updates = new List<BanterComponentPropertyUpdate>();
           if(force) { 
                updates.Add(new BanterComponentPropertyUpdate(){
                    name = PropertyName.targetPosition,
                    type = PropertyType.Vector3,
                    value = componentType.targetPosition,
                    componentType = ComponentType.ConfigurableJoint,
                    oid = oid,
                    cid = cid
                });
            }
           if(force) { 
                updates.Add(new BanterComponentPropertyUpdate(){
                    name = PropertyName.autoConfigureConnectedAnchor,
                    type = PropertyType.Bool,
                    value = componentType.autoConfigureConnectedAnchor,
                    componentType = ComponentType.ConfigurableJoint,
                    oid = oid,
                    cid = cid
                });
            }
           if(force) { 
                updates.Add(new BanterComponentPropertyUpdate(){
                    name = PropertyName.xMotion,
                    type = PropertyType.Int,
                    value = componentType.xMotion,
                    componentType = ComponentType.ConfigurableJoint,
                    oid = oid,
                    cid = cid
                });
            }
           if(force) { 
                updates.Add(new BanterComponentPropertyUpdate(){
                    name = PropertyName.yMotion,
                    type = PropertyType.Int,
                    value = componentType.yMotion,
                    componentType = ComponentType.ConfigurableJoint,
                    oid = oid,
                    cid = cid
                });
            }
           if(force) { 
                updates.Add(new BanterComponentPropertyUpdate(){
                    name = PropertyName.zMotion,
                    type = PropertyType.Int,
                    value = componentType.zMotion,
                    componentType = ComponentType.ConfigurableJoint,
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