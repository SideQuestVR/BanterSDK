#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using BS;
using SideQuest.FlexaBody;

namespace BS.VisualScripting
{
    [UnitTitle("Set Can Jump")]
    [UnitShortTitle("Set Can Jump")]
    [UnitCategory("BS\\Player\\Actions")]
    [TypeIcon(typeof(BSObjectId))]
    public class SetCanJump : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput canJump;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var value = flow.GetValue<bool>(canJump);
                ActionsSystem.canJump = value;
                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            canJump = ValueInput("Can Jump", true);
        }
    }
}
#endif