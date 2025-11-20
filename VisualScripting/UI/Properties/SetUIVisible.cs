#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Bridge;
using Banter.UI.Core;
using Banter.VisualScripting.UI.Helpers;
using UnityEngine;

namespace Banter.VisualScripting
{
    [UnitTitle("Set UI Visible")]
    [UnitShortTitle("Set UI Visible")]
    [UnitCategory("Banter\\UI\\Properties\\State")]
    [TypeIcon(typeof(BanterObjectId))]
    public class SetUIVisible : Unit
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
        public ValueInput visible;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var targetId = flow.GetValue<string>(elementId);
                var targetName = flow.GetValue<string>(elementName);
                var visibleValue = flow.GetValue<bool>(visible);

                // Resolve element name to ID if needed
                string elemId = UIElementResolverHelper.ResolveElementIdOrName(targetId, targetName);

                if (string.IsNullOrEmpty(elemId))
                {
                    Debug.LogWarning("[SetUIVisible] Element ID/Name is null or empty.");
                    return outputTrigger;
                }

                if (!UIPanelExtensions.ValidateElementForOperation(elemId, "SetUIVisible"))
                {
                    return outputTrigger;
                }

                try
                {
                    // Get the panel ID for message routing
                    var panelId = UIPanelExtensions.GetFormattedPanelIdByElementId(elemId);
                    if (panelId == null)
                    {
                        Debug.LogError($"[SetUIVisible] Could not resolve panel for element '{elemId}'");
                        return outputTrigger;
                    }
                    
                    // Format: panelId|SET_UI_PROPERTY|elementId§propertyName§value
                    var message = $"{panelId}{MessageDelimiters.PRIMARY}{UICommands.SET_UI_PROPERTY}{MessageDelimiters.PRIMARY}{elemId}{MessageDelimiters.SECONDARY}visible{MessageDelimiters.SECONDARY}{(visibleValue ? "1" : "0")}";
                    
                    // Send command through UIElementBridge
                    UIElementBridge.HandleMessage(message);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[SetUIVisible] Failed to set UI visible state: {e.Message}");
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            elementId = ValueInput<string>("Element ID", "");
            elementName = ValueInput<string>("Element Name", "");
            visible = ValueInput("Visible", true);
        }
    }
}
#endif
