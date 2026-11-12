using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BS
{
    /* 
    #### Capsule Collider
    Add a capsule shaped physics collider to the object.

    **Properties**
    - `isTrigger` - If the collider is a trigger.
    - `radius` - The radius of the capsule.
    - `height` - The height of the capsule.

    **Code Example**
    ```js
        const isTrigger = false;
        const radius = 0.5;
        const height = 2;
        const gameObject = new GameObject("MyCapsuleCollider"); 
        const capsuleCollider = await gameObject.AddComponent(new BS.BSCapsuleCollider(isTrigger, radius, height));
    ```
    */
    [WatchComponent(typeof(CapsuleCollider))]
    [RequireComponent(typeof(CapsuleCollider))]
    [RequireComponent(typeof(BSObjectId))]
    public partial class BSCapsuleCollider : UnityComponentBase
    {
        [See(initial = "false")][SerializeField] internal bool isTrigger = false;
        [See(initial = "0.5")][SerializeField] internal float radius = 0.5f;
        [See(initial = "2")][SerializeField] internal float height = 2;
    }
}