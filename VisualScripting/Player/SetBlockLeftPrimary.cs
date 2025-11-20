#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.FlexaBody;

namespace Banter.VisualScripting
{
    [UnitTitle("Set Block Left Primary")]
    [UnitShortTitle("Block Left Primary")]
    [UnitCategory("Banter\\Player\\Input")]
    [TypeIcon(typeof(BanterObjectId))]
    public class SetBlockLeftPrimary : Unit
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
                ActionsSystem.Blocker_LeftPrimary.All = value;
                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            blockInput = ValueInput("Block Left Primary", false);
        }
    }
}
#endif