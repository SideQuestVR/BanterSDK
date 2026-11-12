using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BS
{
    /*
    #### Banter Lathe
    A surface of revolution: a 2D profile swept around the Y axis.

    **Properties**
    - `shapePoints` - The 2D half-profile to revolve, as JSON. Without it there is no shape and no mesh is built.
    - `segments` - How finely the profile itself is sampled.
    - `radialSegments` - The number of segments around the axis of revolution.
    - `phiStart` - The starting angle of the revolution.
    - `phiLength` - The swept angle. Less than a full turn leaves the solid open.
    */
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BSObjectId))]
    [WatchComponent]
    public partial class BSLathe : BSComponentBase
    {
        [Tooltip("The 2D half-profile to revolve, as JSON. Without it there is no shape and no mesh is built.")]
        [See(initial = "")][SerializeField] internal string shapePoints = "";
        [Tooltip("How finely the profile itself is sampled.")]
        [See(initial = "32")][SerializeField] internal int segments = 32;
        [Tooltip("The number of segments around the axis of revolution.")]
        [See(initial = "32")][SerializeField] internal int radialSegments = 32;
        [Tooltip("The starting angle of the revolution.")]
        [See(initial = "0")][SerializeField] internal float phiStart = 0f;
        [Tooltip("The swept angle. Less than a full turn leaves the solid open.")]
        [See(initial = "6.283185")][SerializeField] internal float phiLength = Mathf.PI * 2f;
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
            geometry.geometryType = GeometryType.LatheGeometry;
            geometry.shapePoints = shapePoints;
            geometry.segments = segments;
            geometry.radialSegments = radialSegments;
            geometry.phiStart = phiStart;
            geometry.phiLength = phiLength;
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