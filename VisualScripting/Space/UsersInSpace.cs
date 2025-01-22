#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using System.Collections.Generic;

namespace Banter.VisualScripting
{
    [UnitTitle("Get Users in space")]
    [UnitShortTitle("Get Users")]
    [UnitCategory("Banter")]
    [TypeIcon(typeof(BanterObjectId))]
    public class GetUsers : Unit
    {
        [DoNotSerialize]
        public ValueOutput users;

        protected override void Definition()
        {
            List<BanterUser> AllUsers = new List<BanterUser>();
            users = ValueOutput<List<BanterUser>>("Users", flow => {
               foreach(UserData sceneUser in BanterScene.Instance().users)
                {
                    BanterUser currUser = new BanterUser(sceneUser.name, sceneUser.id, sceneUser.uid, sceneUser.color, sceneUser.isLocal, sceneUser.isSpaceAdmin);
                   

                    AllUsers.Add(currUser);

                }
                return AllUsers;
            });
        }
    }
}
#endif


