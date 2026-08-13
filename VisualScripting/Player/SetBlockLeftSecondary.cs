#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using SideQuest.FlexaBody;

namespace Banter.VisualScripting
{
    [UnitTitle("Set Block Left Secondary")]
    [UnitShortTitle("Block Left Secondary")]
    [UnitCategory("Banter\\Player\\Input")]
    [TypeIcon(typeof(BSObjectId))]
    public class SetBlockLeftSecondary : Unit
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
                ActionsSystem.Blocker_LeftSecondary.All = value;
                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            blockInput = ValueInput("Block Left Secondary", false);
        }
    }
}
#endif