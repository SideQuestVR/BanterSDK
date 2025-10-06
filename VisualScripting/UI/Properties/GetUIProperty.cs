#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Bridge;
using Banter.VisualScripting.UI.Helpers;
using UnityEngine;
using System.Collections;

namespace Banter.VisualScripting
{
    [UnitTitle("Get UI Property")]
    [UnitShortTitle("Get UI Property")]
    [UnitCategory("Banter\\UI\\Properties")]
    [TypeIcon(typeof(BanterObjectId))]
    public class GetUIProperty : Unit
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
        public ValueInput propertyName;

        [DoNotSerialize]
        public ValueInput panelReference;

        [DoNotSerialize]
        public ValueOutput success;

        [DoNotSerialize]
        public ValueOutput propertyValue;

        // Store callback for cleanup
        private System.Action<CustomEventArgs> _currentCallback;
        private string _currentEventName;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var targetId = flow.GetValue<string>(elementId);
                var targetName = flow.GetValue<string>(elementName);
                var propName = flow.GetValue<UIPropertyNameVS>(propertyName);
                var panel = flow.GetValue<BanterUIPanel>(panelReference);

                // Resolve element name to ID if needed
                string elemId = UIElementResolverHelper.ResolveElementIdOrName(targetId, targetName);

                if (string.IsNullOrEmpty(elemId))
                {
                    Debug.LogWarning("[GetUIProperty] Element ID/Name is null or empty.");
                    flow.SetValue(success, false);
                    flow.SetValue(propertyValue, null);
                    return outputTrigger;
                }

                if (panel == null)
                {
                    Debug.LogWarning("[GetUIProperty] Panel reference is null.");
                    flow.SetValue(success, false);
                    flow.SetValue(propertyValue, null);
                    return outputTrigger;
                }

                try
                {
                    // Clean up any existing callback
                    CleanupCallback();
                    
                    // Get the panel ID for message routing
                    var panelId = panel.GetFormattedPanelId();
                    
                    // Convert enum to property name
                    var propNameStr = GetPropertyName(propName);
                    
                    // Set up callback to receive the value
                    _currentEventName = $"UIProperty_{elemId}_{propNameStr}";
                    _currentCallback = (CustomEventArgs args) => {
                        if (args.arguments != null && args.arguments.Length > 0)
                        {
                            flow.SetValue(propertyValue, args.arguments[0]);
                            flow.SetValue(success, true);
                            Debug.Log($"[GetUIProperty] Received property value: {args.arguments[0]} for {_currentEventName}");
                        }
                        else
                        {
                            flow.SetValue(propertyValue, null);
                            flow.SetValue(success, false);
                        }
                        
                        // Clean up callback after use
                        CleanupCallback();
                    };
                    
                    // Register for the callback event
                    EventBus.Register<CustomEventArgs>(new EventHook(_currentEventName), _currentCallback);
                    
                    // Format: panelId|GET_UI_PROPERTY|elementId§propertyName
                    var message = $"{panelId}{MessageDelimiters.PRIMARY}{UICommands.GET_UI_PROPERTY}{MessageDelimiters.PRIMARY}{elemId}{MessageDelimiters.SECONDARY}{propNameStr}";
                    
                    // Send command through UIElementBridge
                    UIElementBridge.HandleMessage(message);

                    Debug.Log($"[GetUIProperty] Requested property '{propNameStr}' for element {elemId}, waiting for callback on {_currentEventName}");
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[GetUIProperty] Failed to get UI property: {e.Message}");
                    flow.SetValue(success, false);
                    flow.SetValue(propertyValue, null);
                    CleanupCallback();
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            elementId = ValueInput<string>("Element ID", "");
            elementName = ValueInput<string>("Element Name", "");
            propertyName = ValueInput("Property", UIPropertyNameVS.Text);
            panelReference = ValueInput<BanterUIPanel>("Panel");
            success = ValueOutput<bool>("Success");
            propertyValue = ValueOutput<object>("Value");
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

        private string GetPropertyName(UIPropertyNameVS propName)
        {
            return propName switch
            {
                UIPropertyNameVS.Checked => "checked",
                UIPropertyNameVS.Elasticity => "elasticity",
                UIPropertyNameVS.Enabled => "enabled",
                UIPropertyNameVS.Horizontalscrolling => "horizontalscrolling",
                UIPropertyNameVS.Maxvalue => "maxvalue",
                UIPropertyNameVS.Minvalue => "minvalue",
                UIPropertyNameVS.Name => "name",
                UIPropertyNameVS.Scrolldecelerationrate => "scrolldecelerationrate",
                UIPropertyNameVS.Scrollposition => "scrollposition",
                UIPropertyNameVS.Text => "text",
                UIPropertyNameVS.Tooltip => "tooltip",
                UIPropertyNameVS.Value => "value",
                UIPropertyNameVS.Verticalscrolling => "verticalscrolling",
                UIPropertyNameVS.Visible => "visible",
                _ => "text"
            };
        }
    }
}
#endif