using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BS
{
    /* 
    #### Sphere Collider
    Add a sphere shaped physics collider to the object.

    **Properties**
    - `isTrigger` - If the collider is a trigger.
    - `radius` - The radius of the sphere.

    **Code Example**
    ```js
        const isTrigger = false;
        const radius = 0.5;
        const gameObject = new BS.GameObject("MySphereCollider"); 
        const sphereCollider = await gameObject.AddComponent(new BS.BSSphereCollider(isTrigger, radius));
    ```
    */
    [WatchComponent(typeof(SphereCollider))]
    [RequireComponent(typeof(SphereCollider))]
    [RequireComponent(typeof(BSObjectId))]
    public partial class BSSphereCollider : UnityComponentBase
    {
        [See(initial = "false")][SerializeField] internal bool isTrigger = false;
        [See(initial = "0.5")][SerializeField] internal float radius = 0.5f;
    }
}