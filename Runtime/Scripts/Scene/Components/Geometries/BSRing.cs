using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BS
{
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BSObjectId))]
    [WatchComponent]
    public partial class BSRing : BSComponentBase
    {
        [Tooltip("Radius of the inner circle")]
        [See(initial = "1")][SerializeField] internal float innerRadius = 1;
        [Tooltip("Radius of the outer circle")]
        [See(initial = "2")][SerializeField] internal float outerRadius = 2;
        [Tooltip("Number of segments in the theta direction")]
        [See(initial = "32")][SerializeField] internal int thetaSegments = 32;
        [Tooltip("Number of segments in the phi direction")]
        [See(initial = "1")][SerializeField] internal int phiSegments = 1;
        [Tooltip("Start angle in radians")]
        [See(initial = "0")][SerializeField] internal float thetaStart = 0;
        [Tooltip("Angle in radians")]
        [See(initial = "Math.PI * 2")][SerializeField] internal float thetaLength = Mathf.PI * 2;




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
            geometry.geometryType = GeometryType.RingGeometry;
            geometry.innerRadius = innerRadius;
            geometry.outerRadius = outerRadius;
            geometry.thetaSegments = thetaSegments;
            geometry.phiSegments = phiSegments;
            geometry.thetaStart = thetaStart;
            geometry.thetaLength = thetaLength;
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