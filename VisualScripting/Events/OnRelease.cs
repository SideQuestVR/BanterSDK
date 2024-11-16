#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using UnityEngine;

namespace Banter.VisualScripting
{

    [UnitTitle("On Release Passthrough Event Received")]
    [UnitShortTitle("On Release Passthrough")]
    [UnitCategory("Events\\Banter")]
    [TypeIcon(typeof(BanterObjectId))]
    public class OnRelease : EventUnit<CustomEventArgs>
    {
        [DoNotSerialize]
        [PortLabelHidden]
        public ValueInput gameObject { get; private set; }
        [DoNotSerialize]
        public ValueOutput isLeft;

        protected override bool register => true;

        public override EventHook GetHook(GraphReference reference)
        {
            return new EventHook("OnGrab");
        }

        protected override void Definition()
        {
            base.Definition();
            // Setting the value on our port.
            gameObject = ValueInput<GameObject>("Game Object", null).NullMeansSelf();
            isLeft = ValueOutput<bool>("Is Left");
        }

        protected override bool ShouldTrigger(Flow flow, CustomEventArgs data)
        {
            return data.name == flow.GetValue<GameObject>(gameObject).GetInstanceID().ToString();
        }

        // Setting the value on our port.
        protected override void AssignArguments(Flow flow, CustomEventArgs data)
        {
            flow.SetValue(isLeft, (HandSide)data.arguments[1] == HandSide.LEFT);
        }
    }
}
#endif
