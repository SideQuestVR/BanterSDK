#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.VisualScripting.UI.Helpers;
using UnityEngine;

namespace Banter.VisualScripting
{
    [UnitTitle("On Toggle Changed")]
    [UnitShortTitle("On Toggle Changed")]
    [UnitCategory("Events\\Banter\\UI")]
    [TypeIcon(typeof(BanterObjectId))]
    public class OnToggleChanged : EventUnit<CustomEventArgs>
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
        public ValueOutput isOn;

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
            isOn = ValueOutput<bool>("Is On");
        }

        public override void StartListening(GraphStack stack)
        {
            LogVerbose("StartListening called");
            base.StartListening(stack);

            // Auto-register when graph starts, not when event arrives
            if (!_eventRegistered)
            {
                LogVerbose("Not yet registered, checking auto-register setting");
                var flow = Flow.New(stack.ToReference());
                var shouldAutoRegister = flow.GetValue<bool>(autoRegister);
                LogVerbose($"Auto Register = {shouldAutoRegister}");

                if (shouldAutoRegister)
                {
                    var targetId = flow.GetValue<string>(elementId);
                    var targetName = flow.GetValue<string>(elementName);
                    LogVerbose($"Element ID = '{targetId}', Element Name = '{targetName}'");

                    string resolvedTarget = UIElementResolverHelper.ResolveElementIdOrName(targetId, targetName);
                    LogVerbose($"Resolved target = '{resolvedTarget}'");

                    if (!string.IsNullOrEmpty(resolvedTarget))
                    {
                        LogVerbose($"Calling TryRegisterChangeEventWithRetry for '{resolvedTarget}'");
                        UIEventAutoRegisterHelper.TryRegisterChangeEventWithRetry(resolvedTarget, "OnToggleChanged");
                        _eventRegistered = true;
                    }
                    else
                    {
                        Debug.LogWarning("[OnToggleChanged] Resolved target is null or empty - cannot auto-register");
                    }
                }
            }
            else
            {
                LogVerbose("Already registered, skipping auto-register");
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
            LogVerbose($"ShouldTrigger called with event name: '{data.name}'");

            var targetId = flow.GetValue<string>(elementId);
            var targetName = flow.GetValue<string>(elementName);

            LogVerbose($"Target ID: '{targetId}', Target Name: '{targetName}'");

            // Check if this is a change event
            if (!data.name.StartsWith("UIChange_"))
            {
                LogVerbose("Event name doesn't start with 'UIChange_', returning false");
                return false;
            }

            // Priority: Element ID first, then Element Name
            string resolvedTarget2 = UIElementResolverHelper.ResolveElementIdOrName(targetId, targetName);
            LogVerbose($"Resolved target: '{resolvedTarget2}'");

            // Extract element ID from event name
            var eventElementId = data.name.Replace("UIChange_", "");
            LogVerbose($"Event element ID: '{eventElementId}'");

            // If no specific element is provided, trigger for any toggle change
            if (string.IsNullOrEmpty(resolvedTarget2))
            {
                LogVerbose("No specific target, checking if boolean change event");
                // Only trigger if it's a boolean change event
                var result = data.arguments != null && data.arguments.Length >= 1 &&
                       (data.arguments[0] is bool || (data.arguments[0] is string strVal && (strVal == "1" || strVal == "0" || strVal.ToLower() == "true" || strVal.ToLower() == "false")));
                LogVerbose($"ShouldTrigger result (any toggle): {result}");
                return result;
            }

            // Otherwise, only trigger for the specific element
            var matches = eventElementId == resolvedTarget2;
            var hasValidArgs = data.arguments != null && data.arguments.Length >= 1 &&
                   (data.arguments[0] is bool || (data.arguments[0] is string strVal2 && (strVal2 == "1" || strVal2 == "0" || strVal2.ToLower() == "true" || strVal2.ToLower() == "false")));

            LogVerbose($"Element match: {matches}, Valid args: {hasValidArgs}");
            LogVerbose($"ShouldTrigger result (specific element): {matches && hasValidArgs}");

            return matches && hasValidArgs;
        }

        protected override void AssignArguments(Flow flow, CustomEventArgs data)
        {
            // Extract element ID from event name
            var eventElementId = data.name.Replace("UIChange_", "");
            flow.SetValue(changedElementId, eventElementId);

            // Parse the boolean value
            if (data.arguments != null && data.arguments.Length >= 1)
            {
                bool value = false;

                if (data.arguments[0] is bool boolValue)
                {
                    value = boolValue;
                }
                else if (data.arguments[0] is string strValue)
                {
                    value = strValue == "1" || strValue.ToLower() == "true";
                }

                flow.SetValue(isOn, value);
            }
            else
            {
                flow.SetValue(isOn, false);
            }
        }

        [System.Diagnostics.Conditional("BANTER_UI_DEBUG")]
        private void LogVerbose(string message)
        {
            UnityEngine.Debug.Log($"[OnToggleChanged] {message}");
        }
    }
}
#endif
