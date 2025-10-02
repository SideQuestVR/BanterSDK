#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.VisualScripting.UI.Helpers;
using UnityEngine;

namespace Banter.VisualScripting
{
    [UnitTitle("On Text Field Changed")]
    [UnitShortTitle("On Text Field Changed")]
    [UnitCategory("Events\\Banter\\UI\\Input")]
    [TypeIcon(typeof(BanterObjectId))]
    public class OnTextFieldChanged : EventUnit<CustomEventArgs>
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
        public ValueOutput text;

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
            text = ValueOutput<string>("Text");
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
                        
                        UIEventAutoRegisterHelper.TryRegisterChangeEventWithRetry(resolvedTarget, "OnTextFieldChanged");
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

            // If no specific element is provided, trigger for any string change
            if (string.IsNullOrEmpty(resolvedTarget2))
            {
                // Trigger for string change events
                // Note: User should use the appropriate specialized node (OnToggleChanged, OnSliderChanged)
                // for non-string inputs. This node accepts any string value.
                return data.arguments != null && data.arguments.Length >= 1 &&
                       data.arguments[0] is string;
            }

            // Otherwise, only trigger for the specific element with string values
            return eventElementId == resolvedTarget2 &&
                   data.arguments != null && data.arguments.Length >= 1 &&
                   data.arguments[0] is string;
        }

        protected override void AssignArguments(Flow flow, CustomEventArgs data)
        {
            // Extract element ID from event name
            var eventElementId = data.name.Replace("UIChange_", "");
            flow.SetValue(changedElementId, eventElementId);

            // Parse the string value
            if (data.arguments != null && data.arguments.Length >= 1)
            {
                string textValue = data.arguments[0]?.ToString() ?? "";
                flow.SetValue(text, textValue);
            }
            else
            {
                flow.SetValue(text, "");
            }
        }
    }
}
#endif
