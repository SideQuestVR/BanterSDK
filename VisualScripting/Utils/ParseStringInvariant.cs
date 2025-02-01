#if BANTER_VISUAL_SCRIPTING
using UnityEngine;
using Unity.VisualScripting;
using Banter.SDK;
using System.Linq;
using Banter.Utilities.Async;
using System.Globalization;

namespace Banter.VisualScripting
{
    [UnitTitle("String To Float Invariant Culture")]
    [UnitShortTitle("String To Float")]
    [UnitCategory("Banter")]
    [TypeIcon(typeof(BanterObjectId))]
    public class ParseStringInvariant : Unit
    {
        [DoNotSerialize]
        public ValueInput stringInput;

        [DoNotSerialize]
        public ValueOutput floatOutput;

        protected override void Definition()
        {
            stringInput = ValueInput("string", "");
            floatOutput = ValueOutput<float>("float", flow => {
                return float.Parse(flow.GetValue<string>(stringInput), CultureInfo.InvariantCulture);
            });
        }
    }
}
#endif
