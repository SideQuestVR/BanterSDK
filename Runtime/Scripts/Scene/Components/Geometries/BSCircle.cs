using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BS
{
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BSObjectId))]
    [WatchComponent]
    public partial class BSCircle : BSComponentBase
    {



        [Tooltip("The radius of the circle")]
        [See(initial = "1")][SerializeField] internal float radius = 1;
        [Tooltip("The number of segments that make up the circle")]
        [See(initial = "16")][SerializeField] internal int segments = 16;
        [Tooltip("The starting angle of the circle in radians")]
        [See(initial = "0")][SerializeField] internal float thetaStart = 0;
        [Tooltip("The length of the angle of the circle in radians")]
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
            geometry.geometryType = GeometryType.CircleGeometry;
            geometry.radius = radius;
            geometry.segments = segments;
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