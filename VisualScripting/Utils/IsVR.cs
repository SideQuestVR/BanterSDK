using Unity.VisualScripting;

namespace BS.VisualScripting
{
    /// <summary>True when the player is in a headset rather than on a flat screen. In SDK Play Mode: whether an XR device is active.</summary>
    [UnitTitle("Is VR")]
    [UnitShortTitle("Is VR")]
    [UnitCategory("BS\\Utils")]
    [TypeIcon(typeof(BSObjectId))]
    public class IsVR : Unit
    {
        [DoNotSerialize]
        public ValueOutput result;

        protected override void Definition()
        {
            result = ValueOutput<bool>("Is VR", flow => BSScene.Instance().IsVR());
        }
    }
}
