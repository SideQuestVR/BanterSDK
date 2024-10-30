#if BANTER_VISUAL_SCRIPTING
using System.Linq;
using UnityEngine;
using Unity.VisualScripting;
using Banter.SDK;
using System.Data.Common;

namespace Banter.VisualScripting
{
    [UnitTitle("Get User State")]
    [UnitShortTitle("User State")]
    [UnitCategory("Banter")]
    [TypeIcon(typeof(BanterObjectId))]
    public class GetUserState : Unit
    {
        [DoNotSerialize]
        public ValueInput idOrName;

        [DoNotSerialize]
        public ValueOutput userPosition;

        [DoNotSerialize]
        public ValueOutput userRotation;

        protected override void Definition()
        {
            idOrName = ValueInput("id, uid, or name", string.Empty);

            userPosition = ValueOutput("Position", (f) => GetUserDataTransform(f).position);
            userRotation = ValueOutput("Rotation", (f) => GetUserDataTransform(f).rotation);
        }

        private Transform GetUserDataTransform(Flow f)
        {
            var value = f.GetValue<string>(idOrName);
            var data = BanterScene.Instance().users.FirstOrDefault(user => user.id == value || user.name == value || user.uid == value);

            if (data == null)
            {
                LogLine.Err("User not found: " + value);
            }
            return (data != null) ? data.Head.transform : null;
        }
    }

    [UnitTitle("Get Local User State")]
    [UnitShortTitle("Local User State")]
    [UnitCategory("Banter")]
    [TypeIcon(typeof(BanterObjectId))]
    public class GetLocalUserState : Unit
    {
        [DoNotSerialize]
        public ValueOutput userPosition;

        [DoNotSerialize]
        public ValueOutput userRotation;

        protected override void Definition()
        {
            userPosition = ValueOutput("Position", (f) =>
            {
                var data = BanterScene.Instance().users.FirstOrDefault(user => user.isLocal);
                return (data != null) ? data.Head.transform.position : Vector3.zero;
            });

            userRotation = ValueOutput("Rotation", (f) =>
            {
                var data = BanterScene.Instance().users.FirstOrDefault(user => user.isLocal);
                return (data != null) ? data.Head.transform.rotation : Quaternion.identity;
            });
        }
    }
}
#endif
