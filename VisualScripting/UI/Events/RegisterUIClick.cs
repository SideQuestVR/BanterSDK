#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Bridge;
using Banter.UI.Core;
using UnityEngine;

namespace Banter.VisualScripting
{
    [UnitTitle("Register UI Click")]
    [UnitShortTitle("Register UI Click")]
    [UnitCategory("Banter\\UI\\Events")]
    [TypeIcon(typeof(BanterObjectId))]
    public class RegisterUIClick : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput elementId;

        [DoNotSerialize]
        public ValueOutput success;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var elemId = flow.GetValue<string>(elementId);
                if (!UIPanelExtensions.ValidateElementForOperation(elemId, "RegisterUIClick"))
                {
                    flow.SetValue(success, false);
                    return outputTrigger;
                }

                try
                {
                    // Get the formatted panel ID using UIElementResolver
                    var panelId = UIPanelExtensions.GetFormattedPanelIdByElementId(elemId);
                    if (panelId == null)
                    {
                        Debug.LogError($"[RegisterUIClick] Could not resolve panel for element '{elemId}'");
                        flow.SetValue(success, false);
                        return outputTrigger;
                    }
                    
                    // Format: panelId|REGISTER_UI_EVENT|elementId§eventType
                    var message = $"{panelId}{MessageDelimiters.PRIMARY}{UICommands.REGISTER_UI_EVENT}{MessageDelimiters.PRIMARY}{elemId}{MessageDelimiters.SECONDARY}click";
                    
                    // Send command through UIElementBridge
                    UIElementBridge.HandleMessage(message);

                    flow.SetValue(success, true);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[RegisterUIClick] Failed to register UI click event: {e.Message}");
                    flow.SetValue(success, false);
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            elementId = ValueInput<string>("Element ID");
            success = ValueOutput<bool>("Success");
        }
    }
}
#endif