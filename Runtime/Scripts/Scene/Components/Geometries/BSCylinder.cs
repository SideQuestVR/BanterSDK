using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BS
{
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BSObjectId))]
    [WatchComponent]
    public partial class BSCylinder : BSComponentBase
    {
        [Tooltip("Radius of the top of the cylinder")]
        [See(initial = "1")][SerializeField] internal float radiusTop = 1;
        [Tooltip("Radius of the bottom of the cylinder")]
        [See(initial = "1")][SerializeField] internal float radiusBottom = 1;
        [Tooltip("Height of the cylinder")]
        [See(initial = "1")][SerializeField] internal float height = 1;
        [Tooltip("Number of segments around the cylinder")]
        [See(initial = "32")][SerializeField] internal int radialSegments = 32;
        [Tooltip("Number of segments along the height of the cylinder")]
        [See(initial = "1")][SerializeField] internal int heightSegments = 1;
        [Tooltip("Whether the cylinder is open-ended")]
        [See(initial = "false")][SerializeField] internal bool openEnded = false;
        [Tooltip("Start angle of the cylinder in radians")]
        [See(initial = "0")][SerializeField] internal float thetaStart = 0;
        [Tooltip("Angle length of the cylinder in radians")]
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
            geometry.radiusTop = radiusTop;
            geometry.radiusBottom = radiusBottom;
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
            var material = GetComponent<BSMaterial>();
            if (material)
            {
                Destroy(material);
            }

        }
        internal void UpdateCallback(List<PropertyName> changedProperties)
        {
            SetupGeometry();
        }
    }
}