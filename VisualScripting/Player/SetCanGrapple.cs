using Unity.VisualScripting;
using BS;
using SideQuest.FlexaBody;

namespace BS.VisualScripting
{
    [UnitTitle("Set Can Grapple")]
    [UnitShortTitle("Set Can Grapple")]
    [UnitCategory("BS\\Player\\Actions")]
    [TypeIcon(typeof(BSObjectId))]
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
