using UnityEngine;
using Unity.VisualScripting;

namespace BS.VisualScripting
{
    /// <summary>
    /// The user a collider belongs to (a player's body or hands), or none: the same lookup the trigger and collision
    /// events use, so it finds remote players and the local player alike. The page gets the same as
    /// <c>e.detail.user</c> on its collision and trigger events.
    /// </summary>
    [UnitTitle("User From Collider")]
    [UnitShortTitle("User From Collider")]
    [UnitCategory("BS\\User")]
    [TypeIcon(typeof(BSObjectId))]
    public class UserFromCollider : Unit
    {
        [DoNotSerialize]
        public ValueInput collider;

        [DoNotSerialize]
        public ValueOutput isUser;

        [DoNotSerialize]
        public ValueOutput isLocalUser;

        [DoNotSerialize]
        public ValueOutput userInfo;

        [DoNotSerialize]
        public ValueOutput userData;

        protected override void Definition()
        {
            collider = ValueInput<Collider>("Collider", null);
            isUser = ValueOutput<bool>("Is User", flow => Find(flow) != null);
            // The local player's own body or hands: the check most trigger graphs want.
            isLocalUser = ValueOutput<bool>("Is Local User", flow => Find(flow) is UserData user && user.isLocal);
            userInfo = ValueOutput<BSUser>("User Info", flow =>
            {
                var user = Find(flow);
                return user == null ? null : new BSUser()
                {
                    name = user.name,
                    id = user.id,
                    uid = user.uid,
                    color = user.color,
                    isLocal = user.isLocal,
                    isSpaceAdmin = user.isSpaceAdmin,
                };
            });
            userData = ValueOutput<UserData>("User Data", Find);
            Requirement(collider, isUser);
            Requirement(collider, isLocalUser);
            Requirement(collider, userInfo);
            Requirement(collider, userData);
        }

        UserData Find(Flow flow)
        {
            var other = flow.GetValue<Collider>(collider);
            // Not just GetComponentInParent: the local player's UserData isn't above its rig's colliders.
            return other != null ? BSScene.Instance().GetUserFromCollider(other) : null;
        }
    }
}
