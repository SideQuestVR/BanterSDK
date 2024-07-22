#if BANTER_VISUAL_SCRIPTING
using System.Linq;
using UnityEngine;
using Unity.VisualScripting;
using Banter.SDK;

namespace Banter.VisualScripting
{
    [UnitTitle("Get User Info")]
    [UnitShortTitle("Get User")]
    [UnitCategory("Banter")]
    [TypeIcon(typeof(BanterObjectId))]
    public class GetUserInfo : Unit
    {
        [DoNotSerialize]
        public ValueInput idOrName;

        [DoNotSerialize]
        public ValueOutput name;

        [DoNotSerialize]
        public ValueOutput id;

        [DoNotSerialize]
        public ValueOutput uid;

        [DoNotSerialize]
        public ValueOutput color;

        [DoNotSerialize]
        public ValueOutput isLocal;

        protected override void Definition()
        {
            idOrName = ValueInput("id, uid, or name", string.Empty);

            name = ValueOutput("Name", (f) => GetUserData(f)?.name);
            id = ValueOutput("Id", (f) => GetUserData(f)?.id);
            uid = ValueOutput("Uid", (f) => GetUserData(f)?.uid);
            color = ValueOutput<Color>("Color", (f) =>
            {
                var returncol = GetUserData(f)?.color;
                if (ColorUtility.TryParseHtmlString(returncol, out Color converted))
                {
                    return converted;
                }
                return Color.black;
            });
            isLocal = ValueOutput("Is Local", (f) => GetUserData(f)?.isLocal);
        }

        private UserData GetUserData(Flow f)
        {
            return BanterScene.Instance().users.First(user => 
                {
                    var value = f.GetValue<string>(idOrName);

                    if (user.id == value || user.name == value || user.uid == value)
                    {
                        return true;
                    }
                    return false;
                });
        }
    }

    [UnitTitle("Get Local User Info")]
    [UnitShortTitle("Get Local User")]
    [UnitCategory("Banter")]
    [TypeIcon(typeof(BanterObjectId))]
    public class GetLocalUserInfo : Unit
    {
        [DoNotSerialize]
        public ValueOutput name;

        [DoNotSerialize]
        public ValueOutput id;

        [DoNotSerialize]
        public ValueOutput uid;

        [DoNotSerialize]
        public ValueOutput color;

        [DoNotSerialize]
        public ValueOutput isLocal;

        protected override void Definition()
        {
            name = ValueOutput("Name", (f) => GetUserData(f)?.name);
            id = ValueOutput("Id", (f) => GetUserData(f)?.id);
            uid = ValueOutput("Uid", (f) => GetUserData(f)?.uid);
            color = ValueOutput<Color>("Color", (f) =>
            {
                var returncol = GetUserData(f)?.color;
                if (ColorUtility.TryParseHtmlString(returncol, out Color converted))
                {
                    return converted;
                }
                return Color.black;
            });
            isLocal = ValueOutput("Is Local", (f) => GetUserData(f)?.isLocal);
        }

        private UserData GetUserData(Flow f)
        {
            return BanterScene.Instance().users.First(user => user.isLocal);
        }
    }
}
#endif
