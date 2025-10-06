#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.VisualScripting.UI.Helpers;

namespace Banter.VisualScripting
{
    [UnitTitle("On Slider Int Changed")]
    [UnitShortTitle("On Slider Int Changed")]
    [UnitCategory("Events\\Banter\\UI")]
    [TypeIcon(typeof(BanterObjectId))]
    public class OnSliderIntChanged : EventUnit<CustomEventArgs>
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

                        UIEventAutoRegisterHelper.TryRegisterChangeEventWithRetry(resolvedTarget, "OnSliderIntChanged");
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
                // Only trigger if it's an int value
                return data.arguments != null && data.arguments.Length >= 1 &&
                       (data.arguments[0] is int ||
                        (data.arguments[0] is string strVal && int.TryParse(strVal, out _)));
            }

            // Otherwise, only trigger for the specific element
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

            // Parse the int value
            if (data.arguments != null && data.arguments.Length >= 1)
            {
                int intValue = 0;

                if (data.arguments[0] is int directIntValue)
                {
                    intValue = directIntValue;
                }
                else if (data.arguments[0] is string stringValue)
                {
                    int.TryParse(stringValue, out intValue);
                }

                // Calculate delta
                int deltaValue = intValue - lastValue;

                // Set output values
                flow.SetValue(value, intValue);
                flow.SetValue(delta, deltaValue);

                // Store for next delta calculation
                lastValue = intValue;
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
