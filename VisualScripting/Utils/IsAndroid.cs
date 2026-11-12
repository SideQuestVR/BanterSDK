using Unity.VisualScripting;

namespace BS.VisualScripting
{
    /// <summary>True when the app runs on Android (Quest). False in the Unity editor, whatever the build target.</summary>
    [UnitTitle("Is Android")]
    [UnitShortTitle("Is Android")]
    [UnitCategory("BS\\Utils")]
    [TypeIcon(typeof(BSObjectId))]
    public class IsAndroid : Unit
    {
        [DoNotSerialize]
        public ValueOutput result;

        protected override void Definition()
        {
            result = ValueOutput<bool>("Is Android", flow => BSScene.Instance().IsAndroid());
        }
    }
}
