#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;

namespace Banter.VisualScripting
{
    [UnitTitle("Copy Text To Clipboard")]
    [UnitShortTitle("CopyToClipboard")]
    [UnitCategory("Banter\\Utils")]
    [TypeIcon(typeof(BanterObjectId))]
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
#endif
