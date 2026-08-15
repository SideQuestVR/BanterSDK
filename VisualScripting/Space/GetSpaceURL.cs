#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using BS;

namespace BS.VisualScripting
{
    [UnitTitle("Get Space URL")]
    [UnitShortTitle("Space URL")]
    [UnitCategory("BS\\Space")]
    [TypeIcon(typeof(BSObjectId))]
    public class GetSpaceURL : Unit
    {
        [DoNotSerialize]
        public ValueOutput spaceUrl;

        protected override void Definition()
        {
            spaceUrl = ValueOutput<string>("spaceUrl", flow => {
                return BSScene.Instance().CurrentUrl;
            });
        }
    }
}
#endif