using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.VisualScripting;
using BS;
using System.Data.Common;

namespace BS.VisualScripting
{
    [UnitTitle("Get Users")]
    [UnitShortTitle("Get Users")]
    [UnitCategory("BS\\Space")]
    [TypeIcon(typeof(BSObjectId))]
    public class GetUsers : Unit
    {
        [DoNotSerialize]
        public ValueOutput info;

        protected override void Definition()
        {
            info = ValueOutput("Users Array", (f) => {
                var data = BSScene.Instance().users;
                if (data == null)
                {
                    return null;
                }

                List<BSUser> users = new ();
                for (var i = 0; i < data.Count; i++)
                {
                    users.Add(new BSUser()
                    {
                        name = data[i].name,
                        id = data[i].id,
                        uid = data[i].uid,
                        color = data[i].color,
                        isLocal = data[i].isLocal,
                        isSpaceAdmin = data[i].isSpaceAdmin,
                    });
                }

                return users;
            });
        }
    }
}
