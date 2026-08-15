#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using BS;

namespace BS.VisualScripting
{
    [UnitTitle("Inject BullSchript")]
    [UnitShortTitle("Inject BS")]
    [UnitCategory("BS\\Browser")]
    [TypeIcon(typeof(BSObjectId))]
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
                BSScene.Instance().link.pipe.view.EvaluateJS(code,
                    s =>
                    {
                        BSScene.Instance().events.OnJsCallbackRecieved.Invoke(returnCode, s, true);
                    });

                return outputTrigger;
            });
            outputTrigger = ControlOutput("");
            jsCode = ValueInput("BullSchript", string.Empty);
            returnId = ValueInput("Return ID", string.Empty);
        }
    }
}
#endif
