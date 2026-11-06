using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace BS
{
    /* 
    #### Banter Inverted Mesh
    Invert the mesh of a GameObject. This is useful for creating inverted colliders.

    **Code Example**
    ```js
        const gameObject = new BS.GameObject("MyInvertedMeshCollider"); 
        const invertedMesh = await gameObject.AddComponent(new BS.BSInvertedMesh());
    ```
    */
    [RequireComponent(typeof(BSObjectId))]
    [WatchComponent]
    public partial class BSInvertedMesh : BSComponentBase
    {
        internal override void DestroyStuff() { }
        internal void UpdateCallback(List<PropertyName> changedProperties) { }
        internal override void StartStuff()
        {
            var meshFilter = GetComponent<MeshFilter>();
            if (meshFilter != null)
            {
                var mesh = meshFilter.mesh;
                mesh.triangles = mesh.triangles.Reverse().ToArray();
                Debug.Log("Loaded inverted mesh");
                SetLoadedIfNot();
            }
        }
        internal override void UpdateStuff() { }
    }
}