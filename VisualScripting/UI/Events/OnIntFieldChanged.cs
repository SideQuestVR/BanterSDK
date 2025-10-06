#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.VisualScripting.UI.Helpers;
using UnityEngine;

namespace Banter.VisualScripting
{
    [UnitTitle("On Int Field Changed")]
    [UnitShortTitle("On Int Field Changed")]
    [UnitCategory("Events\\Banter\\UI")]
    [TypeIcon(typeof(BanterObjectId))]
    public class OnIntFieldChanged : EventUnit<CustomEventArgs>
    {
        [DoNotSerialize]
        public ValueInput elementId;

        [DoNotSerialize]
        public ValueInput elementName;

        [DoNotSerialize]
        public ValueInput autoRegister;

        [DoNotSerialize]
        public ValueOutput changedElementId;

        [DoNotSerialize]
        public ValueOutput value;

        [DoNotSerialize]
        public ValueOutput delta;

        protected override bool register => true;

        private bool _eventRegistered = false;
        private int lastValue = 0;

        public override EventHook GetHook(GraphReference reference)
        {
            return new EventHook("OnUIChange");
        }

        protected override void Definition()
        {
            base.Definition();
            elementId = ValueInput<string>("Element ID", "");
            elementName = ValueInput<string>("Element Name", "");
            autoRegister = ValueInput<bool>("Auto Register", true);
            changedElementId = ValueOutput<string>("Element ID");
            value = ValueOutput<int>("Value");
            delta = ValueOutput<int>("Delta");
        }

        public override void StartListening(GraphStack stack)
        {
            base.StartListening(stack);

            // Auto-register when graph starts, not when event arrives
            if (!_eventRegistered)
            {
                var flow = Flow.New(stack.ToReference());
                var shouldAutoRegister = flow.GetValue<bool>(autoRegister);

                if (shouldAutoRegister)
                {
                    var targetId = flow.GetValue<string>(elementId);
                    var targetName = flow.GetValue<string>(elementName);
                    string resolvedTarget = UIElementResolverHelper.ResolveElementIdOrName(targetId, targetName);

                    if (!string.IsNullOrEmpty(resolvedTarget))
                    {

                        UIEventAutoRegisterHelper.TryRegisterChangeEventWithRetry(resolvedTarget, "OnIntFieldChanged");
                        _eventRegistered = true;
                    }
                }
            }
        }

        public override void StopListening(GraphStack stack)
        {
            base.StopListening(stack);
            // Reset flag so auto-registration works on next play session
            _eventRegistered = false;
        }

        protected override bool ShouldTrigger(Flow flow, CustomEventArgs data)
        {
            var targetId = flow.GetValue<string>(elementId);
            var targetName = flow.GetValue<string>(elementName);

            // Check if this is a change event
            if (!data.name.StartsWith("UIChange_"))
                return false;

            // Priority: Element ID first, then Element Name
            string resolvedTarget2 = UIElementResolverHelper.ResolveElementIdOrName(targetId, targetName);

            // Extract element ID from event name
            var eventElementId = data.name.Replace("UIChange_", "");

            // If no specific element is provided, trigger for any int change
            if (string.IsNullOrEmpty(resolvedTarget2))
            {
                // Only trigger if it's an int change event
                return data.arguments != null && data.arguments.Length >= 1 &&
                       (data.arguments[0] is int ||
                        (data.arguments[0] is string strVal && int.TryParse(strVal, out _)));
            }

            // Otherwise, only trigger for the specific element with int values
            return eventElementId == resolvedTarget2 &&
                   data.arguments != null && data.arguments.Length >= 1 &&
                   (data.arguments[0] is int ||
                    (data.arguments[0] is string strVal2 && int.TryParse(strVal2, out _)));
        }

        protected override void AssignArguments(Flow flow, CustomEventArgs data)
        {
            // Extract element ID from event name
            var eventElementId = data.name.Replace("UIChange_", "");
            flow.SetValue(changedElementId, eventElementId);

            // Parse the int values
            if (data.arguments != null && data.arguments.Length >= 2)
            {
                int newValue = 0;
                int oldValue = 0;

                // Parse new value
                if (data.arguments[0] is int intVal)
                    newValue = intVal;
                else if (data.arguments[0] is string strVal && int.TryParse(strVal, out var parsed))
                    newValue = parsed;

                // Parse old value (for delta calculation)
                if (data.arguments[1] is int oldIntVal)
                    oldValue = oldIntVal;
                else if (data.arguments[1] is string oldStrVal && int.TryParse(oldStrVal, out var oldParsed))
                    oldValue = oldParsed;

                flow.SetValue(value, newValue);
                flow.SetValue(delta, newValue - oldValue);
            }
            else if (data.arguments != null && data.arguments.Length >= 1)
            {
                // Only new value available
                int newValue = 0;

                if (data.arguments[0] is int intVal)
                    newValue = intVal;
                else if (data.arguments[0] is string strVal && int.TryParse(strVal, out var parsed))
                    newValue = parsed;

                flow.SetValue(value, newValue);
                flow.SetValue(delta, 0); // No old value to calculate delta
            }
            else
            {
                flow.SetValue(value, 0);
                flow.SetValue(delta, 0);
            }
        }
    }
}
#endif
