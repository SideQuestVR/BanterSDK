using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BS
{
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BSObjectId))]
    [WatchComponent]
    public partial class BSHorn : BSComponentBase
    {
        [Tooltip("The number of stacks to divide the shape into.")]
        [See(initial = "5")][SerializeField] internal int stacks = 5;
        [Tooltip("The number of slices to divide the shape into.")]
        [See(initial = "5")][SerializeField] internal int slices = 5;

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
            geometry.geometryType = GeometryType.ParametricGeometry;
            geometry.parametricType = ParametricGeometryType.Horn;
            geometry.stacks = stacks;
            geometry.slices = slices;
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