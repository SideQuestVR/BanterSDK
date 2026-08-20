using Unity.VisualScripting;
using BS;
using SideQuest.FlexaBody;

namespace BS.VisualScripting
{
    [UnitTitle("Set Block Right Thumbstick")]
    [UnitShortTitle("Block Right Thumbstick")]
    [UnitCategory("BS\\Player\\Input")]
    [TypeIcon(typeof(BSObjectId))]
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
                ActionsSystem.Blocker_RightThumbstick.All = value;
                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            blockInput = ValueInput("Block Right Thumbstick", false);
        }
    }
}
