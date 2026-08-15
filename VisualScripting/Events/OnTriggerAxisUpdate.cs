#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using BS;

namespace BS.VisualScripting
{
    [UnitTitle("On Trigger Axis Update")]
    [UnitShortTitle("Trigger Update")]
    [UnitCategory("Events\\BS\\Controller")]
    [TypeIcon(typeof(BSObjectId))]
    public class OnTriggerAxisUpdate : EventUnit<CustomEventArgs>
    {
        [DoNotSerialize]
        public ValueOutput handSide;

        [DoNotSerialize]
        public ValueOutput triggerValue;

        protected override bool register => true;

        public override EventHook GetHook(GraphReference reference)
        {
            return new EventHook("OnTriggerAxisUpdate");
        }

        protected override void Definition()
        {
            base.Definition();
            handSide = ValueOutput<int>("Hand Side");
            triggerValue = ValueOutput<float>("Trigger Value");
        }

        protected override bool ShouldTrigger(Flow flow, CustomEventArgs data)
        {
            return true;
        }

        protected override void AssignArguments(Flow flow, CustomEventArgs data)
        {
            flow.SetValue(handSide, data.arguments[0]);
            flow.SetValue(triggerValue, data.arguments[1]);
        }
    }
}
#endif