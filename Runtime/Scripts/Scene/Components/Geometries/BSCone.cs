using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BS
{
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BSObjectId))]
    [WatchComponent]
    public partial class BSCone : BSComponentBase
    {

        [Tooltip("Radius of the cone")]
        [See(initial = "1")][SerializeField] internal float radius = 1;
        [Tooltip("Height of the cone")]
        [See(initial = "1")][SerializeField] internal float height = 1;
        [Tooltip("Number of segments around the cone")]
        [See(initial = "32")][SerializeField] internal int radialSegments = 32;
        [Tooltip("Number of segments along the height of the cone")]
        [See(initial = "1")][SerializeField] internal int heightSegments = 1;
        [Tooltip("Whether the cone is open-ended")]
        [See(initial = "false")][SerializeField] internal bool openEnded = false;
        [Tooltip("Start angle of the cone in radians")]
        [See(initial = "0")][SerializeField] internal float thetaStart = 0f;
        [Tooltip("Angle length of the cone in radians")]
        [See(initial = "Math.PI * 2")][SerializeField] internal float thetaLength = Mathf.PI * 2f;



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
            geometry.geometryType = GeometryType.CylinderGeometry;
            geometry.radiusTop = 0;
            geometry.radiusBottom = radius;
            geometry.height = height;
            geometry.radialSegments = radialSegments;
            geometry.heightSegments = heightSegments;
            geometry.openEnded = openEnded;
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