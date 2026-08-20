using Unity.VisualScripting;
using BS;
using SideQuest.FlexaBody;

namespace BS.VisualScripting
{
    [UnitTitle("Set Can Teleport")]
    [UnitShortTitle("Set Can Teleport")]
    [UnitCategory("BS\\Player\\Actions")]
    [TypeIcon(typeof(BSObjectId))]
    public class SetCanTeleport : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput canTeleport;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var value = flow.GetValue<bool>(canTeleport);
                ActionsSystem.canTeleport = value;
                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            canTeleport = ValueInput("Can Teleport", true);
        }
    }
}
