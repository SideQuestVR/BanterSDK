using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BS
{
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BSObjectId))]
    [WatchComponent]
    public partial class BSSphere : BSComponentBase
    {
        [Tooltip("Radius of the sphere")]
        [See(initial = "1")][SerializeField] internal float radius = 1;
        [Tooltip("Number of horizontal segments")]
        [See(initial = "16")][SerializeField] internal int widthSegments = 16;
        [Tooltip("Number of vertical segments")]
        [See(initial = "16")][SerializeField] internal int heightSegments = 16;
        [Tooltip("Start angle in radians for the horizontal segments")]
        [See(initial = "0")][SerializeField] internal float phiStart = 0;
        [Tooltip("Length of the horizontal segments in radians")]
        [See(initial = "Math.PI * 2")][SerializeField] internal float phiLength = Mathf.PI * 2;
        [Tooltip("Start angle in radians for the vertical segments")]
        [See(initial = "0")][SerializeField] internal float thetaStart = 0;
        [Tooltip("Length of the vertical segments in radians")]
        [See(initial = "Math.PI")][SerializeField] internal float thetaLength = Mathf.PI;

        internal override void StartStuff()
        {
            SetupGeometry();
            SetLoadedIfNot();
        }

        void SetupGeometry()
        {
            var geometry = GetComponent<BSGeometry>();
            if (geometry == null)
            {
                geometry = gameObject.AddComponent<BSGeometry>();
            }
            geometry.geometryType = GeometryType.SphereGeometry;
            geometry.radius = radius;
            geometry.widthSegments = widthSegments;
            geometry.heightSegments = heightSegments;
            geometry.phiStart = phiStart;
            geometry.phiLength = phiLength;
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

        internal override void UpdateStuff()
        {

        }
        internal void UpdateCallback(List<PropertyName> changedProperties)
        {
            SetupGeometry();
        }
    }
}