using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
// using GLTFast;
using TMPro;
using Siccity.GLTFUtility;
using Unity.VisualScripting;
using UnityEngine;
using PropertyName = Banter.SDK.PropertyName;

namespace Banter.SDK
{

    /* 
    #### Banter GLTF
    Add a 3D Model from a glb file to the scene.

    **Properties**
    - `url` - The url of the glb file.
    - `generateMipMaps` - If mipmaps should be generated.
    - `addColliders` - If colliders should be added.
    - `nonConvexColliders` - If colliders should be non convex.
    - `slippery` - If colliders should be slippery.
    - `climbable` - If colliders should be climbable.
    - `legacyRotate` - If the model should be rotated to match the old GLTF forward direction.

    **Code Example**
    ```js
        const url = "https://example.com/model.glb";
        const generateMipMaps = false;
        const addColliders = false;
        const nonConvexColliders = false;
        const slippery = false;
        const climbable = false;
        const legacyRotate = false;
        const gameObject = new BS.GameObject("MyGLTF"); 
        const gltf = await gameObject.AddComponent(new BS.BanterGLTF(url, generateMipMaps, addColliders, nonConvexColliders, slippery, climbable, legacyRotate));
    ```
    */
    [WatchComponent]
    [RequireComponent(typeof(BanterObjectId))]

    public class BanterGLTF : BanterComponentBase
    {
        [See(initial = "")] public string url;
        [See(initial = "false")] public bool generateMipMaps;
        [See(initial = "false")] public bool addColliders;
        [See(initial = "false")] public bool nonConvexColliders;
        [See(initial = "false")] public bool slippery;
        [See(initial = "false")] public bool climbable;
        // This has been added because unity changed the forward direction of GLTF fast between version 3 and 4. 
        // https://docs.unity3d.com/Packages/com.unity.cloud.gltfast@5.0/manual/UpgradeGuides.html#upgrade-to-4x
        // We decided to leave legacy aframe stuff on the old one by default for compatibility reasons, but 
        // that new stuff will use the forward direction of the new version by default. Aframe shim will set 
        // this to true automaticaly.
        [See(initial = "false")] public bool legacyRotate;
        bool loadStarted;

        public override void StartStuff()
        {
            LogLine.Do("Warning: Using BanterGLTF is not recommended for production use. It is slow and not optimized.");
            SetupGLTF();
        }
        async void SetupGLTF()
        {
            if (loadStarted)
            {
                return;
            }
            loadStarted = true;
            try
            {
                Importer.ImportGLBAsync(await Get.Bytes(url), new ImportSettings(), (go, animations) =>
                {
                    try
                    {
                        var comp = this;
                        if (comp == null || gameObject == null)
                        {
                            LogLine.Do("GameObject/Component was destroyed before GLTF was loaded, killing the GLTF.");
                            Destroy(go);
                            return;
                        }
                        if (transform.childCount > 0)
                        {
                            Destroy(transform.GetChild(0).gameObject);
                        }
                        go.transform.SetParent(transform, false);
                        go.transform.name = Path.GetFileNameWithoutExtension(url);

                        Traverse.Do(go.transform, t =>
                        {
                            var collider = t.name.Contains("sq-collider");
                            var nonconvexcollider = t.name.Contains("sq-nonconvexcollider");
                            if (collider || nonconvexcollider && (t.gameObject.GetComponent<Renderer>() != null))
                            {
                                t.gameObject.AddComponent<BanterObjectId>();
                                try
                                {
                                    var col = t.gameObject.AddComponent<BanterMeshCollider>();
                                    if (nonconvexcollider)
                                    {
                                        col.convex = false;
                                    }
                                }
                                catch (Exception)
                                {
                                    // Debug.LogError(e);
                                }
                            }
                            var climbable = t.name.Contains("sq-climbable");
                            if (climbable)
                            {
                                t.gameObject.layer = 20;
                            }
                        });


                        if (legacyRotate)
                        {
                            var child = go.transform;
                            child.RotateAround(child.position, child.up, 180f);
                        }
                        if (addColliders || nonConvexColliders || slippery)
                        {
                            foreach (var mf in GetComponentsInChildren<MeshFilter>())
                            {
                                if (mf.sharedMesh == null)
                                {
                                    continue;
                                }
                                var collider = mf.AddComponent<MeshCollider>();
                                collider.convex = !nonConvexColliders;
                                collider.sharedMesh = mf.sharedMesh;
                                if (climbable)
                                {
                                    mf.gameObject.layer = 20;
                                }
                                if (slippery)
                                {
                                    collider.material = new PhysicMaterial()
                                    {
                                        dynamicFriction = 0,
                                        staticFriction = 0
                                    };
                                }
                                // need to add support fo sq-overridecolor default setting maybe? 
                            }
                        }
                        SetLoadedIfNot();
                    }
                    catch (Exception e)
                    {
                        SetLoadedIfNot(false, e.Message + " - " + url);
                        Destroy(go);
                    }
                });
            }
            catch (Exception e)
            {
                Debug.LogError(e + " " + url);
                SetLoadedIfNot(false, e.Message);
            }
        }
        public override void DestroyStuff() { }
        public void UpdateCallback(List<PropertyName> changedProperties)
        {
            SetupGLTF();
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
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.url, PropertyName.generateMipMaps, PropertyName.addColliders, PropertyName.nonConvexColliders, PropertyName.slippery, PropertyName.climbable, PropertyName.legacyRotate, };
            UpdateCallback(changedProperties);
        }


        public override void Init()
        {
            scene = BanterScene.Instance();
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(gameObject.GetInstanceID(), GetInstanceID(), ComponentType.BanterGLTF);


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
                    var valurl = (BanterString)values[i];
                    if (valurl.n == PropertyName.url)
                    {
                        url = valurl.x;
                        changedProperties.Add(PropertyName.url);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valgenerateMipMaps = (BanterBool)values[i];
                    if (valgenerateMipMaps.n == PropertyName.generateMipMaps)
                    {
                        generateMipMaps = valgenerateMipMaps.x;
                        changedProperties.Add(PropertyName.generateMipMaps);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valaddColliders = (BanterBool)values[i];
                    if (valaddColliders.n == PropertyName.addColliders)
                    {
                        addColliders = valaddColliders.x;
                        changedProperties.Add(PropertyName.addColliders);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valnonConvexColliders = (BanterBool)values[i];
                    if (valnonConvexColliders.n == PropertyName.nonConvexColliders)
                    {
                        nonConvexColliders = valnonConvexColliders.x;
                        changedProperties.Add(PropertyName.nonConvexColliders);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valslippery = (BanterBool)values[i];
                    if (valslippery.n == PropertyName.slippery)
                    {
                        slippery = valslippery.x;
                        changedProperties.Add(PropertyName.slippery);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valclimbable = (BanterBool)values[i];
                    if (valclimbable.n == PropertyName.climbable)
                    {
                        climbable = valclimbable.x;
                        changedProperties.Add(PropertyName.climbable);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var vallegacyRotate = (BanterBool)values[i];
                    if (vallegacyRotate.n == PropertyName.legacyRotate)
                    {
                        legacyRotate = vallegacyRotate.x;
                        changedProperties.Add(PropertyName.legacyRotate);
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
                    name = PropertyName.url,
                    type = PropertyType.String,
                    value = url,
                    componentType = ComponentType.BanterGLTF,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.generateMipMaps,
                    type = PropertyType.Bool,
                    value = generateMipMaps,
                    componentType = ComponentType.BanterGLTF,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.addColliders,
                    type = PropertyType.Bool,
                    value = addColliders,
                    componentType = ComponentType.BanterGLTF,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.nonConvexColliders,
                    type = PropertyType.Bool,
                    value = nonConvexColliders,
                    componentType = ComponentType.BanterGLTF,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.slippery,
                    type = PropertyType.Bool,
                    value = slippery,
                    componentType = ComponentType.BanterGLTF,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.climbable,
                    type = PropertyType.Bool,
                    value = climbable,
                    componentType = ComponentType.BanterGLTF,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.legacyRotate,
                    type = PropertyType.Bool,
                    value = legacyRotate,
                    componentType = ComponentType.BanterGLTF,
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