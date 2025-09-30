#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.FlexaBody;

namespace Banter.VisualScripting
{
    [UnitTitle("Set Can Rotate")]
    [UnitShortTitle("Set Can Rotate")]
    [UnitCategory("Banter\\Player\\Actions")]
    [TypeIcon(typeof(BanterObjectId))]
    public class SetCanRotate : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput canRotate;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var value = flow.GetValue<bool>(canRotate);
                ActionsSystem.canRotate = value;
                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            canRotate = ValueInput("Can Rotate", true);
        }
    }
}
#endif