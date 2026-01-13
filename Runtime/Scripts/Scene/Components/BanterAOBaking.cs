using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Banter.SDK
{

    /*
    #### Banter AO Baking
    Merges child meshes and bakes ambient occlusion into vertex colors.

    **Properties**
    - `subdivisionLevel` - Subdivision level: 0=none, 1=4x, 2=16x, 3=64x triangles (0-3)
    - `sampleCount` - Number of ray samples per vertex (16-256)
    - `aoIntensity` - Intensity of the AO effect (0-2)
    - `aoBias` - Bias to avoid self-intersection (0.001-0.1)
    - `aoRadius` - How far to check for occlusion (0 = auto)
    - `hideSourceObjects` - Hide source objects after merging
    - `targetShaderName` - Shader name to apply (e.g., "Mobile/StylizedFakeLit")
    - `isProcessing` - Read-only, true during bake operation
    - `progress` - Read-only, bake progress (0-1)

    **Methods**
    - `BakeAO()` - Merge child meshes, subdivide, and bake AO (async)
    - `Preview()` - Merge without AO baking
    - `Clear()` - Remove generated mesh and show source objects

    **Code Example**
    ```js
        const parent = new BS.GameObject("MyBuilding");

        // Add child primitives...
        const wall = new BS.GameObject("Wall", parent);
        wall.AddComponent(new BS.BanterGeometry({type: "cube"}));

        // Add AO baking component to parent
        const aoBaker = await parent.AddComponent(new BS.BanterAOBaking(
            2,      // subdivisionLevel
            128,    // sampleCount
            1.2,    // aoIntensity
            0.005,  // aoBias
            0,      // aoRadius (0 = auto)
            true,   // hideSourceObjects
            "Mobile/StylizedFakeLit"  // targetShaderName
        ));

        // Bake AO
        aoBaker.BakeAO();
        console.log("AO baking started!");
    ```

    */
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BanterObjectId))]
    [WatchComponent]
    public class BanterAOBaking : BanterComponentBase
    {
        [Tooltip("Subdivision level: 0=none, 1=4x, 2=16x, 3=64x triangles")]
        [See(initial = "1")][SerializeField] internal int subdivisionLevel = 1;

        [Tooltip("Number of ray samples per vertex (higher = better quality, slower)")]
        [See(initial = "64")][SerializeField] internal int sampleCount = 64;

        [Tooltip("Intensity of the AO effect")]
        [See(initial = "1")][SerializeField] internal float aoIntensity = 1f;

        [Tooltip("Bias to avoid self-intersection")]
        [See(initial = "0.005")][SerializeField] internal float aoBias = 0.005f;

        [Tooltip("How far to check for occlusion (0 = auto based on mesh size)")]
        [See(initial = "0")][SerializeField] internal float aoRadius = 0f;

        [Tooltip("Hide source objects after merging")]
        [See(initial = "true")][SerializeField] internal bool hideSourceObjects = true;

        [Tooltip("Shader name to apply to merged meshes (e.g., 'Mobile/StylizedFakeLit')")]
        [See(initial = "\"Mobile/StylizedFakeLit\"")][SerializeField] internal string targetShaderName = "Mobile/StylizedFakeLit";

        [Tooltip("True when bake operation is in progress (read-only)")]
        [See(initial = "false")][SerializeField] internal bool isProcessing = false;

        [Tooltip("Bake progress from 0-1 (read-only)")]
        [See(initial = "0")][SerializeField] internal float progress = 0f;

        private PrimitiveMergerAO _merger;

        internal override void StartStuff()
        {
            SetLoadedIfNot();
        }

        internal override void UpdateStuff()
        {

        }

        internal override void DestroyStuff()
        {
            if (_merger != null)
            {
                _merger.ClearGeneratedMesh();
            }
        }

        internal void UpdateCallback(List<PropertyName> changedProperties)
        {
            // Properties are applied when methods are called
        }

        private void SetupMerger()
        {
            _merger = GetComponent<PrimitiveMergerAO>();
            if (_merger == null)
            {
                _merger = gameObject.AddComponent<PrimitiveMergerAO>();
            }

            _merger.useChildren = true;
            _merger.subdivisionLevel = Mathf.Clamp(subdivisionLevel, 0, 3);
            _merger.sampleCount = Mathf.Clamp(sampleCount, 16, 256);
            _merger.aoIntensity = Mathf.Clamp(aoIntensity, 0f, 2f);
            _merger.aoBias = Mathf.Clamp(aoBias, 0.001f, 0.1f);
            _merger.aoRadius = Mathf.Max(0f, aoRadius);
            _merger.hideSourceObjects = hideSourceObjects;

            if (!string.IsNullOrEmpty(targetShaderName))
            {
                var shader = Shader.Find(targetShaderName);
                if (shader != null)
                {
                    _merger.targetShader = shader;
                }
                else
                {
                    Debug.LogWarning($"[BanterAOBaking] Shader not found: {targetShaderName}");
                }
            }
        }

        private IEnumerator BakeAOCoroutine()
        {
            SetupMerger();
            isProcessing = true;
            progress = 0f;
            SyncProperties(true);

            yield return _merger.BakeAOAsync(() =>
            {
                isProcessing = false;
                progress = 1f;
                SyncProperties(true);
            });
        }

        /// <summary>
        /// Merge child meshes, subdivide, and bake ambient occlusion (async)
        /// </summary>
        [Method]
        public void _BakeAO()
        {
            StartCoroutine(BakeAOCoroutine());
        }

        /// <summary>
        /// Preview merge without baking AO
        /// </summary>
        [Method]
        public void _Preview()
        {
            SetupMerger();
            _merger.PreviewMerge();
        }

        /// <summary>
        /// Remove generated mesh and show source objects
        /// </summary>
        [Method]
        public void _Clear()
        {
            if (_merger != null)
            {
                _merger.ClearGeneratedMesh();
            }
        }
        // BANTER COMPILED CODE 
        public System.Int32 SubdivisionLevel { get { return subdivisionLevel; } set { subdivisionLevel = value; UpdateCallback(new List<PropertyName> { PropertyName.subdivisionLevel }); } }
        public System.Int32 SampleCount { get { return sampleCount; } set { sampleCount = value; UpdateCallback(new List<PropertyName> { PropertyName.sampleCount }); } }
        public System.Single AoIntensity { get { return aoIntensity; } set { aoIntensity = value; UpdateCallback(new List<PropertyName> { PropertyName.aoIntensity }); } }
        public System.Single AoBias { get { return aoBias; } set { aoBias = value; UpdateCallback(new List<PropertyName> { PropertyName.aoBias }); } }
        public System.Single AoRadius { get { return aoRadius; } set { aoRadius = value; UpdateCallback(new List<PropertyName> { PropertyName.aoRadius }); } }
        public System.Boolean HideSourceObjects { get { return hideSourceObjects; } set { hideSourceObjects = value; UpdateCallback(new List<PropertyName> { PropertyName.hideSourceObjects }); } }
        public System.String TargetShaderName { get { return targetShaderName; } set { targetShaderName = value; UpdateCallback(new List<PropertyName> { PropertyName.targetShaderName }); } }
        public System.Boolean IsProcessing { get { return isProcessing; } set { isProcessing = value; UpdateCallback(new List<PropertyName> { PropertyName.isProcessing }); } }
        public System.Single Progress { get { return progress; } set { progress = value; UpdateCallback(new List<PropertyName> { PropertyName.progress }); } }

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
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.subdivisionLevel, PropertyName.sampleCount, PropertyName.aoIntensity, PropertyName.aoBias, PropertyName.aoRadius, PropertyName.hideSourceObjects, PropertyName.targetShaderName, PropertyName.isProcessing, PropertyName.progress, };
            UpdateCallback(changedProperties);
        }
        internal override string GetSignature()
        {
            return "BanterAOBaking" +  PropertyName.subdivisionLevel + subdivisionLevel + PropertyName.sampleCount + sampleCount + PropertyName.aoIntensity + aoIntensity + PropertyName.aoBias + aoBias + PropertyName.aoRadius + aoRadius + PropertyName.hideSourceObjects + hideSourceObjects + PropertyName.targetShaderName + targetShaderName + PropertyName.isProcessing + isProcessing + PropertyName.progress + progress;
        }

        internal override void Init(List<object> constructorProperties = null)
        {
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(gameObject.GetInstanceID(), GetInstanceID(), ComponentType.BanterAOBaking);


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

        void BakeAO()
        {
            _BakeAO();
        }
        void Preview()
        {
            _Preview();
        }
        void Clear()
        {
            _Clear();
        }
        internal override object CallMethod(string methodName, List<object> parameters)
        {

            if (methodName == "BakeAO" && parameters.Count == 0)
            {
                BakeAO();
                return null;
            }
            else if (methodName == "Preview" && parameters.Count == 0)
            {
                Preview();
                return null;
            }
            else if (methodName == "Clear" && parameters.Count == 0)
            {
                Clear();
                return null;
            }
            else
            {
                return null;
            }
        }

        internal override void Deserialise(List<object> values)
        {
            List<PropertyName> changedProperties = new List<PropertyName>();
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] is BanterInt)
                {
                    var valsubdivisionLevel = (BanterInt)values[i];
                    if (valsubdivisionLevel.n == PropertyName.subdivisionLevel)
                    {
                        subdivisionLevel = valsubdivisionLevel.x;
                        changedProperties.Add(PropertyName.subdivisionLevel);
                    }
                }
                if (values[i] is BanterInt)
                {
                    var valsampleCount = (BanterInt)values[i];
                    if (valsampleCount.n == PropertyName.sampleCount)
                    {
                        sampleCount = valsampleCount.x;
                        changedProperties.Add(PropertyName.sampleCount);
                    }
                }
                if (values[i] is BanterFloat)
                {
                    var valaoIntensity = (BanterFloat)values[i];
                    if (valaoIntensity.n == PropertyName.aoIntensity)
                    {
                        aoIntensity = valaoIntensity.x;
                        changedProperties.Add(PropertyName.aoIntensity);
                    }
                }
                if (values[i] is BanterFloat)
                {
                    var valaoBias = (BanterFloat)values[i];
                    if (valaoBias.n == PropertyName.aoBias)
                    {
                        aoBias = valaoBias.x;
                        changedProperties.Add(PropertyName.aoBias);
                    }
                }
                if (values[i] is BanterFloat)
                {
                    var valaoRadius = (BanterFloat)values[i];
                    if (valaoRadius.n == PropertyName.aoRadius)
                    {
                        aoRadius = valaoRadius.x;
                        changedProperties.Add(PropertyName.aoRadius);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valhideSourceObjects = (BanterBool)values[i];
                    if (valhideSourceObjects.n == PropertyName.hideSourceObjects)
                    {
                        hideSourceObjects = valhideSourceObjects.x;
                        changedProperties.Add(PropertyName.hideSourceObjects);
                    }
                }
                if (values[i] is BanterString)
                {
                    var valtargetShaderName = (BanterString)values[i];
                    if (valtargetShaderName.n == PropertyName.targetShaderName)
                    {
                        targetShaderName = valtargetShaderName.x;
                        changedProperties.Add(PropertyName.targetShaderName);
                    }
                }
                if (values[i] is BanterBool)
                {
                    var valisProcessing = (BanterBool)values[i];
                    if (valisProcessing.n == PropertyName.isProcessing)
                    {
                        isProcessing = valisProcessing.x;
                        changedProperties.Add(PropertyName.isProcessing);
                    }
                }
                if (values[i] is BanterFloat)
                {
                    var valprogress = (BanterFloat)values[i];
                    if (valprogress.n == PropertyName.progress)
                    {
                        progress = valprogress.x;
                        changedProperties.Add(PropertyName.progress);
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
                    name = PropertyName.subdivisionLevel,
                    type = PropertyType.Int,
                    value = subdivisionLevel,
                    componentType = ComponentType.BanterAOBaking,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.sampleCount,
                    type = PropertyType.Int,
                    value = sampleCount,
                    componentType = ComponentType.BanterAOBaking,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.aoIntensity,
                    type = PropertyType.Float,
                    value = aoIntensity,
                    componentType = ComponentType.BanterAOBaking,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.aoBias,
                    type = PropertyType.Float,
                    value = aoBias,
                    componentType = ComponentType.BanterAOBaking,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.aoRadius,
                    type = PropertyType.Float,
                    value = aoRadius,
                    componentType = ComponentType.BanterAOBaking,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.hideSourceObjects,
                    type = PropertyType.Bool,
                    value = hideSourceObjects,
                    componentType = ComponentType.BanterAOBaking,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.targetShaderName,
                    type = PropertyType.String,
                    value = targetShaderName,
                    componentType = ComponentType.BanterAOBaking,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.isProcessing,
                    type = PropertyType.Bool,
                    value = isProcessing,
                    componentType = ComponentType.BanterAOBaking,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BanterComponentPropertyUpdate()
                {
                    name = PropertyName.progress,
                    type = PropertyType.Float,
                    value = progress,
                    componentType = ComponentType.BanterAOBaking,
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