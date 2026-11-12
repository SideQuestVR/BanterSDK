using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BS
{
    /*
    #### Banter Extrude
    A 2D outline given thickness, optionally along a curve.

    **Properties**
    - `shapePoints` - The 2D outline and holes, as JSON. Without it there is no shape and no mesh is built.
    - `curvePoints` - An optional 3D path to extrude along. Empty extrudes straight along Z.
    - `depth` - How far to extrude when there is no extrude path.
    - `depthSegments` - Subdivisions along the extrusion axis (three.js calls this steps).
    - `segments` - How finely curves in the outline are sampled.
    */
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BSObjectId))]
    [WatchComponent]
    public partial class BSExtrude : BSComponentBase
    {
        [Tooltip("The 2D outline and holes, as JSON. Without it there is no shape and no mesh is built.")]
        [See(initial = "")][SerializeField] internal string shapePoints = "";
        [Tooltip("An optional 3D path to extrude along. Empty extrudes straight along Z.")]
        [See(initial = "")][SerializeField] internal string curvePoints = "";
        [Tooltip("How far to extrude when there is no extrude path.")]
        [See(initial = "1")][SerializeField] internal float depth = 1f;
        [Tooltip("Subdivisions along the extrusion axis (three.js calls this steps).")]
        [See(initial = "1")][SerializeField] internal int depthSegments = 1;
        [Tooltip("How finely curves in the outline are sampled.")]
        [See(initial = "32")][SerializeField] internal int segments = 32;
        internal override void StartStuff()
        {
            SetupGeometry();
            SetLoadedIfNot();
        }

        internal override void UpdateStuff()
        {

        }

        void SetupGeometry()
        {
            var geometry = GetComponent<BSGeometry>();
            if (geometry == null)
            {
                geometry = gameObject.AddComponent<BSGeometry>();
            }
            geometry.geometryType = GeometryType.ExtrudeGeometry;
            geometry.shapePoints = shapePoints;
            geometry.curvePoints = curvePoints;
            geometry.depth = depth;
            geometry.depthSegments = depthSegments;
            geometry.segments = segments;
            // Every call, not only when the geometry was just added: this also runs for each
            // property change, and the mesh has to follow. Unchanged values hit BSGeometry's cache.
            geometry.SetGeometry();
        }

        internal override void DestroyStuff()
        {
            var geometry = GetComponent<BSGeometry>();
            if (geometry)
            {
                Destroy(geometry);
            }
        }
        internal void UpdateCallback(List<PropertyName> changedProperties)
        {
            SetupGeometry();
        }
    }
}