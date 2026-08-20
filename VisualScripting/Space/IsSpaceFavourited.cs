using Unity.VisualScripting;
using BS;

namespace BS.VisualScripting
{
    [UnitTitle("Is Space Favourited")]
    [UnitShortTitle("Space Favourited?")]
    [UnitCategory("BS\\Space")]
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
