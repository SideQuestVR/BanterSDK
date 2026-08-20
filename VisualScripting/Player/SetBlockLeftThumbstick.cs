using Unity.VisualScripting;
using BS;
using SideQuest.FlexaBody;

namespace BS.VisualScripting
{
    [UnitTitle("Set Block Left Thumbstick")]
    [UnitShortTitle("Block Left Thumbstick")]
    [UnitCategory("BS\\Player\\Input")]
    [TypeIcon(typeof(BSObjectId))]
    public class SetBlockLeftThumbstick : Unit
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
                ActionsSystem.Blocker_LeftThumbstick.All = value;
                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            blockInput = ValueInput("Block Left Thumbstick", false);
        }
    }
}
