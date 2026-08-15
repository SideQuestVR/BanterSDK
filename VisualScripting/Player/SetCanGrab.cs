#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using BS;
using SideQuest.FlexaBody;

namespace BS.VisualScripting
{
    [UnitTitle("Set Can Grab")]
    [UnitShortTitle("Set Can Grab")]
    [UnitCategory("BS\\Player\\Actions")]
    [TypeIcon(typeof(BSObjectId))]
    public class SetCanGrab : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput canGrab;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var value = flow.GetValue<bool>(canGrab);
                ActionsSystem.canGrab = value;
                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            canGrab = ValueInput("Can Grab", true);
        }
    }
}
#endif