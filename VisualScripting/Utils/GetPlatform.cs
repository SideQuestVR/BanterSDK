#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using BS;

namespace BS.VisualScripting
{
    [UnitTitle("Get Platform")]
    [UnitShortTitle("Get Platform")]
    [UnitCategory("BS\\Utils")]
    [TypeIcon(typeof(BSObjectId))]
    public class GetPlatform : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueOutput platform;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var platformString = BSScene.Instance().events.GetPlatform?.Invoke() ?? "";
                flow.SetValue(platform, platformString);
                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            platform = ValueOutput<string>("Platform");
        }
    }
}
#endif