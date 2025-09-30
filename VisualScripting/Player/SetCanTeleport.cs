#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.FlexaBody;

namespace Banter.VisualScripting
{
    [UnitTitle("Set Can Teleport")]
    [UnitShortTitle("Set Can Teleport")]
    [UnitCategory("Banter\\Player\\Actions")]
    [TypeIcon(typeof(BanterObjectId))]
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
#endif