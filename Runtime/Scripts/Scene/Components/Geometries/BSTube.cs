using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BS
{
    /*
    #### Banter Tube
    A tube swept along a 3D curve.

    **Properties**
    - `curvePoints` - The 3D curve to sweep along, as JSON. Without it there is no path and no mesh is built.
    - `radius` - The radius of the tube cross-section.
    - `tubularSegments` - The number of segments along the path.
    - `radialSegments` - The number of segments around the cross-section.
    */
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BSObjectId))]
    [WatchComponent]
    public partial class BSTube : BSComponentBase
    {
        [Tooltip("The 3D curve to sweep along, as JSON. Without it there is no path and no mesh is built.")]
        [See(initial = "")][SerializeField] internal string curvePoints = "";
        [Tooltip("The radius of the tube cross-section.")]
        [See(initial = "0.5")][SerializeField] internal float radius = 0.5f;
        [Tooltip("The number of segments along the path.")]
        [See(initial = "32")][SerializeField] internal int tubularSegments = 32;
        [Tooltip("The number of segments around the cross-section.")]
        [See(initial = "32")][SerializeField] internal int radialSegments = 32;
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
            geometry.geometryType = GeometryType.TubeGeometry;
            geometry.curvePoints = curvePoints;
            geometry.radius = radius;
            geometry.tubularSegments = tubularSegments;
            geometry.radialSegments = radialSegments;
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