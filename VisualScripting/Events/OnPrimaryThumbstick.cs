#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using UnityEngine;

namespace Banter.VisualScripting
{

    [UnitTitle("On PrimaryThumbstick")]
    [UnitShortTitle("On PrimaryThumbstick")]
    [UnitCategory("Events\\Banter\\HeldEvents")]
    [TypeIcon(typeof(BanterObjectId))]
    public class OnPrimaryThumbstick : EventUnit<CustomEventArgs>
    {
        [DoNotSerialize]
        [PortLabelHidden]
        [NullMeansSelf]
        public ValueInput banterHeldEvents { get; private set; }
        [DoNotSerialize]
        public ValueOutput isLeft;
        [DoNotSerialize]
        public ValueOutput input;

        protected override bool register => true;

        public override EventHook GetHook(GraphReference reference)
        {
            return new EventHook("OnPrimaryThumbstick");
        }

        protected override void Definition()
        {
            base.Definition();
            // Setting the value on our port.
            banterHeldEvents = ValueInput<GameObject>(nameof(banterHeldEvents), null).NullMeansSelf();
            isLeft = ValueOutput<bool>("Is Left");
            input = ValueOutput<Vector2>("Input");
        }

        protected override bool ShouldTrigger(Flow flow, CustomEventArgs data)
        {
            return data.name == flow.GetValue<GameObject>(banterHeldEvents).GetInstanceID().ToString();
        }

        // Setting the value on our port.
        protected override void AssignArguments(Flow flow, CustomEventArgs data)
        {
            flow.SetValue(input, data.arguments[0]);
            flow.SetValue(isLeft, (HandSide)data.arguments[1] == HandSide.LEFT);
        }
    }
}
#endif
