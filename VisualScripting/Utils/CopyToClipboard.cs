using Unity.VisualScripting;
using BS;

namespace BS.VisualScripting
{
    [UnitTitle("Copy Text To Clipboard")]
    [UnitShortTitle("CopyToClipboard")]
    [UnitCategory("BS\\Utils")]
    [TypeIcon(typeof(BSObjectId))]
    public class CopyToClipboard : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput text;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var _text = flow.GetValue<string>(text);
                UniClipboard.SetText(_text);
                return outputTrigger;
            });
            outputTrigger = ControlOutput("");
            text = ValueInput("String", string.Empty);
        }
    }
}
