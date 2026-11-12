using Unity.VisualScripting;
using BS;
using BS.UI.Bridge;
using BS.UI.Core;
using BS.VisualScripting.UI.Helpers;
using UnityEngine;

namespace BS.VisualScripting
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
    [UnitCategory("BS\\UI\\Properties")]
    [TypeIcon(typeof(BSObjectId))]
    public class SetUIProperty : Unit
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
        public ValueInput propertyName;

        [DoNotSerialize]
        public ValueInput propertyValue;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) =>
            {
                var targetId = flow.GetValue<string>(elementId);
                var targetName = flow.GetValue<string>(elementName);
                var propName = flow.GetValue<UIPropertyNameVS>(propertyName);
                var propValue = flow.GetValue<object>(propertyValue);

                // Resolve element name to ID if needed
                string elemId = UIElementResolverHelper.ResolveElementIdOrName(targetId, targetName);

                if (!UIPanelExtensions.ValidateElementForOperation(elemId, "SetUIProperty"))
                {
                    return outputTrigger;
                }

                try
                {
                    // Get the formatted panel ID using UIElementResolver
                    var panelId = UIPanelExtensions.GetFormattedPanelIdByElementId(elemId);
                    if (panelId == null)
                    {
                        Debug.LogError($"[SetUIProperty] Could not resolve panel for element '{elemId}'");
                        return outputTrigger;
                    }

                    // Convert enum to property name and format value
                    var propNameStr = GetPropertyName(propName);
                    var valueStr = FormatPropertyValue(propValue, propName);

                    // Format: panelId|SET_UI_PROPERTY|elementId§propertyName§value
                    var message = $"{panelId}{MessageDelimiters.PRIMARY}{UICommands.SET_UI_PROPERTY}{MessageDelimiters.PRIMARY}{elemId}{MessageDelimiters.SECONDARY}{propNameStr}{MessageDelimiters.SECONDARY}{valueStr}";

                    // Send command through UIElementBridge
                    UIElementBridge.HandleMessage(message);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[SetUIProperty] Failed to set UI property: {e.Message}");
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            elementId = ValueInput<string>("Element ID", "");
            elementName = ValueInput<string>("Element Name", "");
            propertyName = ValueInput("Property", UIPropertyNameVS.Text);
            propertyValue = ValueInput<object>("Value");
        }

        // The element setters match these case-sensitively (BSUIScrollView's scrollDecelerationRate, the bridge's
        // horizontalScrolling); the lowercase spellings never applied. min/maxvalue are the bridge's Slider spellings.
        private string GetPropertyName(UIPropertyNameVS propName)
        {
            return propName switch
            {
                UIPropertyNameVS.Checked => "checked",
                UIPropertyNameVS.Elasticity => "elasticity",
                UIPropertyNameVS.Enabled => "enabled",
                UIPropertyNameVS.Horizontalscrolling => "horizontalScrolling",
                UIPropertyNameVS.Maxvalue => "maxvalue",
                UIPropertyNameVS.Minvalue => "minvalue",
                UIPropertyNameVS.Name => "name",
                UIPropertyNameVS.Scrolldecelerationrate => "scrollDecelerationRate",
                UIPropertyNameVS.Scrollposition => "scrollPosition",
                UIPropertyNameVS.Text => "text",
                UIPropertyNameVS.Tooltip => "tooltip",
                UIPropertyNameVS.Value => "value",
                UIPropertyNameVS.Verticalscrolling => "verticalScrolling",
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
                UIPropertyNameVS.Value => FormatNumber(value, "0"),
                UIPropertyNameVS.Maxvalue => FormatNumber(value, "100"),
                UIPropertyNameVS.Minvalue => FormatNumber(value, "0"),
                UIPropertyNameVS.Elasticity => FormatNumber(value, "0.1"),
                UIPropertyNameVS.Scrolldecelerationrate => FormatNumber(value, "0.135"),
                // "x,y", invariant: Vector2.ToString() is "(1.00, 2.00)", which the setter cannot read.
                UIPropertyNameVS.Scrollposition when value is Vector2 position =>
                    NumberFormat.Format(position.x) + "," + NumberFormat.Format(position.y),
                _ => value.ToString(), // String values
            };
        }

        /// <summary>
        /// A number of any type (int, double, float, long ...), a bool as 1/0, or a numeric string, written
        /// culture-invariant because the bridge parses invariant. Anything else, NaN, an infinity or a number
        /// outside float's range sends <paramref name="fallback"/>.
        /// </summary>
        public static string FormatNumber(object value, string fallback)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            switch (value)
            {
                case bool b:
                    return b ? "1" : "0";
                case string s:
                    // Sent as written, so "10000000" stays digits for an integer slider instead of a float's "1E+07".
                    // NumberStyles.Float: a comma-decimal "1,5" falls back instead of reading as 15.
                    s = s.Trim();
                    return NumberFormat.TryParse(s, out var parsed) && IsSendable(parsed) ? s : fallback;
                case float or double or decimal or int or long or short or byte or sbyte or uint or ulong or ushort:
                    // Each type's own invariant text: an int stays digits instead of a float's 1.677722E+07.
                    return IsSendable(System.Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture))
                        ? ((System.IFormattable)value).ToString(null, ci)
                        : fallback;
                default:
                    return fallback;
            }
        }

        // A slider can't hold NaN or an infinity, and the bridge's float parse overflows past float's range.
        private static bool IsSendable(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value) && System.Math.Abs(value) <= float.MaxValue;
    }
}
