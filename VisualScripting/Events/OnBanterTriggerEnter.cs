using System;
using BS;
using Unity.VisualScripting;
using UnityEngine;

namespace BS.VisualScripting
{
    /// <summary>
    /// Called when a collider enters the trigger.
    /// </summary>
    [UnitTitle("On BS Trigger Enter Event Received")]
    [UnitShortTitle("On BS Trigger Enter")]
    [UnitCategory("Events\\BS\\Trigger")]
    [TypeIcon(typeof(BSObjectId))]
    public class OnBanterTriggerEnter : TriggerEventUnit
    {
        public override Type MessageListenerType => typeof(UnityOnTriggerEnterMessageListener);
        protected override string hookName => EventHooks.OnTriggerEnter;
        [DoNotSerialize]
        public ValueOutput user { get; private set; }
        protected override void Definition()
        {
            base.Definition();

            user = ValueOutput<BSUser>(nameof(user));
        }
        protected override void AssignArguments(Flow flow, Collider other)
        {
            base.AssignArguments(flow, other);
            var user = other.gameObject.GetComponentInParent<UserData>();
            if (user != null)
            {
                flow.SetValue(this.user, new BSUser() { name = user.name, id = user.id, uid = user.uid, color = user.color, isLocal = user.isLocal, isSpaceAdmin = user.isSpaceAdmin });
            }
            else
            {
                flow.SetValue(this.user, null);
            }
        }
    }
}
