using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BS
{
    /*
    #### Banter Dodecahedron
    A twelve-faced regular polyhedron.

    **Properties**
    - `radius` - The radius of the circumscribed sphere.
    - `detail` - Subdivision level. 0 is the plain solid; each step subdivides towards a sphere.
    */
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BSObjectId))]
    [WatchComponent]
    public partial class BSDodecahedron : BSComponentBase
    {
        [Tooltip("The radius of the circumscribed sphere.")]
        [See(initial = "0.5")][SerializeField] internal float radius = 0.5f;
        [Tooltip("Subdivision level. 0 is the plain solid; each step subdivides towards a sphere.")]
        [See(initial = "0")][SerializeField] internal float detail = 0f;
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
            geometry.geometryType = GeometryType.DodecahedronGeometry;
            geometry.radius = radius;
            geometry.detail = detail;
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