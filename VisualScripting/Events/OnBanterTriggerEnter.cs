using System;
using Banter.SDK;
using Unity.VisualScripting;
using UnityEngine;

namespace Banter.VisualScripting
{
// #if MODULE_PHYSICS_EXISTS
    /// <summary>
    /// Called when a collider enters the trigger.
    /// </summary>
    public class OnBanterTriggerEnter : TriggerEventUnit
    {
        public override Type MessageListenerType => typeof(UnityOnTriggerEnterMessageListener);
        protected override string hookName => EventHooks.OnTriggerEnter;
        public new ValueOutput collider { get; private set; }
        public ValueOutput user { get; private set; }
        protected override void Definition()
        {
            base.Definition();

            collider = ValueOutput<Collider>(nameof(collider));

            user = ValueOutput<BanterUser>(nameof(user));
        }
        protected override void AssignArguments(Flow flow, Collider other)
        {
            flow.SetValue(collider, other);
            var user = other.gameObject.GetComponentInParent<UserData>();
            if (user != null)
            {
                flow.SetValue(this.user, new BanterUser() { name = user.name, id = user.id, uid = user.uid, color = user.color, isLocal = user.isLocal, isSpaceAdmin = user.isSpaceAdmin });
            }
        }
    }
// #endif
}