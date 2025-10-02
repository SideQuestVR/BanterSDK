#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.VisualScripting.UI.Helpers;

namespace Banter.VisualScripting
{
    [UnitTitle("On Radio Button Group Changed")]
    [UnitShortTitle("On Radio Button Group Changed")]
    [UnitCategory("Events\\Banter\\UI")]
    [TypeIcon(typeof(BanterObjectId))]
    public class OnRadioButtonGroupChanged : EventUnit<CustomEventArgs>
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
        public ValueOutput selectedValue;

        [DoNotSerialize]
        public ValueOutput selectedIndex;

        protected override bool register => true;

        private bool _eventRegistered = false;

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
            selectedValue = ValueOutput<string>("Selected Value");
            selectedIndex = ValueOutput<int>("Selected Index");
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
                        
                        UIEventAutoRegisterHelper.TryRegisterChangeEventWithRetry(resolvedTarget, "OnRadioButtonGroupChanged");
                        _eventRegistered = true;
                    }
                }
            }
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

            // If no specific element is provided, trigger for any int change (RadioButtonGroup typically uses int for index)
            if (string.IsNullOrEmpty(resolvedTarget2))
            {
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

            // Parse the value - RadioButtonGroup typically returns an int index
            if (data.arguments != null && data.arguments.Length >= 1)
            {
                int index = -1;
                string value = "";

                if (data.arguments[0] is int intValue)
                {
                    index = intValue;
                    value = intValue.ToString();
                }
                else if (data.arguments[0] is string stringValue)
                {
                    if (int.TryParse(stringValue, out int parsedIndex))
                    {
                        index = parsedIndex;
                        value = stringValue;
                    }
                    else
                    {
                        // If it's not an index, treat it as a value string
                        value = stringValue;
                        index = -1;
                    }
                }

                flow.SetValue(selectedIndex, index);
                flow.SetValue(selectedValue, value);
            }
            else
            {
                flow.SetValue(selectedIndex, -1);
                flow.SetValue(selectedValue, "");
            }
        }
    }
}
#endif
