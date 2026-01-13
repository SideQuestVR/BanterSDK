#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;

namespace Banter.VisualScripting
{
    [UnitTitle("Get Platform")]
    [UnitShortTitle("Get Platform")]
    [UnitCategory("Banter\\Utils")]
    [TypeIcon(typeof(BanterObjectId))]
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
                var platformString = BanterScene.Instance().events.GetPlatform?.Invoke() ?? "";
                flow.SetValue(platform, platformString);
                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            platform = ValueOutput<string>("Platform");
        }
    }
}
#endif