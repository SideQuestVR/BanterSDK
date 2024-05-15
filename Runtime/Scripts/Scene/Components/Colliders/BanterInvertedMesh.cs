using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace Banter{
/* 
#### Banter Inverted Mesh
Invert the mesh of a GameObject. This is useful for creating inverted colliders.

**Code Example**
```js
    const gameObject = new BS.GameObject("MyInvertedMeshCollider"); 
    const invertedMesh = await gameObject.AddComponent(new BS.BanterInvertedMesh());
```
*/
    [RequireComponent(typeof(BanterObjectId))]
    [WatchComponent]
    public class BanterInvertedMesh : BanterComponentBase
    {
        public override void DestroyStuff() { }
        public void UpdateCallback(List<PropertyName> changedProperties) { }
        public override void StartStuff() {
            var meshFilter = GetComponent<MeshFilter>();
            if(meshFilter != null) {
                var mesh = meshFilter.mesh;
                mesh.triangles = mesh.triangles.Reverse().ToArray();
                Debug.Log("Loaded inverted mesh");
                SetLoadedIfNot();
            }
        }
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
            scene.RegisterBanterMonoscript(gameObject.GetInstanceID(), GetInstanceID(), ComponentType.BanterInvertedMesh);
            
            
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