#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.FlexaBody;

namespace Banter.VisualScripting
{
    [UnitTitle("Set Can Move")]
    [UnitShortTitle("Set Can Move")]
    [UnitCategory("Banter\\Player\\Actions")]
    [TypeIcon(typeof(BanterObjectId))]
    public class SetCanMove : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput canMove;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var value = flow.GetValue<bool>(canMove);
                ActionsSystem.canMove = value;
                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            canMove = ValueInput("Can Move", true);
        }
    }
}
#endif