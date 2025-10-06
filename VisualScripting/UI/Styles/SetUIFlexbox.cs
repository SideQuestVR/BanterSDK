#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Bridge;
using Banter.UI.Core;
using UnityEngine;
using Banter.VisualScripting.UI.Helpers;

namespace Banter.VisualScripting
{
    public enum UIFlexDirection
    {
        Column,
        ColumnReverse,
        Row,
        RowReverse
    }

    public enum UIJustifyContent
    {
        FlexStart,
        FlexEnd,
        Center,
        SpaceBetween,
        SpaceAround,
        SpaceEvenly
    }

    public enum UIAlignItems
    {
        FlexStart,
        FlexEnd,
        Center,
        Stretch
    }

    public enum UIFlexWrap
    {
        NoWrap,
        Wrap,
        WrapReverse
    }

    [UnitTitle("Set UI Flexbox")]
    [UnitShortTitle("Set UI Flexbox")]
    [UnitCategory("Banter\\UI\\Styles\\Layout")]
    [TypeIcon(typeof(BanterObjectId))]
    public class SetUIFlexbox : Unit
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
        public ValueInput flexDirection;

        [DoNotSerialize]
        public ValueInput justifyContent;

        [DoNotSerialize]
        public ValueInput alignItems;

        [DoNotSerialize]
        public ValueInput flexWrap;

        [DoNotSerialize]
        public ValueInput flexGrow;

        [DoNotSerialize]
        public ValueInput flexShrink;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var targetId = flow.GetValue<string>(elementId);
                var targetName = flow.GetValue<string>(elementName);

                // Resolve element name to ID if needed
                string elemId = UIElementResolverHelper.ResolveElementIdOrName(targetId, targetName);
                var flexDir = flow.GetValue<UIFlexDirection>(flexDirection);
                var justifyAlign = flow.GetValue<UIJustifyContent>(justifyContent);
                var alignItemsVal = flow.GetValue<UIAlignItems>(alignItems);
                var flexWrapVal = flow.GetValue<UIFlexWrap>(flexWrap);
                var flexGrowVal = flow.GetValue<float>(flexGrow);
                var flexShrinkVal = flow.GetValue<float>(flexShrink);

                if (!UIPanelExtensions.ValidateElementForOperation(elemId, "SetUIFlexbox"))
                {
                    return outputTrigger;
                }

                try
                {
                    var panelId = UIPanelExtensions.GetFormattedPanelIdByElementId(elemId);
                    if (panelId == null)
                    {
                        Debug.LogError($"[SetUIFlexbox] Could not resolve panel for element '{elemId}'");
                        return outputTrigger;
                    }
                    
                    // Set flex-direction
                    string flexDirValue = flexDir switch
                    {
                        UIFlexDirection.Column => "column",
                        UIFlexDirection.ColumnReverse => "column-reverse",
                        UIFlexDirection.Row => "row",
                        UIFlexDirection.RowReverse => "row-reverse",
                        _ => "column"
                    };
                    SendStyleCommand(panelId, elemId, "flex-direction", flexDirValue);
                    
                    // Set justify-content
                    string justifyValue = justifyAlign switch
                    {
                        UIJustifyContent.FlexStart => "flex-start",
                        UIJustifyContent.FlexEnd => "flex-end",
                        UIJustifyContent.Center => "center",
                        UIJustifyContent.SpaceBetween => "space-between",
                        UIJustifyContent.SpaceAround => "space-around",
                        UIJustifyContent.SpaceEvenly => "space-evenly",
                        _ => "flex-start"
                    };
                    SendStyleCommand(panelId, elemId, "justify-content", justifyValue);
                    
                    // Set align-items
                    string alignValue = alignItemsVal switch
                    {
                        UIAlignItems.FlexStart => "flex-start",
                        UIAlignItems.FlexEnd => "flex-end",
                        UIAlignItems.Center => "center",
                        UIAlignItems.Stretch => "stretch",
                        _ => "stretch"
                    };
                    SendStyleCommand(panelId, elemId, "align-items", alignValue);
                    
                    // Set flex-wrap
                    string wrapValue = flexWrapVal switch
                    {
                        UIFlexWrap.NoWrap => "nowrap",
                        UIFlexWrap.Wrap => "wrap",
                        UIFlexWrap.WrapReverse => "wrap-reverse",
                        _ => "nowrap"
                    };
                    SendStyleCommand(panelId, elemId, "flex-wrap", wrapValue);
                    
                    // Set flex-grow and flex-shrink
                    SendStyleCommand(panelId, elemId, "flex-grow", flexGrowVal.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    SendStyleCommand(panelId, elemId, "flex-shrink", flexShrinkVal.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[SetUIFlexbox] Failed to set UI flexbox: {e.Message}");
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            elementId = ValueInput<string>("Element ID", "");
            elementName = ValueInput<string>("Element Name", "");
            flexDirection = ValueInput("Flex Direction", UIFlexDirection.Column);
            justifyContent = ValueInput("Justify Content", UIJustifyContent.FlexStart);
            alignItems = ValueInput("Align Items", UIAlignItems.Stretch);
            flexWrap = ValueInput("Flex Wrap", UIFlexWrap.NoWrap);
            flexGrow = ValueInput("Flex Grow", 0f);
            flexShrink = ValueInput("Flex Shrink", 1f);
        }

        private void SendStyleCommand(string panelId, string elementId, string styleName, string value)
        {
            var message = $"{panelId}{MessageDelimiters.PRIMARY}{UICommands.SET_UI_STYLE}{MessageDelimiters.PRIMARY}{elementId}{MessageDelimiters.SECONDARY}{styleName}{MessageDelimiters.SECONDARY}{value}";
            UIElementBridge.HandleMessage(message);
        }
    }
}
#endif