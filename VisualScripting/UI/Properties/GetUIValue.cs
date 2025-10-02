#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Bridge;
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
        public ValueInput panelReference;

        [DoNotSerialize]
        public ValueOutput success;

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
                var panel = flow.GetValue<BanterUIPanel>(panelReference);

                // Resolve element name to ID if needed
                string elemId = UIElementResolverHelper.ResolveElementIdOrName(targetId, targetName);

                if (string.IsNullOrEmpty(elemId))
                {
                    Debug.LogWarning("[GetUIValue] Element ID/Name is null or empty.");
                    flow.SetValue(success, false);
                    flow.SetValue(value, 0f);
                    return outputTrigger;
                }

                if (panel == null)
                {
                    Debug.LogWarning("[GetUIValue] Panel reference is null.");
                    flow.SetValue(success, false);
                    flow.SetValue(value, 0f);
                    return outputTrigger;
                }

                try
                {
                    // Clean up any existing callback
                    CleanupCallback();
                    
                    // Get the panel ID for message routing
                    var panelId = panel.GetFormattedPanelId();
                    
                    // Set up callback to receive the value
                    _currentEventName = $"UIProperty_{elemId}_value";
                    _currentCallback = (CustomEventArgs args) => {
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
                                
                            flow.SetValue(value, floatValue);
                            flow.SetValue(success, true);
                            Debug.Log($"[GetUIValue] Received value: {floatValue} for {_currentEventName}");
                        }
                        else
                        {
                            flow.SetValue(value, 0f);
                            flow.SetValue(success, false);
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

                    Debug.Log($"[GetUIValue] Requested value for element {elemId}, waiting for callback on {_currentEventName}");
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[GetUIValue] Failed to get UI value: {e.Message}");
                    flow.SetValue(success, false);
                    flow.SetValue(value, 0f);
                    CleanupCallback();
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            elementId = ValueInput<string>("Element ID", "");
            elementName = ValueInput<string>("Element Name", "");
            panelReference = ValueInput<BanterUIPanel>("Panel");
            success = ValueOutput<bool>("Success");
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