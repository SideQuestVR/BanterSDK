#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.FlexaBody;

namespace Banter.VisualScripting
{
    [UnitTitle("Set Can Crouch")]
    [UnitShortTitle("Set Can Crouch")]
    [UnitCategory("Banter\\Player\\Actions")]
    [TypeIcon(typeof(BanterObjectId))]
    public class SetCanCrouch : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput canCrouch;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var value = flow.GetValue<bool>(canCrouch);
                ActionsSystem.canCrouch = value;
                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            canCrouch = ValueInput("Can Crouch", true);
        }
    }
}
#endif