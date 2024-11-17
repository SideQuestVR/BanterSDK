#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using UnityEngine;

namespace Banter.VisualScripting
{

    [UnitTitle("On Grab Event Received")]
    [UnitShortTitle("On Grab")]
    [UnitCategory("Events\\Banter\\BanterHeldEvents\\")]
    [TypeIcon(typeof(BanterObjectId))]
    public class OnGrab : EventUnit<CustomEventArgs>
    {
        [DoNotSerialize]
        [PortLabelHidden]
        [NullMeansSelf]
        public ValueInput banterHeldEvents { get; private set; }
        [DoNotSerialize]
        public ValueOutput isLeft;
        [DoNotSerialize]
        public ValueOutput grabPosition;

        protected override bool register => true;

        public override EventHook GetHook(GraphReference reference)
        {
            return new EventHook("OnGrab");
        }

        protected override void Definition()
        {
            base.Definition();
            // Setting the value on our port.
            banterHeldEvents = ValueInput<GameObject>(nameof(banterHeldEvents), null).NullMeansSelf();
            isLeft = ValueOutput<bool>("Is Left");
            grabPosition = ValueOutput<Vector3>("Point");
        }

        protected override bool ShouldTrigger(Flow flow, CustomEventArgs data)
        {
            return data.name == flow.GetValue<GameObject>(banterHeldEvents).GetInstanceID().ToString();
        }

        // Setting the value on our port.
        protected override void AssignArguments(Flow flow, CustomEventArgs data)
        {
            flow.SetValue(grabPosition, data.arguments[0]);
            flow.SetValue(isLeft, (HandSide)data.arguments[1] == HandSide.LEFT);
        }
    }
}
#endif
