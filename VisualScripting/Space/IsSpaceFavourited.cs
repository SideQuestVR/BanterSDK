#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;

namespace Banter.VisualScripting
{
    [UnitTitle("Is Space Favourited")]
    [UnitShortTitle("Space Favourited?")]
    [UnitCategory("Banter\\Space")]
    [TypeIcon(typeof(BSObjectId))]
    public class IsSpaceFavourited : Unit
    {
        [DoNotSerialize]
        public ValueOutput isFavourited;

        protected override void Definition()
        {
            isFavourited = ValueOutput<bool>("isFavourited", flow => {
                return BSScene.Instance().data.IsSpaceFavourited();
            });
        }
    }
}
#endif
