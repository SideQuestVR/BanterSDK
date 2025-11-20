#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Bridge;
using Banter.UI.Core;
using Banter.VisualScripting.UI.Helpers;
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
        public ValueInput elementName;

        [DoNotSerialize]
        public ValueOutput textValue;

        // Store callback for cleanup
        private System.Action<CustomEventArgs> _currentCallback;
        private string _currentEventName;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var targetId = flow.GetValue<string>(elementId);
                var targetName = flow.GetValue<string>(elementName);
                string elemId = UIElementResolverHelper.ResolveElementIdOrName(targetId, targetName);

                if (string.IsNullOrEmpty(elemId))
                {
                    Debug.LogWarning("[GetUIText] Element ID/Name is null or empty.");
                    flow.SetValue(textValue, "");
                    return outputTrigger;
                }

                if (!UIPanelExtensions.ValidateElementForOperation(elemId, "GetUIText"))
                {
                    flow.SetValue(textValue, "");
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
                        Debug.LogError($"[GetUIText] Could not resolve panel for element '{elemId}'");
                        flow.SetValue(textValue, "");
                        return outputTrigger;
                    }
                
                // Set up callback to receive the value
                _currentEventName = $"UIProperty_{elemId}_text";
                _currentCallback = (CustomEventArgs args) => {
                    if (!graphReference.isValid)
                    {
                        CleanupCallback();
                        return;
                    }

                    var callbackFlow = Flow.New(graphReference);

                    if (args.arguments != null && args.arguments.Length > 0)
                    {
                        callbackFlow.SetValue(textValue, args.arguments[0]?.ToString() ?? "");
#if BANTER_UI_DEBUG
                        Debug.Log($"[GetUIText] Received text value: '{args.arguments[0]}' for {_currentEventName}");
#endif
                    }
                    else
                    {
                        callbackFlow.SetValue(textValue, "");
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

#if BANTER_UI_DEBUG
                    Debug.Log($"[GetUIText] Requested text for element {elemId}, waiting for callback on {_currentEventName}");
#endif
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[GetUIText] Failed to get UI text: {e.Message}");
                    flow.SetValue(textValue, "");
                    CleanupCallback();
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            elementId = ValueInput<string>("Element ID", "");
            elementName = ValueInput<string>("Element Name", "");
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
