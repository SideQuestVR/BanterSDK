using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
namespace BS
{
    /* 
    #### Banter Collider Events
    Fire collision and trigger events on the object. This component is required for the `trigger-enter` etc events to work.

    **Code Example**
    ```js
        const gameObject = new BS.GameObject("MyBanterColliderEvents"); 
        const events = await gameObject.AddComponent(new BS.BSColliderEvents());
    ```
    */
    [RequireComponent(typeof(BSObjectId))]
    [WatchComponent]
    public partial class BSColliderEvents : BSComponentBase
    {
        void OnCollisionEnter(Collision collision)
        {
            BSScene.Instance().link?._OnCollisionEnter(gameObject, collision);
        }
        void OnCollisionExit(Collision collision)
        {
            BSScene.Instance().link?._OnCollisionExit(gameObject, collision);
        }
        void OnTriggerEnter(Collider collider)
        {
            BSScene.Instance().link?._OnTriggerEnter(gameObject, collider);
        }
        void OnTriggerExit(Collider collider)
        {
            BSScene.Instance().link?._OnTriggerExit(gameObject, collider);
        }
        internal override void DestroyStuff() { }

        internal override void UpdateStuff()
        {

        }
        internal void UpdateCallback(List<PropertyName> changedProperties) { }
        internal override void StartStuff()
        {
            SetLoadedIfNot();
        }
    }
}