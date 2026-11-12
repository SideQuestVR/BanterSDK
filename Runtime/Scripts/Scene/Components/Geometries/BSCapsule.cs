using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BS
{
    /*
    #### Banter Capsule
    A capsule: a cylinder with hemispherical caps.

    **Properties**
    - `radius` - The overall bounding radius. The caps are half this, so the default 0.5/1 capsule is not a plain sphere.
    - `height` - The total height, caps included.
    - `radialSegments` - The number of radial segments to divide the shape into.
    - `heightSegments` - Doubles as the cap resolution; 1 is treated as 8 so the caps are not two-ring hemispheres.
    */
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BSObjectId))]
    [WatchComponent]
    public partial class BSCapsule : BSComponentBase
    {
        [Tooltip("The overall bounding radius. The caps are half this, so the default 0.5/1 capsule is not a plain sphere.")]
        [See(initial = "0.5")][SerializeField] internal float radius = 0.5f;
        [Tooltip("The total height, caps included.")]
        [See(initial = "1")][SerializeField] internal float height = 1f;
        [Tooltip("The number of radial segments to divide the shape into.")]
        [See(initial = "32")][SerializeField] internal int radialSegments = 32;
        [Tooltip("Doubles as the cap resolution; 1 is treated as 8 so the caps are not two-ring hemispheres.")]
        [See(initial = "1")][SerializeField] internal int heightSegments = 1;
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
            geometry.geometryType = GeometryType.CapsuleGeometry;
            geometry.radius = radius;
            geometry.height = height;
            geometry.radialSegments = radialSegments;
            geometry.heightSegments = heightSegments;
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