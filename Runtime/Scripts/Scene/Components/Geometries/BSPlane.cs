using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BS
{
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BSObjectId))]
    [WatchComponent]
    public partial class BSPlane : BSComponentBase
    {
        [Tooltip("The width of the plane.")]
        [See(initial = "1")][SerializeField] internal float width = 1;
        [Tooltip("The height of the plane.")]
        [See(initial = "1")][SerializeField] internal float height = 1;
        [Tooltip("The number of width segments to divide the plane into.")]
        [See(initial = "1")][SerializeField] internal int widthSegments = 1;
        [Tooltip("The number of height segments to divide the plane into.")]
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
            geometry.geometryType = GeometryType.PlaneGeometry;
            geometry.width = width;
            geometry.height = height;
            geometry.widthSegments = widthSegments;
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