#if BANTER_VISUAL_SCRIPTING
using System.Linq;
using UnityEngine;
using Unity.VisualScripting;
using Banter.SDK;
using System.Data.Common;

namespace Banter.VisualScripting
{
    [UnitTitle("Get Local User Language")]
    [UnitShortTitle("Get User Language")]
    [UnitCategory("Banter\\User")]
    [TypeIcon(typeof(BanterObjectId))]
    public class GetUserLanguage : Unit
    {
        [DoNotSerialize]
        public ValueOutput info;

        protected override void Definition()
        {
            info = ValueOutput("Language", (f) => {
                return BanterScene.Instance().events.GetUserLanguage();
            });
        }
    }
}
#endif
