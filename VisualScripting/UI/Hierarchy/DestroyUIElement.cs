#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using BS;
using BS.UI.Bridge;
using BS.UI.Core;
using BS.VisualScripting.UI.Helpers;
using UnityEngine;

namespace BS.VisualScripting
{
    [UnitTitle("Destroy UI Element")]
    [UnitShortTitle("Destroy UI Element")]
    [UnitCategory("BS\\UI\\Hierarchy")]
    [TypeIcon(typeof(BSObjectId))]
    public class DestroyUIElement : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput elementId;

        [DoNotSerialize]
        public ValueInput elementName;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var targetId = flow.GetValue<string>(elementId);
                var targetName = flow.GetValue<string>(elementName);

                // Resolve element name to ID if needed
                string elemId = UIElementResolverHelper.ResolveElementIdOrName(targetId, targetName);

                if (!UIPanelExtensions.ValidateElementForOperation(elemId, "DestroyUIElement"))
                {
                    return outputTrigger;
                }

                try
                {
                    // Get the formatted panel ID using UIElementResolver
                    var panelId = UIPanelExtensions.GetFormattedPanelIdByElementId(elemId);
                    if (panelId == null)
                    {
                        Debug.LogError($"[DestroyUIElement] Could not resolve panel for element '{elemId}'");
                        return outputTrigger;
                    }

                    // Format: panelId|DESTROY_UI_ELEMENT|elementId
                    var message = $"{panelId}{MessageDelimiters.PRIMARY}{UICommands.DESTROY_UI_ELEMENT}{MessageDelimiters.PRIMARY}{elemId}";

                    // Send command through UIElementBridge
                    UIElementBridge.HandleMessage(message);

#if BANTER_UI_DEBUG
                    Debug.Log($"[DestroyUIElement] Destroyed element '{elemId}'");
#endif
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[DestroyUIElement] Failed to destroy element '{elemId}': {e.Message}");
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            elementId = ValueInput<string>("Element ID", "");
            elementName = ValueInput<string>("Element Name", "");
        }
    }
}
#endif
