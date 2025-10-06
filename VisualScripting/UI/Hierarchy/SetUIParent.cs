#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Bridge;
using Banter.UI.Core;
using Banter.VisualScripting.UI.Helpers;
using UnityEngine;

namespace Banter.VisualScripting
{
    [UnitTitle("Set UI Parent")]
    [UnitShortTitle("Set UI Parent")]
    [UnitCategory("Banter\\UI\\Hierarchy")]
    [TypeIcon(typeof(BanterObjectId))]
    public class SetUIParent : Unit
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
        public ValueInput newParentId;

        [DoNotSerialize]
        public ValueInput newParentName;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var targetId = flow.GetValue<string>(elementId);
                var targetName = flow.GetValue<string>(elementName);
                var parentId = flow.GetValue<string>(newParentId);
                var parentName = flow.GetValue<string>(newParentName);

                // Resolve element name to ID if needed
                string elemId = UIElementResolverHelper.ResolveElementIdOrName(targetId, targetName);

                // Resolve parent element name to ID if needed
                string parentElemId = UIElementResolverHelper.ResolveElementIdOrName(parentId, parentName);

                if (!UIPanelExtensions.ValidateElementForOperation(elemId, "SetUIParent"))
                {
                    return outputTrigger;
                }

                if (!UIPanelExtensions.ValidateElementForOperation(parentElemId, "SetUIParent (new parent)"))
                {
                    return outputTrigger;
                }

                try
                {
                    // Get the formatted panel ID using UIElementResolver
                    var panelId = UIPanelExtensions.GetFormattedPanelIdByElementId(elemId);
                    if (panelId == null)
                    {
                        Debug.LogError($"[SetUIParent] Could not resolve panel for element '{elemId}'");
                        return outputTrigger;
                    }

                    // Format: panelId|SET_UI_PARENT|elementId§newParentId
                    // Note: SET_UI_PARENT command may not be fully implemented in backend yet
                    // If it doesn't work, this is essentially the same as AttachUIChild
                    var message = $"{panelId}{MessageDelimiters.PRIMARY}{UICommands.SET_UI_PARENT}{MessageDelimiters.PRIMARY}{elemId}{MessageDelimiters.SECONDARY}{parentElemId}";

                    // Send command through UIElementBridge
                    UIElementBridge.HandleMessage(message);

                    Debug.Log($"[SetUIParent] Set parent of '{elemId}' to '{parentElemId}'");
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[SetUIParent] Failed to set parent: {e.Message}");
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            elementId = ValueInput<string>("Element ID", "");
            elementName = ValueInput<string>("Element Name", "");
            newParentId = ValueInput<string>("New Parent ID", "");
            newParentName = ValueInput<string>("New Parent Name", "");
        }
    }
}
#endif
