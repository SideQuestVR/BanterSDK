#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Bridge;
using Banter.UI.Core;
using Banter.VisualScripting.UI.Helpers;
using UnityEngine;

namespace Banter.VisualScripting
{
    [UnitTitle("Detach UI Child")]
    [UnitShortTitle("Detach UI Child")]
    [UnitCategory("Banter\\UI\\Hierarchy")]
    [TypeIcon(typeof(BanterObjectId))]
    public class DetachUIChild : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput parentElementId;

        [DoNotSerialize]
        public ValueInput parentElementName;

        [DoNotSerialize]
        public ValueInput childElementId;

        [DoNotSerialize]
        public ValueInput childElementName;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var parentId = flow.GetValue<string>(parentElementId);
                var parentName = flow.GetValue<string>(parentElementName);
                var childId = flow.GetValue<string>(childElementId);
                var childName = flow.GetValue<string>(childElementName);

                // Resolve parent element name to ID if needed
                string parentElemId = UIElementResolverHelper.ResolveElementIdOrName(parentId, parentName);

                // Resolve child element name to ID if needed
                string childElemId = UIElementResolverHelper.ResolveElementIdOrName(childId, childName);

                if (!UIPanelExtensions.ValidateElementForOperation(childElemId, "DetachUIChild"))
                {
                    return outputTrigger;
                }

                try
                {
                    // Get the formatted panel ID using UIElementResolver (from child or parent)
                    var panelId = UIPanelExtensions.GetFormattedPanelIdByElementId(childElemId);
                    if (panelId == null)
                    {
                        Debug.LogError($"[DetachUIChild] Could not resolve panel for child element '{childElemId}'");
                        return outputTrigger;
                    }

                    // Format: panelId|DETACH_UI_CHILD|parentId§childId
                    var message = $"{panelId}{MessageDelimiters.PRIMARY}{UICommands.DETACH_UI_CHILD}{MessageDelimiters.PRIMARY}{parentElemId}{MessageDelimiters.SECONDARY}{childElemId}";

                    // Send command through UIElementBridge
                    UIElementBridge.HandleMessage(message);

                    Debug.Log($"[DetachUIChild] Detached child '{childElemId}' from parent '{parentElemId}'");
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[DetachUIChild] Failed to detach child: {e.Message}");
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            parentElementId = ValueInput<string>("Parent Element ID", "");
            parentElementName = ValueInput<string>("Parent Element Name", "");
            childElementId = ValueInput<string>("Child Element ID", "");
            childElementName = ValueInput<string>("Child Element Name", "");
        }
    }
}
#endif
