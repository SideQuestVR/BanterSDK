#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using UnityEngine;

namespace Banter.VisualScripting
{
    [UnitTitle("Color: TryParseHtmlString")]
    [UnitShortTitle("TryParseHtmlString")]
    [UnitCategory("Banter")]
    [TypeIcon(typeof(Color))]
    public class ColorUtilityTryParseHtmlString : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput hexColor;

        [DoNotSerialize]
        public ValueOutput color;

        private Color outputColor;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => 
            {
                var colString = flow.GetValue<string>(hexColor);
                if (colString[0] != '#')
                {
                    colString = "#" + colString;
                }
                ColorUtility.TryParseHtmlString(flow.GetValue<string>(hexColor), out var returnCol);
                outputColor = returnCol;
                return outputTrigger;
            });

            outputTrigger = ControlOutput("");

            hexColor = ValueInput("Hex code", "#000000");
            color = ValueOutput("Color", (flow) => outputColor);
        }
    }
}
#endif
