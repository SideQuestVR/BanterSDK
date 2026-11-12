using Unity.VisualScripting;
using BS;
using UnityEngine;

namespace BS.VisualScripting
{
    [UnitTitle("Add Toast Message")]
    [UnitShortTitle("Add Toast Message")]
    [UnitCategory("BS\\User")]
    [TypeIcon(typeof(BSObjectId))]
    public class AddToastMessage : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput message;
        [DoNotSerialize]
        [PortLabel("Delay (s)")]
        public ValueInput delay;
        [DoNotSerialize]
        [PortLabel("Timeout (s)")]
        public ValueInput timeout;
        [DoNotSerialize]
        public ValueInput color;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) =>
            {
                var _message = flow.GetValue<string>(message);
                var _color = flow.GetValue<Color>(color);
                var _timeout = flow.GetValue<int>(timeout);
                var _delay = flow.GetValue<int>(delay);

                // Seconds on the node; OnToast carries milliseconds (the host reads timeoutMs / 1000).
                BSScene.Instance().events.OnToast?.Invoke(_message, _timeout * 1000, _delay * 1000, _color);

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
