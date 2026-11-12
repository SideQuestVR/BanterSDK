using Unity.VisualScripting;

namespace BS.VisualScripting
{
    /// <summary>True when the app runs on Windows, the Windows editor included.</summary>
    [UnitTitle("Is Windows")]
    [UnitShortTitle("Is Windows")]
    [UnitCategory("BS\\Utils")]
    [TypeIcon(typeof(BSObjectId))]
    public class IsWindows : Unit
    {
        [DoNotSerialize]
        public ValueOutput result;

        protected override void Definition()
        {
            result = ValueOutput<bool>("Is Windows", flow => BSScene.Instance().IsWindows());
        }
    }
}
