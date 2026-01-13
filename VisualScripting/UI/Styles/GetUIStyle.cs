#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Bridge;
using Banter.UI.Core;
using UnityEngine;
using Banter.VisualScripting.UI.Helpers;

namespace Banter.VisualScripting
{
    [UnitTitle("Get UI Style")]
    [UnitShortTitle("Get UI Style")]
    [UnitCategory("Banter\\UI\\Styles")]
    [TypeIcon(typeof(BanterObjectId))]
    public class GetUIStyle : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput elementId;

        [DoNotSerialize]
        public ValueInput elementName;

        [DoNotSerialize]
        public ValueInput styleProperty;

        [DoNotSerialize]
        public ValueOutput elementIdOutput;

        [DoNotSerialize]
        public ValueOutput styleValue;

        // Store callback for cleanup
        private System.Action<CustomEventArgs> _currentCallback;
        private string _currentEventName;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var targetId = flow.GetValue<string>(elementId);
                var targetName = flow.GetValue<string>(elementName);
                var property = flow.GetValue<UIStyleProperty>(styleProperty);

                // Resolve element name to ID if needed
                string elemId = UIElementResolverHelper.ResolveElementIdOrName(targetId, targetName);

                if (string.IsNullOrEmpty(elemId))
                {
                    Debug.LogWarning("[GetUIStyle] Element ID/Name is null or empty.");
                    flow.SetValue(elementIdOutput, "");
                    flow.SetValue(styleValue, null);
                    return outputTrigger;
                }

                // Set element ID output for chaining
                flow.SetValue(elementIdOutput, elemId);

                var graphReference = flow.stack.ToReference();

                try
                {
                    // Clean up any existing callback
                    CleanupCallback();

                    // Auto-resolve panel from element ID
                    var panelId = UIPanelExtensions.GetFormattedPanelIdByElementId(elemId);
                    if (panelId == null)
                    {
                        Debug.LogError($"[GetUIStyle] Could not resolve panel for element '{elemId}'");
                        flow.SetValue(styleValue, null);
                        return outputTrigger;
                    }

                    // Convert the UIStyleProperty enum to its USS property name
                    var propertyName = property.ToUSSName();

                    // Set up callback to receive the value
                    _currentEventName = $"UIStyle_{elemId}_{propertyName}";
                    _currentCallback = (CustomEventArgs args) => {
                        if (!graphReference.isValid)
                        {
                            CleanupCallback();
                            return;
                        }

                        var callbackFlow = Flow.New(graphReference);

                        if (args.arguments != null && args.arguments.Length > 0)
                        {
                            callbackFlow.SetValue(styleValue, args.arguments[0]);
#if BANTER_UI_DEBUG
                            Debug.Log($"[GetUIStyle] Received style value: {args.arguments[0]} for {_currentEventName}");
#endif
                        }
                        else
                        {
                            callbackFlow.SetValue(styleValue, null);
                        }

                        // Clean up callback after use
                        CleanupCallback();
                    };

                    // Register for the callback event
                    EventBus.Register<CustomEventArgs>(new EventHook(_currentEventName), _currentCallback);

                    // Format: panelId|GET_UI_STYLE|elementId§styleName
                    var message = $"{panelId}{MessageDelimiters.PRIMARY}{UICommands.GET_UI_STYLE}{MessageDelimiters.PRIMARY}{elemId}{MessageDelimiters.SECONDARY}{propertyName}";

                    // Send command through UIElementBridge
                    UIElementBridge.HandleMessage(message);

#if BANTER_UI_DEBUG
                    Debug.Log($"[GetUIStyle] Requested style '{propertyName}' for element {elemId}, waiting for callback on {_currentEventName}");
#endif
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[GetUIStyle] Failed to get UI style: {e.Message}");
                    flow.SetValue(styleValue, null);
                    CleanupCallback();
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            elementId = ValueInput<string>("Element ID", "");
            elementName = ValueInput<string>("Element Name", "");
            styleProperty = ValueInput("Style Property", UIStyleProperty.BackgroundColor);
            elementIdOutput = ValueOutput<string>("Element ID");
            styleValue = ValueOutput<string>("Style Value");
        }

        private void CleanupCallback()
        {
            if (_currentCallback != null && !string.IsNullOrEmpty(_currentEventName))
            {
                EventBus.Unregister(new EventHook(_currentEventName), _currentCallback);
                _currentCallback = null;
                _currentEventName = null;
            }
        }
    }
}
#endif
