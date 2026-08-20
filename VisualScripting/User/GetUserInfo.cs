using System.Linq;
using UnityEngine;
using Unity.VisualScripting;
using BS;
using System.Data.Common;

namespace BS.VisualScripting
{
    [UnitTitle("Get User Info")]
    [UnitShortTitle("Get User")]
    [UnitCategory("BS\\User")]
    [TypeIcon(typeof(BSObjectId))]
    public class GetUserInfo : Unit
    {
        [DoNotSerialize]
        public ValueInput idOrName;

        [DoNotSerialize]
        public ValueOutput info;

        protected override void Definition()
        {
            idOrName = ValueInput("id, uid, or name", string.Empty);

            info = ValueOutput("Name", (f) => {
                var data = BSScene.Instance().users.FirstOrDefault(user => 
                {
                    var value = f.GetValue<string>(idOrName);

                    if (user.id == value || user.name == value || user.uid == value)
                    {
                        return true;
                    }
                    return false;
                });
                if (data == null)
                {
                    return null;
                }
                return new BSUser()
                {
                    name = data.name,
                    id = data.id,
                    uid = data.uid,
                    color = data.color,
                    isLocal = data.isLocal,
                    isSpaceAdmin = data.isSpaceAdmin,
                };
            });
        }
    }

    [UnitTitle("Get Local User Info")]
    [UnitShortTitle("Get Local User")]
    [UnitCategory("BS\\User")]
    [TypeIcon(typeof(BSObjectId))]
    public class GetLocalUserInfo : Unit
    {
        [DoNotSerialize]
        public ValueOutput info;

        protected override void Definition()
        {
            info = ValueOutput("User Info", (f) =>
            {
                var data = BSScene.Instance().users.FirstOrDefault(user => user.isLocal);
                if (data == null)
                {
                    return null;
                }
                return new BSUser()
                {
                    name = data.name,
                    id = data.id,
                    uid = data.uid,
                    color = data.color,
                    isLocal = data.isLocal,
                    isSpaceAdmin = data.isSpaceAdmin,
                };
            });
        }
    }
}
