#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;

namespace Banter.VisualScripting
{
    [UnitTitle("Get Space URL")]
    [UnitShortTitle("Space URL")]
    [UnitCategory("Banter\\Space")]
    [TypeIcon(typeof(BanterObjectId))]
    public class GetSpaceURL : Unit
    {
        [DoNotSerialize]
        public ValueOutput spaceUrl;

        protected override void Definition()
        {
            spaceUrl = ValueOutput<string>("spaceUrl", flow => {
                return BanterScene.Instance().CurrentUrl;
            });
        }
    }
}
#endif