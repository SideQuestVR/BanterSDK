#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using UnityEngine;

namespace Banter.VisualScripting
{
    [UnitTitle("Add Toast Message")]
    [UnitShortTitle("Add Toast Message")]
    [UnitCategory("Banter\\User")]
    [TypeIcon(typeof(BanterObjectId))]
    public class AddToastMessage : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput message;
        [DoNotSerialize]
        public ValueInput delay;
        [DoNotSerialize]
        public ValueInput timeout;
        [DoNotSerialize]
        public ValueInput color;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var _message = flow.GetValue<string>(message);
                var _color = flow.GetValue<Color>(color);
                var _timeout = flow.GetValue<float>(timeout);
                var _delay = flow.GetValue<float>(delay);
                
                BanterScene.Instance().events.OnToast?.Invoke(_message, _timeout, _delay, _color);

                return outputTrigger;
            });
            outputTrigger = ControlOutput("");
            message = ValueInput("Message", "");
            color = ValueInput("Color", new Color(0.1647f, 0.1647f, 0.1647f));
            timeout = ValueInput("Timeout", 5);
            delay = ValueInput("Delay", 0);
        }
    }
}
#endif
