#if BANTER_VISUAL_SCRIPTING
using UnityEngine;
using Unity.VisualScripting;
using BS;
using System.Linq;
using BS.Utilities.Async;
using System.Globalization;

namespace BS.VisualScripting
{
    [UnitTitle("Float To String Invariant Culture")]
    [UnitShortTitle("FloatToString")]
    [UnitCategory("BS\\Utils")]
    [TypeIcon(typeof(BSObjectId))]
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