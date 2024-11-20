#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using UnityEngine;

namespace Banter.VisualScripting
{

    [UnitTitle("On BUp")]
    [UnitShortTitle("On BUp")]
    [UnitCategory("Events\\Banter\\HeldEvents")]
    [TypeIcon(typeof(BanterObjectId))]
    public class OnBUp : EventUnit<CustomEventArgs>
    {
        [DoNotSerialize]
        [PortLabelHidden]
        [NullMeansSelf]
        public ValueInput banterHeldEvents { get; private set; }
        [DoNotSerialize]
        public ValueOutput isLeft;

        protected override bool register => true;

        public override EventHook GetHook(GraphReference reference)
        {
            return new EventHook("OnBUp");
        }

        protected override void Definition()
        {
            base.Definition();
            // Setting the value on our port.
            banterHeldEvents = ValueInput<GameObject>(nameof(banterHeldEvents), null).NullMeansSelf();
            isLeft = ValueOutput<bool>("Is Left");
        }

        protected override bool ShouldTrigger(Flow flow, CustomEventArgs data)
        {
            return data.name == flow.GetValue<GameObject>(banterHeldEvents).GetInstanceID().ToString();
        }

        // Setting the value on our port.
        protected override void AssignArguments(Flow flow, CustomEventArgs data)
        {
            flow.SetValue(isLeft, (HandSide)data.arguments[0] == HandSide.LEFT);
        }
    }
}
#endif
