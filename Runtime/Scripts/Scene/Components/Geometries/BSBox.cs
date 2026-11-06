using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BS
{
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BSObjectId))]
    [WatchComponent]
    public partial class BSBox : BSComponentBase
    {
        [Tooltip("The width of the box.")]
        [See(initial = "1")][SerializeField] internal float width = 1;
        [Tooltip("The height of the box.")]
        [See(initial = "1")][SerializeField] internal float height = 1;
        [Tooltip("The depth of the box.")]
        [See(initial = "1")][SerializeField] internal float depth = 1;
        [Tooltip("The number of width segments to divide the box into.")]
        [See(initial = "1")][SerializeField] internal int widthSegments = 1;
        [Tooltip("The number of height segments to divide the box into.")]
        [See(initial = "1")][SerializeField] internal int heightSegments = 1;
        [Tooltip("The number of depth segments to divide the box into.")]
        [See(initial = "1")][SerializeField] internal int depthSegments = 1;
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
            geometry.geometryType = GeometryType.BoxGeometry;
            geometry.width = width;
            geometry.height = height;
            geometry.depth = depth;
            geometry.widthSegments = widthSegments;
            geometry.heightSegments = heightSegments;
            geometry.depthSegments = depthSegments;
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