#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Bridge;
using Banter.UI.Core;
using Banter.VisualScripting.UI.Helpers;
using UnityEngine;

namespace Banter.VisualScripting
{
    [UnitTitle("Set UI Text")]
    [UnitShortTitle("Set UI Text")]
    [UnitCategory("Banter\\UI\\Properties\\Text")]
    [TypeIcon(typeof(BanterObjectId))]
    public class SetUIText : Unit
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
        public ValueInput text;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                Debug.Log("[SetUIText] ========== TRIGGERED ==========");

                var targetId = flow.GetValue<string>(elementId);
                var targetName = flow.GetValue<string>(elementName);
                var textValue = flow.GetValue<string>(text);

                Debug.Log($"[SetUIText] Input values: targetId='{targetId}', targetName='{targetName}', textValue='{textValue}'");

                string elemId = UIElementResolverHelper.ResolveElementIdOrName(targetId, targetName);
                Debug.Log($"[SetUIText] Resolved element ID: '{elemId}'");

                if (!UIPanelExtensions.ValidateElementForOperation(elemId, "SetUIText"))
                {
                    Debug.LogError($"[SetUIText] Validation FAILED for element '{elemId}'");
                    return outputTrigger;
                }

                Debug.Log($"[SetUIText] Validation PASSED for element '{elemId}'");

                try
                {
                    // Get the formatted panel ID using UIElementResolver
                    Debug.Log($"[SetUIText] Getting panel ID for element '{elemId}'");
                    var panelId = UIPanelExtensions.GetFormattedPanelIdByElementId(elemId);
                    if (panelId == null)
                    {
                        Debug.LogError($"[SetUIText] Could not resolve panel for element '{elemId}'");
                        return outputTrigger;
                    }

                    Debug.Log($"[SetUIText] Got panel ID: '{panelId}'");

                    // Format: panelId|SET_UI_PROPERTY|elementId§propertyName§value
                    var message = $"{panelId}{MessageDelimiters.PRIMARY}{UICommands.SET_UI_PROPERTY}{MessageDelimiters.PRIMARY}{elemId}{MessageDelimiters.SECONDARY}text{MessageDelimiters.SECONDARY}{textValue ?? ""}";

                    Debug.Log($"[SetUIText] Sending message: '{message}'");

                    // Send command through UIElementBridge
                    UIElementBridge.HandleMessage(message);

                    Debug.Log($"[SetUIText] SUCCESS - Text set to '{textValue}' for element '{elemId}'");
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[SetUIText] EXCEPTION: {e.Message}\n{e.StackTrace}");
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            elementId = ValueInput<string>("Element ID", "");
            elementName = ValueInput<string>("Element Name", "");
            text = ValueInput("Text", "");
        }
    }
}
#endif