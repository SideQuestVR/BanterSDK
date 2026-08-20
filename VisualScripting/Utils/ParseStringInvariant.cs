using UnityEngine;
using Unity.VisualScripting;
using BS;
using System.Linq;
using BS.Utilities.Async;
using System.Globalization;

namespace BS.VisualScripting
{
    [UnitTitle("String To Float Invariant Culture")]
    [UnitShortTitle("String To Float")]
    [UnitCategory("BS\\Utils")]
    [TypeIcon(typeof(BSObjectId))]
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
