using Unity.VisualScripting;
using UnityEngine;

namespace BS.VisualScripting
{
    /// <summary>Sets the toggle state of the BS World Option on the target GameObject.</summary>
    [UnitTitle("Set World Option")]
    [UnitShortTitle("Set World Option")]
    [UnitCategory("BS\\Space")]
    [TypeIcon(typeof(BSObjectId))]
    public class SetWorldOption : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        [PortLabelHidden]
        [NullMeansSelf]
        public ValueInput worldOption { get; private set; }

        [DoNotSerialize]
        public ValueInput isOn;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) =>
            {
                var option = flow.GetValue<GameObject>(worldOption).GetComponent<BSWorldOption>();
                if (option != null) option.IsOn = flow.GetValue<bool>(isOn);
                return outputTrigger;
            });
            outputTrigger = ControlOutput("");
            worldOption = ValueInput<GameObject>(nameof(worldOption), null).NullMeansSelf();
            isOn = ValueInput<bool>("Is On", false);
            Succession(inputTrigger, outputTrigger);
        }
    }
}
