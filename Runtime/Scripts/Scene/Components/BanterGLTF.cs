using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Siccity.GLTFUtility;
using System.Threading.Tasks;

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
    [DefaultExecutionOrder(-1)]
    [WatchComponent]
    [RequireComponent(typeof(BanterObjectId))]

    public class BanterGLTF : BanterComponentBase
    {
        [Tooltip("The URL of the GLB file to be loaded.")]
        [See(initial = "")][SerializeField] internal string url;

        [Tooltip("Enable to generate mipmaps for improved texture scaling.")]
        [See(initial = "false")][SerializeField] internal bool generateMipMaps;

        [Tooltip("Enable to automatically add colliders to the imported model.")]
        [See(initial = "false")][SerializeField] internal bool addColliders;

        [Tooltip("Enable to use non-convex colliders instead of convex ones.")]
        [See(initial = "false")][SerializeField] internal bool nonConvexColliders;

        [Tooltip("Enable to make colliders slippery (zero friction).")]
        [See(initial = "false")][SerializeField] internal bool slippery;

        [Tooltip("Enable to make the model's colliders climbable.")]
        [See(initial = "false")][SerializeField] internal bool climbable;
        // This has been added because unity changed the forward direction of GLTF fast between version 3 and 4. 
        // https://docs.unity3d.com/Packages/com.unity.cloud.gltfast@5.0/manual/UpgradeGuides.html#upgrade-to-4x
        // We decided to leave legacy aframe stuff on the old one by default for compatibility reasons, but 
        // that new stuff will use the forward direction of the new version by default. Aframe shim will set 
        // this to true automaticaly.
        [Tooltip("Enable to rotate the model for compatibility with legacy GLTF forward direction.")]
        [See(initial = "false")][SerializeField] internal bool legacyRotate;

        [Tooltip("Set child objects to a specific layer - 0 to disable.")]
        [See(initial = "0")][SerializeField] internal int childrenLayer;
        bool loadStarted;

         private static Dictionary<string, byte[]> gltfCache = new Dictionary<string, byte[]>();
        public static void ClearCache()
        {
            gltfCache.Clear();
        }

        private async Task<byte[]> GetCachedGLTF()
        {
            var signature = GetSignature();
            if (gltfCache.ContainsKey(signature))
            {
                return gltfCache[signature];
            }
            else
            {
                var gltf = await Get.Bytes(url);
                gltfCache.Add(signature, gltf);
                return gltf;
            }
        }

        internal override void StartStuff()
        {
            LogLine.Do("Warning: Using BanterGLTF is not recommended for production use. It is slow and not optimized.");
            SetupGLTF();
        }
        void KillGLTF(GameObject go)
        {
            foreach (Transform child in go.transform.GetComponentsInChildren<Transform>())
            {
                if (child != null && child.gameObject != null && child.gameObject != gameObject)
                {
                    var renderer = child.GetComponent<Renderer>();
                    if (renderer != null)
                    {
                        if (renderer.sharedMaterial.HasTexture("_MainTex")) {
                            var _MainTex = renderer.sharedMaterial.mainTexture;
                            if (_MainTex != null)
                            {
                                DestroyImmediate(_MainTex);
                            } 
                        }
                        DestroyImmediate(renderer.sharedMaterial);
                        renderer.sharedMaterial = null;
                    }
                    var filter = child.GetComponent<MeshFilter>();
                    if (filter != null && filter.sharedMesh != null)
                    {
                        DestroyImmediate(filter.sharedMesh);
                        filter.sharedMesh = null;
                    }
                }
            }
        }
        async void SetupGLTF()
        {
            if (loadStarted)
            {
                return;
            }
            _loaded = false;
            loadStarted = true;
            try
            {
                Importer.ImportGLBAsync(await GetCachedGLTF(), new ImportSettings(), (go, animations) =>
                {
                    try
                    {
                        foreach (var anim in animations)
                        {
                            DestroyImmediate(anim);
                        }
                        var comp = this;
                        if (comp == null || gameObject == null)
                        {
                            LogLine.Do("GameObject/Component was destroyed before GLTF was loaded, killing the GLTF.");
                            KillGLTF(go);
                            Destroy(go);
                            return;
                        }
                        if (transform.childCount > 0)
                        {
                            KillGLTF(transform.GetChild(0).gameObject);
                            Destroy(transform.GetChild(0).gameObject);
                        }
                        go.transform.SetParent(transform, false);
                        go.transform.name = Path.GetFileNameWithoutExtension(url);

                        Traverse.Do(go.transform, t =>
                        {
                            var collider = t.name.Contains("sq-collider");
                            var nonconvexcollider = t.name.Contains("sq-nonconvexcollider");
                            t.gameObject.layer = childrenLayer;
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
                                var collider = mf.gameObject.AddComponent<MeshCollider>();
                                collider.convex = !nonConvexColliders;
                                collider.sharedMesh = mf.sharedMesh;
                                if (climbable)
                                {
                                    mf.gameObject.layer = 20;
                                }
                                if (slippery)
                                {
                                    collider.material = new PhysicsMaterial()
                                    {
                                        dynamicFriction = 0,
                                        staticFriction = 0
                                    };
                                }
                                // need to add support fo sq-overridecolor default setting maybe? 
                            }
                        }
                        SetLoadedIfNot();
                        loadStarted = false;
                    }
                    catch (Exception e)
                    {
                        SetLoadedIfNot(false, e.Message + " - " + url);
                        Destroy(go);
                        loadStarted = false;
                    }
                });
            }
            catch (Exception e)
            {
                Debug.LogError(e + " " + url);
                SetLoadedIfNot(false, e.Message);
                loadStarted = false;
            }
        }
        internal override void DestroyStuff()
        {
            if (gameObject)
            {
                KillGLTF(gameObject);
            }
        }

        internal override void UpdateStuff()
        {
            
        }
        internal void UpdateCallback(List<PropertyName> changedProperties)
        {
            SetupGLTF();
        }
        // BANTER COMPILED CODE 
        public System.String Url { get { return url; } set { url = value; UpdateCallback(new List<PropertyName> { PropertyName.url }); } }
        public System.Boolean GenerateMipMaps { get { return generateMipMaps; } set { generateMipMaps = value; UpdateCallback(new List<PropertyName> { PropertyName.generateMipMaps }); } }
        public System.Boolean AddColliders { get { return addColliders; } set { addColliders = value; UpdateCallback(new List<PropertyName> { PropertyName.addColliders }); } }
        public System.Boolean NonConvexColliders { get { return nonConvexColliders; } set { nonConvexColliders = value; UpdateCallback(new List<PropertyName> { PropertyName.nonConvexColliders }); } }
        public System.Boolean Slippery { get { return slippery; } set { slippery = value; UpdateCallback(new List<PropertyName> { PropertyName.slippery }); } }
        public System.Boolean Climbable { get { return climbable; } set { climbable = value; UpdateCallback(new List<PropertyName> { PropertyName.climbable }); } }
        public System.Boolean LegacyRotate { get { return legacyRotate; } set { legacyRotate = value; UpdateCallback(new List<PropertyName> { PropertyName.legacyRotate }); } }
        public System.Int32 ChildrenLayer { get { return childrenLayer; } set { childrenLayer = value; UpdateCallback(new List<PropertyName> { PropertyName.childrenLayer }); } }

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
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.url, PropertyName.generateMipMaps, PropertyName.addColliders, PropertyName.nonConvexColliders, PropertyName.slippery, PropertyName.climbable, PropertyName.legacyRotate, PropertyName.childrenLayer, };
            UpdateCallback(changedProperties);
        }
        internal override string GetSignature()
        {
            return "BanterGLTF" +  PropertyName.url + url + PropertyName.generateMipMaps + generateMipMaps + PropertyName.addColliders + addColliders + PropertyName.nonConvexColliders + nonConvexColliders + PropertyName.slippery + slippery + PropertyName.climbable + climbable + PropertyName.legacyRotate + legacyRotate + PropertyName.childrenLayer + childrenLayer;
        }

        internal override void Init(List<object> constructorProperties = null)
        {
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(gameObject.GetInstanceID(), GetInstanceID(), ComponentType.BanterGLTF);


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
                if (values[i] is BanterInt)
                {
                    var valchildrenLayer = (BanterInt)values[i];
                    if (valchildrenLayer.n == PropertyName.childrenLayer)
                    {
                        childrenLayer = valchildrenLayer.x;
                        changedProperties.Add(PropertyName.childrenLayer);
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
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.childrenLayer,
                    type = PropertyType.Int,
                    value = childrenLayer,
                    componentType = ComponentType.BanterGLTF,
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