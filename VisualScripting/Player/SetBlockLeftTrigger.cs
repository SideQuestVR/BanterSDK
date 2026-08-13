#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using BS;
using SideQuest.FlexaBody;

namespace BS.VisualScripting
{
    [UnitTitle("Set Block Left Trigger")]
    [UnitShortTitle("Block Left Trigger")]
    [UnitCategory("Banter\\Player\\Input")]
    [TypeIcon(typeof(BSObjectId))]
    public class SetBlockLeftTrigger : Unit
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
                ActionsSystem.Blocker_LeftTrigger.All = value;
                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            blockInput = ValueInput("Block Left Trigger", false);
        }
    }
}
#endif