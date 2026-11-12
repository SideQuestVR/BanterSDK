using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BS
{

    /* 
    #### Banter Billboard
    Make an object look at the player camera.

    **Properties**
    - `smoothing` - The smoothing of the billboard.
    - `enableXAxis` - Enable the X axis.
    - `enableYAxis` - Enable the Y axis.
    - `enableZAxis` - Enable the Z axis.

    **Code Example**
    ```js
        const smoothing = 0;
        const enableXAxis = true;
        const enableYAxis = true;
        const enableZAxis = true;
        const gameObject = new BS.GameObject("MyBillboard"); 
        const billBoard = await gameObject.AddComponent(new BS.BSBillboard(smoothing, enableXAxis, enableYAxis, enableZAxis));

    ```

    */
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BSObjectId))]
    [WatchComponent]
    public partial class BSBillboard : BSComponentBase
    {
        [Tooltip("Controls the smoothness of the billboard's rotation.")]
        [See(initial = "0")][SerializeField] internal float smoothing = 0;

        [Tooltip("Enable or disable the X axis rotation for the billboard.")]
        [See(initial = "true")][SerializeField] internal bool enableXAxis = true;

        [Tooltip("Enable or disable the Y axis rotation for the billboard.")]
        [See(initial = "true")][SerializeField] internal bool enableYAxis = true;

        [Tooltip("Enable or disable the Z axis rotation for the billboard.")]
        [See(initial = "true")][SerializeField] internal bool enableZAxis = true;

        internal override void StartStuff()
        {
            if (!valuesApplied)
            {
                // Placed in the Inspector, so nothing has added the FaceTarget yet (see valuesApplied).
                ReSetup();
            }
            SetLoadedIfNot();
        }

        internal override void UpdateStuff()
        {

        }

        internal override void DestroyStuff()
        {
            if (lookAt != null)
            {
                Destroy(lookAt);
                lookAt = null;
            }
        }
        FaceTarget lookAt;
        internal void UpdateCallback(List<PropertyName> changedProperties)
        {
            valuesApplied = true;
            if (lookAt == null)
            {
                // A copy made with Instantiate already carries its original's FaceTarget.
                lookAt = GetComponent<FaceTarget>();
            }
            if (lookAt == null)
            {
                lookAt = gameObject.AddComponent<FaceTarget>();
            }
            lookAt.smoothing = smoothing;
            lookAt.enableXAxis = enableXAxis;
            lookAt.enableYAxis = enableYAxis;
            lookAt.enableZAxis = enableZAxis;
            lookAt.isBillboard = true;
        }
    }
}