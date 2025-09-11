#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Bridge;
using Banter.UI.Core;
using UnityEngine;

namespace Banter.VisualScripting
{
    public enum UIPropertyNameVS
    {
        Checked,
        Elasticity,
        Enabled,
        Horizontalscrolling,
        Maxvalue,
        Minvalue,
        Name,
        Scrolldecelerationrate,
        Scrollposition,
        Text,
        Tooltip,
        Value,
        Verticalscrolling,
        Visible
    }

    [UnitTitle("Set UI Property")]
    [UnitShortTitle("Set UI Property")]
    [UnitCategory("Banter\\UI\\Properties")]
    [TypeIcon(typeof(BanterObjectId))]
    public class SetUIProperty : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput elementId;

        [DoNotSerialize]
        public ValueInput propertyName;

        [DoNotSerialize]
        public ValueInput propertyValue;

        [DoNotSerialize]
        public ValueOutput success;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var elemId = flow.GetValue<string>(elementId);
                var propName = flow.GetValue<UIPropertyNameVS>(propertyName);
                var propValue = flow.GetValue<object>(propertyValue);
                if (!UIPanelExtensions.ValidateElementForOperation(elemId, "SetUIProperty"))
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
                        Debug.LogError($"[SetUIProperty] Could not resolve panel for element '{elemId}'");
                        flow.SetValue(success, false);
                        return outputTrigger;
                    }
                    
                    // Convert enum to property name and format value
                    var propNameStr = GetPropertyName(propName);
                    var valueStr = FormatPropertyValue(propValue, propName);
                    
                    // Format: panelId|SET_UI_PROPERTY|elementId§propertyName§value
                    var message = $"{panelId}{MessageDelimiters.PRIMARY}{UICommands.SET_UI_PROPERTY}{MessageDelimiters.PRIMARY}{elemId}{MessageDelimiters.SECONDARY}{propNameStr}{MessageDelimiters.SECONDARY}{valueStr}";
                    
                    // Send command through UIElementBridge
                    UIElementBridge.HandleMessage(message);

                    flow.SetValue(success, true);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[SetUIProperty] Failed to set UI property: {e.Message}");
                    flow.SetValue(success, false);
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            elementId = ValueInput<string>("Element ID");
            propertyName = ValueInput("Property", UIPropertyNameVS.Text);
            propertyValue = ValueInput<object>("Value");
            success = ValueOutput<bool>("Success");
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

        private string FormatPropertyValue(object value, UIPropertyNameVS propName)
        {
            if (value == null) return "";

            return propName switch
            {
                UIPropertyNameVS.Checked => value is bool b ? (b ? "1" : "0") : "0",
                UIPropertyNameVS.Enabled => value is bool e ? (e ? "1" : "0") : "0",
                UIPropertyNameVS.Visible => value is bool v ? (v ? "1" : "0") : "0",
                UIPropertyNameVS.Value => value is float f ? f.ToString(System.Globalization.CultureInfo.InvariantCulture) : "0",
                UIPropertyNameVS.Maxvalue => value is float max ? max.ToString(System.Globalization.CultureInfo.InvariantCulture) : "100",
                UIPropertyNameVS.Minvalue => value is float min ? min.ToString(System.Globalization.CultureInfo.InvariantCulture) : "0",
                UIPropertyNameVS.Elasticity => value is float el ? el.ToString(System.Globalization.CultureInfo.InvariantCulture) : "0.1",
                UIPropertyNameVS.Scrolldecelerationrate => value is float dr ? dr.ToString(System.Globalization.CultureInfo.InvariantCulture) : "0.135",
                _ => value.ToString(), // String values
            };
        }
    }
}
#endif