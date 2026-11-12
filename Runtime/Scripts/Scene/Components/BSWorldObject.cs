using System;
using System.Collections;
using System.Collections.Generic;
using SideQuest.FlexaBody;
using UnityEngine;

namespace BS
{
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BSObjectId))]
    [WatchComponent]
    public partial class BSWorldObject : BSComponentBase
    {

        WorldObject worldObj;

        bool worldObjectAdded;

        internal override void DestroyStuff()
        {
            if (worldObjectAdded && worldObj)
            {
                Destroy(worldObj);
            }
        }

        internal override void StartStuff()
        {
            // FlexaBody's WorldObject in Banter, the SDK's port of it otherwise (Utils/FlexaBody).
            worldObj = GetComponent<WorldObject>();
            if (worldObj == null)
            {
                worldObjectAdded = true;
                worldObj = gameObject.AddComponent<WorldObject>();
            }
            worldObj.RB = GetComponent<Rigidbody>();
            SetLoadedIfNot();
        }

        internal void UpdateCallback(List<PropertyName> changedProperties)
        {
            // SetupPhysicMaterial(changedProperties);
        }

        internal override void UpdateStuff()
        {

        }
    }
}