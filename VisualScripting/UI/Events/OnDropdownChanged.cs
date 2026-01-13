#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.VisualScripting.UI.Helpers;
using UnityEngine;

namespace Banter.VisualScripting
{
    [UnitTitle("On Dropdown Changed")]
    [UnitShortTitle("On Dropdown Changed")]
    [UnitCategory("Events\\Banter\\UI")]
    [TypeIcon(typeof(BanterObjectId))]
    public class OnDropdownChanged : EventUnit<CustomEventArgs>
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

                        UIEventAutoRegisterHelper.TryRegisterChangeEventWithRetry(resolvedTarget, "OnDropdownChanged");
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

            // If no specific element is provided, trigger for any dropdown change (string value)
            if (string.IsNullOrEmpty(resolvedTarget2))
            {
                // Trigger for string changes (dropdowns return string values)
                return data.arguments != null && data.arguments.Length >= 1 &&
                       data.arguments[0] is string;
            }

            // Otherwise, only trigger for the specific element
            return eventElementId == resolvedTarget2 &&
                   data.arguments != null && data.arguments.Length >= 1 &&
                   data.arguments[0] is string;
        }

        protected override void AssignArguments(Flow flow, CustomEventArgs data)
        {
            // Extract element ID from event name
            var eventElementId = data.name.Replace("UIChange_", "");
            flow.SetValue(changedElementId, eventElementId);

            // Parse the dropdown value
            if (data.arguments != null && data.arguments.Length >= 1)
            {
                string value = data.arguments[0]?.ToString() ?? "";
                flow.SetValue(selectedValue, value);

                // Try to get selected index from additional arguments (if provided)
                // Some implementations might send index as second argument
                if (data.arguments.Length >= 3 && data.arguments[2] is int index)
                {
                    flow.SetValue(selectedIndex, index);
                }
                else
                {
                    // Default to -1 if index not provided
                    flow.SetValue(selectedIndex, -1);
                }
            }
            else
            {
                flow.SetValue(selectedValue, "");
                flow.SetValue(selectedIndex, -1);
            }
        }
    }
}
#endif
