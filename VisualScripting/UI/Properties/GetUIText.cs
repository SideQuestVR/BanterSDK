#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Bridge;
using UnityEngine;

namespace Banter.VisualScripting
{
    [UnitTitle("Get UI Text")]
    [UnitShortTitle("Get UI Text")]
    [UnitCategory("Banter\\UI\\Properties\\Text")]
    [TypeIcon(typeof(BanterObjectId))]
    public class GetUIText : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput elementId;

        [DoNotSerialize]
        public ValueInput panelReference;

        [DoNotSerialize]
        public ValueOutput success;

        [DoNotSerialize]
        public ValueOutput textValue;

        // Store callback for cleanup
        private System.Action<CustomEventArgs> _currentCallback;
        private string _currentEventName;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var elemId = flow.GetValue<string>(elementId);
                var panel = flow.GetValue<BanterUIPanel>(panelReference);

                if (string.IsNullOrEmpty(elemId))
                {
                    Debug.LogWarning("[GetUIText] Element ID is null or empty.");
                    flow.SetValue(success, false);
                    flow.SetValue(textValue, "");
                    return outputTrigger;
                }

                if (panel == null)
                {
                    Debug.LogWarning("[GetUIText] Panel reference is null.");
                    flow.SetValue(success, false);
                    flow.SetValue(textValue, "");
                    return outputTrigger;
                }

                try
                {
                    // Clean up any existing callback
                    CleanupCallback();
                    
                    // Get the panel ID for message routing
                    var panelId = $"PanelSettings {panel.PanelId}";
                    
                    // Set up callback to receive the value
                    _currentEventName = $"UIProperty_{elemId}_text";
                    _currentCallback = (CustomEventArgs args) => {
                        if (args.arguments != null && args.arguments.Length > 0)
                        {
                            flow.SetValue(textValue, args.arguments[0]?.ToString() ?? "");
                            flow.SetValue(success, true);
                            Debug.Log($"[GetUIText] Received text value: '{args.arguments[0]}' for {_currentEventName}");
                        }
                        else
                        {
                            flow.SetValue(textValue, "");
                            flow.SetValue(success, false);
                        }
                        
                        // Clean up callback after use
                        CleanupCallback();
                    };
                    
                    // Register for the callback event
                    EventBus.Register<CustomEventArgs>(new EventHook(_currentEventName), _currentCallback);
                    
                    // Format: panelId|GET_UI_PROPERTY|elementId§propertyName
                    var message = $"{panelId}{MessageDelimiters.PRIMARY}{UICommands.GET_UI_PROPERTY}{MessageDelimiters.PRIMARY}{elemId}{MessageDelimiters.SECONDARY}text";
                    
                    // Send command through UIElementBridge
                    UIElementBridge.HandleMessage(message);

                    Debug.Log($"[GetUIText] Requested text for element {elemId}, waiting for callback on {_currentEventName}");
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[GetUIText] Failed to get UI text: {e.Message}");
                    flow.SetValue(success, false);
                    flow.SetValue(textValue, "");
                    CleanupCallback();
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            elementId = ValueInput<string>("Element ID");
            panelReference = ValueInput<BanterUIPanel>("Panel");
            success = ValueOutput<bool>("Success");
            textValue = ValueOutput<string>("Text");
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