#if BANTER_VISUAL_SCRIPTING
using System.Linq;
using UnityEngine;
using Unity.VisualScripting;
using Banter.SDK;
using System.Data.Common;

namespace Banter.VisualScripting
{
    [UnitTitle("Get Users")]
    [UnitShortTitle("Get Users")]
    [UnitCategory("Banter\\Space")]
    [TypeIcon(typeof(BanterObjectId))]
    public class GetUsers : Unit
    {
        [DoNotSerialize]
        public ValueOutput info;

        protected override void Definition()
        {
            info = ValueOutput("Name", (f) => {
                var data = BanterScene.Instance().users;
                if (data == null)
                {
                    return null;
                }

                BanterUser[] users = new BanterUser[data.Count];
                for (var i = 0; i < data.Count; i++)
                {
                    users[i] = new BanterUser()
                    {
                        name = data[i].name,
                        id = data[i].id,
                        uid = data[i].uid,
                        color = data[i].color,
                        isLocal = data[i].isLocal,
                        isSpaceAdmin = data[i].isSpaceAdmin,
                    };
                }

                return users;
            });
        }
    }
}
#endif
