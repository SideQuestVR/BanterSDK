#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.FlexaBody;

namespace Banter.VisualScripting
{
    [UnitTitle("Set Block Right Thumbstick")]
    [UnitShortTitle("Block Right Thumbstick")]
    [UnitCategory("Banter\\Player\\Input")]
    [TypeIcon(typeof(BanterObjectId))]
    public class SetBlockRightThumbstick : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput blockInput;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var value = flow.GetValue<bool>(blockInput);
                ActionsSystem.blockRightThumbstick = value;
                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            blockInput = ValueInput("Block Right Thumbstick", false);
        }
    }
}
#endif