#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Bridge;
using Banter.UI.Core;
using Banter.VisualScripting.UI.Helpers;
using UnityEngine;

namespace Banter.VisualScripting
{
    [UnitTitle("Get UI Value")]
    [UnitShortTitle("Get UI Value")]
    [UnitCategory("Banter\\UI\\Properties\\Value")]
    [TypeIcon(typeof(BanterObjectId))]
    public class GetUIValue : Unit
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
        public ValueOutput value;

        // Store callback for cleanup
        private System.Action<CustomEventArgs> _currentCallback;
        private string _currentEventName;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var targetId = flow.GetValue<string>(elementId);
                var targetName = flow.GetValue<string>(elementName);

                // Resolve element name to ID if needed
                string elemId = UIElementResolverHelper.ResolveElementIdOrName(targetId, targetName);

                if (string.IsNullOrEmpty(elemId))
                {
                    Debug.LogWarning("[GetUIValue] Element ID/Name is null or empty.");
                    flow.SetValue(value, 0f);
                    return outputTrigger;
                }

                if (!UIPanelExtensions.ValidateElementForOperation(elemId, "GetUIValue"))
                {
                    flow.SetValue(value, 0f);
                    return outputTrigger;
                }

                var graphReference = flow.stack.ToReference();

                try
                {
                // Clean up any existing callback
                CleanupCallback();
                
                    // Get the panel ID for message routing
                    var panelId = UIPanelExtensions.GetFormattedPanelIdByElementId(elemId);
                    if (panelId == null)
                    {
                        Debug.LogError($"[GetUIValue] Could not resolve panel for element '{elemId}'");
                        flow.SetValue(value, 0f);
                        return outputTrigger;
                    }
                
                // Set up callback to receive the value
                _currentEventName = $"UIProperty_{elemId}_value";
                _currentCallback = (CustomEventArgs args) => {
                    if (!graphReference.isValid)
                    {
                        CleanupCallback();
                        return;
                    }

                    var callbackFlow = Flow.New(graphReference);

                    if (args.arguments != null && args.arguments.Length > 0)
                    {
                        // Try to parse as float, fallback to 0
                        var rawValue = args.arguments[0];
                        float floatValue = 0f;
                        
                        if (rawValue is float f)
                            floatValue = f;
                        else if (rawValue is string s && float.TryParse(s, out var parsed))
                            floatValue = parsed;
                        else if (rawValue != null && float.TryParse(rawValue.ToString(), out var parsedStr))
                            floatValue = parsedStr;
                            
                        callbackFlow.SetValue(value, floatValue);
#if BANTER_UI_DEBUG
                        Debug.Log($"[GetUIValue] Received value: {floatValue} for {_currentEventName}");
#endif
                    }
                    else
                    {
                        callbackFlow.SetValue(value, 0f);
                    }
                    
                    // Clean up callback after use
                    CleanupCallback();
                };
                    
                    // Register for the callback event
                    EventBus.Register<CustomEventArgs>(new EventHook(_currentEventName), _currentCallback);
                    
                    // Format: panelId|GET_UI_PROPERTY|elementId§propertyName
                    var message = $"{panelId}{MessageDelimiters.PRIMARY}{UICommands.GET_UI_PROPERTY}{MessageDelimiters.PRIMARY}{elemId}{MessageDelimiters.SECONDARY}value";
                    
                    // Send command through UIElementBridge
                    UIElementBridge.HandleMessage(message);

#if BANTER_UI_DEBUG
                    Debug.Log($"[GetUIValue] Requested value for element {elemId}, waiting for callback on {_currentEventName}");
#endif
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[GetUIValue] Failed to get UI value: {e.Message}");
                    flow.SetValue(value, 0f);
                    CleanupCallback();
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            elementId = ValueInput<string>("Element ID", "");
            elementName = ValueInput<string>("Element Name", "");
            value = ValueOutput<float>("Value");
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
