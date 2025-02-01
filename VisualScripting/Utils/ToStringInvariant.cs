#if BANTER_VISUAL_SCRIPTING
using UnityEngine;
using Unity.VisualScripting;
using Banter.SDK;
using System.Linq;
using Banter.Utilities.Async;
using System.Globalization;

namespace Banter.VisualScripting
{
    [UnitTitle("Float To String Invariant Culture")]
    [UnitShortTitle("FloatToString")]
    [UnitCategory("Banter")]
    [TypeIcon(typeof(BanterObjectId))]
    public class ToStringInvariant : Unit
    {
        [DoNotSerialize]
        public ValueInput floatInput;

        [DoNotSerialize]
        public ValueOutput stringOutput;

        protected override void Definition()
        {
            floatInput = ValueInput("float", 0f);
            stringOutput = ValueOutput("string", flow => {
                return flow.GetValue<float>(floatInput).ToString(CultureInfo.InvariantCulture);
            });
        }
    }
}
#endif