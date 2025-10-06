#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Bridge;
using Banter.UI.Core;
using UnityEngine;
using Banter.VisualScripting.UI.Helpers;

namespace Banter.VisualScripting
{
    [UnitTitle("Set UI Style")]
    [UnitShortTitle("Set UI Style")]
    [UnitCategory("Banter\\UI\\Styles")]
    [TypeIcon(typeof(BanterObjectId))]
    public class SetUIStyle : Unit
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
        public ValueInput styleProperty;

        [DoNotSerialize]
        public ValueInput styleValue;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var targetId = flow.GetValue<string>(elementId);
                var targetName = flow.GetValue<string>(elementName);

                // Resolve element name to ID if needed
                string elemId = UIElementResolverHelper.ResolveElementIdOrName(targetId, targetName);
                var property = flow.GetValue<UIStyleProperty>(styleProperty);
                var value = flow.GetValue<string>(styleValue);

                if (!UIPanelExtensions.ValidateElementForOperation(elemId, "SetUIStyle"))
                {
                    return outputTrigger;
                }

                try
                {
                    var panelId = UIPanelExtensions.GetFormattedPanelIdByElementId(elemId);
                    if (panelId == null)
                    {
                        Debug.LogError($"[SetUIStyle] Could not resolve panel for element '{elemId}'");
                        return outputTrigger;
                    }
                    
                    // Convert the UIStyleProperty enum to its USS property name
                    var propertyName = property.ToUSSName();
                    
                    // Format: panelId|SET_UI_STYLE|elementId§styleName§value
                    var message = $"{panelId}{MessageDelimiters.PRIMARY}{UICommands.SET_UI_STYLE}{MessageDelimiters.PRIMARY}{elemId}{MessageDelimiters.SECONDARY}{propertyName}{MessageDelimiters.SECONDARY}{value ?? ""}";
                    
                    // Send command through UIElementBridge
                    UIElementBridge.HandleMessage(message);

                    Debug.Log($"[SetUIStyle] Set style '{propertyName}' = '{value}' on element '{elemId}'");
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[SetUIStyle] Failed to set UI style: {e.Message}");
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            elementId = ValueInput<string>("Element ID", "");
            elementName = ValueInput<string>("Element Name", "");
            styleProperty = ValueInput("Style Property", UIStyleProperty.BackgroundColor);
            styleValue = ValueInput("Style Value", "");
        }
    }
}
#endif