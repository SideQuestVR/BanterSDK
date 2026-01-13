#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;

namespace Banter.VisualScripting
{
    [UnitTitle("Inject BullSchript")]
    [UnitShortTitle("Inject BS")]
    [UnitCategory("Banter\\Browser")]
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
#if BANTER_ORA
                BanterScene.Instance().link.pipe.view.EvaluateJS(code,
                    s =>
                    {
                        BanterScene.Instance().events.OnJsCallbackRecieved.Invoke(returnCode, s, true);
                    });
#endif

                return outputTrigger;
            });
            outputTrigger = ControlOutput("");
            jsCode = ValueInput("BullSchript", string.Empty);
            returnId = ValueInput("Return ID", string.Empty);
        }
    }
}
#endif
