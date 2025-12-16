using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace Banter.SDK
{
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
        internal override void DestroyStuff() { }
        internal void UpdateCallback(List<PropertyName> changedProperties) { }
        internal override void StartStuff()
        {
            var meshFilter = GetComponent<MeshFilter>();
            if (meshFilter != null)
            {
                var mesh = meshFilter.mesh;
                mesh.triangles = mesh.triangles.Reverse().ToArray();
                Debug.Log("Loaded inverted mesh");
                SetLoadedIfNot();
            }
        }
        internal override void UpdateStuff() {}
        // BANTER COMPILED CODE 
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
            List<PropertyName> changedProperties = new List<PropertyName>() { };
            UpdateCallback(changedProperties);
        }
        internal override string GetSignature()
        {
            return "BanterInvertedMesh";
        }

        internal override void Init(List<object> constructorProperties = null)
        {
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(gameObject.GetInstanceID(), GetInstanceID(), ComponentType.BanterInvertedMesh);


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
            }
            if (values.Count > 0) { UpdateCallback(changedProperties); }
        }

        internal override void SyncProperties(bool force = false, Action callback = null)
        {
            var updates = new List<BanterComponentPropertyUpdate>();
            scene.SetFromUnityProperties(updates, callback);
        }

        internal override void WatchProperties(PropertyName[] properties)
        {
        }
        // END BANTER COMPILED CODE 
    }
}