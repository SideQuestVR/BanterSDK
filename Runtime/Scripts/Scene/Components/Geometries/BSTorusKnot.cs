using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BS
{
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BSObjectId))]
    [WatchComponent]
    public partial class BSTorusKnot : BSComponentBase
    {
        [Tooltip("The overall radius of the knot, tube included.")]
        [See(initial = "0.5")][SerializeField] internal float radius = 0.5f;
        [Tooltip("The radius of the tube.")]
        [See(initial = "0.4")][SerializeField] internal float tube = 0.4f;

        [Tooltip("Number of radial segments")]
        [See(initial = "8")][SerializeField] internal int radialSegments = 8;
        [Tooltip("Number of tubular segments")]
        [See(initial = "64")][SerializeField] internal int tubularSegments = 64;
        [Tooltip("The number of p segments to divide the shape into.")]
        [See(initial = "2")][SerializeField] internal int p = 2;
        [Tooltip("The number of q segments to divide the shape into.")]
        [See(initial = "3")][SerializeField] internal int q = 3;


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
            geometry.geometryType = GeometryType.TorusKnotGeometry;
            geometry.tube = tube;
            geometry.radius = radius;
            geometry.radialSegments = radialSegments;
            geometry.tubularSegments = tubularSegments;
            geometry.p = p;
            geometry.q = q;

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