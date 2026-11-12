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
        /// <summary>Whether the collider belongs to a player; when it doesn't, `user` is an empty user.</summary>
        [DoNotSerialize]
        public ValueOutput isUser { get; private set; }
        protected override void Definition()
        {
            base.Definition();

            user = ValueOutput<BSUser>(nameof(user));
            isUser = ValueOutput<bool>(nameof(isUser));
        }
        protected override void AssignArguments(Flow flow, Collider other)
        {
            base.AssignArguments(flow, other);
            // Not just GetComponentInParent: Greenfield's local UserData isn't above the rig's colliders.
            var user = BSScene.Instance().GetUserFromCollider(other);
            if (user != null)
            {
                flow.SetValue(this.user, new BSUser() { name = user.name, id = user.id, uid = user.uid, color = user.color, isLocal = user.isLocal, isSpaceAdmin = user.isSpaceAdmin });
            }
            else
            {
                // Null, as always, for a collider that isn't a player: graphs test that ("isUser" now says it too). Not
                // an empty user: uid "" means the local player to the user-state nodes.
                flow.SetValue(this.user, null);
            }
            flow.SetValue(this.isUser, user != null);
        }
    }
}
