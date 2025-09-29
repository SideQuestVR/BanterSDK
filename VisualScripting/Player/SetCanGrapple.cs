#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.FlexaBody;

namespace Banter.VisualScripting
{
    [UnitTitle("Set Can Grapple")]
    [UnitShortTitle("Set Can Grapple")]
    [UnitCategory("Banter\\Player\\Actions")]
    [TypeIcon(typeof(BanterObjectId))]
    public class SetCanGrapple : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput canGrapple;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var value = flow.GetValue<bool>(canGrapple);
                ActionsSystem.canGrapple = value;
                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            canGrapple = ValueInput("Can Grapple", true);
        }
    }
}
#endif