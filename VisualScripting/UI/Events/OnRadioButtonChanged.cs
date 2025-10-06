#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.VisualScripting.UI.Helpers;

namespace Banter.VisualScripting
{
    [UnitTitle("On Radio Button Changed")]
    [UnitShortTitle("On Radio Button Changed")]
    [UnitCategory("Events\\Banter\\UI")]
    [TypeIcon(typeof(BanterObjectId))]
    public class OnRadioButtonChanged : EventUnit<CustomEventArgs>
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
        public ValueOutput isSelected;

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
            isSelected = ValueOutput<bool>("Is Selected");
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
                        
                        UIEventAutoRegisterHelper.TryRegisterChangeEventWithRetry(resolvedTarget, "OnRadioButtonChanged");
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

            // If no specific element is provided, trigger for any boolean change (RadioButton uses bool)
            if (string.IsNullOrEmpty(resolvedTarget2))
            {
                return data.arguments != null && data.arguments.Length >= 1 &&
                       (data.arguments[0] is bool ||
                        (data.arguments[0] is string strVal &&
                         (strVal == "1" || strVal == "0" || strVal.ToLower() == "true" || strVal.ToLower() == "false")));
            }

            // Otherwise, only trigger for the specific element
            return eventElementId == resolvedTarget2 &&
                   data.arguments != null && data.arguments.Length >= 1 &&
                   (data.arguments[0] is bool ||
                    (data.arguments[0] is string strVal2 &&
                     (strVal2 == "1" || strVal2 == "0" || strVal2.ToLower() == "true" || strVal2.ToLower() == "false")));
        }

        protected override void AssignArguments(Flow flow, CustomEventArgs data)
        {
            // Extract element ID from event name
            var eventElementId = data.name.Replace("UIChange_", "");
            flow.SetValue(changedElementId, eventElementId);

            // Parse the boolean value
            if (data.arguments != null && data.arguments.Length >= 1)
            {
                bool selected = false;

                if (data.arguments[0] is bool boolValue)
                {
                    selected = boolValue;
                }
                else if (data.arguments[0] is string stringValue)
                {
                    selected = stringValue == "1" || stringValue.ToLower() == "true";
                }

                flow.SetValue(isSelected, selected);
            }
            else
            {
                flow.SetValue(isSelected, false);
            }
        }
    }
}
#endif
