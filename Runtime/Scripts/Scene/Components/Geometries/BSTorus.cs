using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BS
{
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BSObjectId))]
    [WatchComponent]
    public partial class BSTorus : BSComponentBase
    {
        [Tooltip("The overall outer radius of the torus, tube included.")]
        [See(initial = "1")][SerializeField] internal float radius = 1;
        [Tooltip("The radius of the tube.")]
        [See(initial = "1")][SerializeField] internal float tube = 1;

        [Tooltip("Number of radial segments")]
        [See(initial = "8")][SerializeField] internal int radialSegments = 8;
        [Tooltip("Number of tubular segments")]
        [See(initial = "6")][SerializeField] internal int tubularSegments = 6;
        [Tooltip("Arc length of the ring in radians")]
        [See(initial = "Math.PI * 2")][SerializeField] internal float arc = Mathf.PI * 2f;



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
            geometry.geometryType = GeometryType.TorusGeometry;
            geometry.tube = tube;
            geometry.radius = radius;
            geometry.radialSegments = radialSegments;
            geometry.tubularSegments = tubularSegments;
            geometry.arc = arc;

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