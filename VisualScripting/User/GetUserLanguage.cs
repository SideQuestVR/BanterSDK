#if BANTER_VISUAL_SCRIPTING
using System.Linq;
using UnityEngine;
using Unity.VisualScripting;
using BS;
using System.Data.Common;

namespace BS.VisualScripting
{
    [UnitTitle("Get Local User Language")]
    [UnitShortTitle("Get User Language")]
    [UnitCategory("Banter\\User")]
    [TypeIcon(typeof(BSObjectId))]
    public class GetUserLanguage : Unit
    {
        [DoNotSerialize]
        public ValueOutput info;

        protected override void Definition()
        {
            info = ValueOutput("Language", (f) => {
                return BSScene.Instance().events.GetUserLanguage();
            });
        }
    }
}
#endif
