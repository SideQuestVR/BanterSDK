using Unity.VisualScripting;
using BS;
using SideQuest.FlexaBody;

namespace BS.VisualScripting
{
    [UnitTitle("Set Block Right Trigger")]
    [UnitShortTitle("Block Right Trigger")]
    [UnitCategory("BS\\Player\\Input")]
    [TypeIcon(typeof(BSObjectId))]
    public class SetBlockRightTrigger : Unit
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
                ActionsSystem.Blocker_RightTrigger.All = value;
                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            blockInput = ValueInput("Block Right Trigger", false);
        }
    }
}
