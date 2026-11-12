using Unity.VisualScripting;
using UnityEngine;

namespace BS.VisualScripting
{
    /// <summary>Fires when the local player presses the BS World Option on the target GameObject.</summary>
    [UnitTitle("On World Option")]
    [UnitShortTitle("On World Option")]
    [UnitCategory("Events\\BS\\Space")]
    [TypeIcon(typeof(BSObjectId))]
    public class OnWorldOption : EventUnit<CustomEventArgs>
    {
        [DoNotSerialize]
        [PortLabelHidden]
        [NullMeansSelf]
        public ValueInput worldOption { get; private set; }

        [DoNotSerialize]
        public ValueOutput isOn;

        protected override bool register => true;

        public override EventHook GetHook(GraphReference reference)
        {
            return new EventHook("OnWorldOption");
        }

        protected override void Definition()
        {
            base.Definition();
            worldOption = ValueInput<GameObject>(nameof(worldOption), null).NullMeansSelf();
            isOn = ValueOutput<bool>("Is On");
        }

        protected override bool ShouldTrigger(Flow flow, CustomEventArgs data)
        {
            return data.name == BSScene.UnityId(flow.GetValue<GameObject>(worldOption)).ToString();
        }

        protected override void AssignArguments(Flow flow, CustomEventArgs data)
        {
            flow.SetValue(isOn, data.arguments[0]);
        }
    }
}
