using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BS
{
    /* 
    #### Mesh Collider
    Add a mesh shaped physics collider to the object. This requires a mesh to be supplied, or used from a MeshFilter component.

    **Properties**
    - `isTrigger` - If the collider is a trigger.
    - `convex` - If the collider is convex i.e. has no holes or concave parts.

    **Code Example**
    ```js
        const isTrigger = false;
        const convex = true;
        const gameObject = new BS.GameObject("MyMeshCollider"); 
        const meshCollider = await gameObject.AddComponent(new BS.BSMeshCollider(isTrigger, convex));
    ```
    */
    [WatchComponent(typeof(MeshCollider))]
    [RequireComponent(typeof(MeshCollider))]
    [RequireComponent(typeof(BSObjectId))]
    public partial class BSMeshCollider : UnityComponentBase
    {
        [See(initial = "false")][SerializeField] internal bool convex = false;
        [See(initial = "false")][SerializeField] internal bool isTrigger = false;
    }
}