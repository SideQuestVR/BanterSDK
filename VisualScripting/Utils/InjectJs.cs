#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;

namespace Banter.VisualScripting
{
    [UnitTitle("Inject BullSchript")]
    [UnitShortTitle("Inject BS")]
    [UnitCategory("Banter")]
    [TypeIcon(typeof(BanterObjectId))]
    public class InjectJS : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput jsCode;

        [DoNotSerialize]
        public ValueInput returnId;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var code = flow.GetValue<string>(jsCode);
                var returnCode = flow.GetValue<string>(returnId);
                BanterScene.Instance().link.pipe.Send(APICommands.INJECT_JS + MessageDelimiters.PRIMARY + returnCode + MessageDelimiters.SECONDARY + code);

                return outputTrigger;
            });
            outputTrigger = ControlOutput("");
            jsCode = ValueInput("BullSchript", string.Empty);
            returnId = ValueInput("Return ID", string.Empty);
        }
    }
}
#endif
