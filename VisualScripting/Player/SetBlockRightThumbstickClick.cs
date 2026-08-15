#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using BS;
using SideQuest.FlexaBody;

namespace BS.VisualScripting
{
    [UnitTitle("Set Block Right Thumbstick Click")]
    [UnitShortTitle("Block Right Stick Click")]
    [UnitCategory("BS\\Player\\Input")]
    [TypeIcon(typeof(BSObjectId))]
    public class SetBlockRightThumbstickClick : Unit
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
                ActionsSystem.Blocker_RightThumbstickClick.All = value;
                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            blockInput = ValueInput("Block Right Thumbstick Click", false);
        }
    }
}
#endif