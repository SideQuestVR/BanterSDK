#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Bridge;
using Banter.UI.Core;
using Banter.VisualScripting.UI.Helpers;
using UnityEngine;

namespace Banter.VisualScripting
{
    [UnitTitle("Attach UI Child")]
    [UnitShortTitle("Attach UI Child")]
    [UnitCategory("Banter\\UI\\Hierarchy")]
    [TypeIcon(typeof(BanterObjectId))]
    public class AttachUIChild : Unit
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

        [DoNotSerialize]
        public ValueInput index;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var parentId = flow.GetValue<string>(parentElementId);
                var parentName = flow.GetValue<string>(parentElementName);
                var childId = flow.GetValue<string>(childElementId);
                var childName = flow.GetValue<string>(childElementName);
                var insertIndex = flow.GetValue<int>(index);

                // Resolve parent element name to ID if needed
                string parentElemId = UIElementResolverHelper.ResolveElementIdOrName(parentId, parentName);

                // Resolve child element name to ID if needed
                string childElemId = UIElementResolverHelper.ResolveElementIdOrName(childId, childName);

                if (!UIPanelExtensions.ValidateElementForOperation(parentElemId, "AttachUIChild (parent)"))
                {
                    return outputTrigger;
                }

                if (!UIPanelExtensions.ValidateElementForOperation(childElemId, "AttachUIChild (child)"))
                {
                    return outputTrigger;
                }

                try
                {
                    // Get the formatted panel ID using UIElementResolver (from parent)
                    var panelId = UIPanelExtensions.GetFormattedPanelIdByElementId(parentElemId);
                    if (panelId == null)
                    {
                        Debug.LogError($"[AttachUIChild] Could not resolve panel for parent element '{parentElemId}'");
                        return outputTrigger;
                    }

                    // Format: panelId|ATTACH_UI_CHILD|parentId§childId§index
                    var message = $"{panelId}{MessageDelimiters.PRIMARY}{UICommands.ATTACH_UI_CHILD}{MessageDelimiters.PRIMARY}{parentElemId}{MessageDelimiters.SECONDARY}{childElemId}{MessageDelimiters.SECONDARY}{insertIndex}";

                    // Send command through UIElementBridge
                    UIElementBridge.HandleMessage(message);

#if BANTER_UI_DEBUG
                    Debug.Log($"[AttachUIChild] Attached child '{childElemId}' to parent '{parentElemId}' at index {insertIndex}");
#endif
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[AttachUIChild] Failed to attach child: {e.Message}");
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            parentElementId = ValueInput<string>("Parent Element ID", "");
            parentElementName = ValueInput<string>("Parent Element Name", "");
            childElementId = ValueInput<string>("Child Element ID", "");
            childElementName = ValueInput<string>("Child Element Name", "");
            index = ValueInput<int>("Index", -1);
        }
    }
}
#endif
