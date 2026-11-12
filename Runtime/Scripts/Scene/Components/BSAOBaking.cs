using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BS
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
        wall.AddComponent(new BS.BSGeometry({type: "cube"}));

        // Add AO baking component to parent
        const aoBaker = await parent.AddComponent(new BS.BSAOBaking(
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
    [RequireComponent(typeof(BSObjectId))]
    [WatchComponent]
    public partial class BSAOBaking : BSComponentBase
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
                    Debug.LogWarning($"[BSAOBaking] Shader not found: {targetShaderName}");
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
    }
}