#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using UnityEngine;

namespace Banter.VisualScripting
{

    [UnitTitle("On Click BanterPlayerEvent Event Received")]
    [UnitShortTitle("On Click BanterPlayerEvent")]
    [UnitCategory("Events\\Banter")]
    [TypeIcon(typeof(BanterObjectId))]
    public class OnClick : EventUnit<CustomEventArgs>
    {
        [DoNotSerialize]
        [PortLabelHidden]
        [NullMeansSelf]
        public ValueInput gameObject { get; private set; }
        [DoNotSerialize]
        public ValueOutput clickPoint;
        [DoNotSerialize]
        public ValueOutput clickNormal;

        protected override bool register => true;

        public override EventHook GetHook(GraphReference reference)
        {
            return new EventHook("OnClick");
        }

        protected override void Definition()
        {
            base.Definition();
            // Setting the value on our port.
            gameObject = ValueInput<GameObject>(nameof(gameObject), null).NullMeansSelf();
            clickPoint = ValueOutput<Vector3>("Point");
            clickNormal = ValueOutput<Vector3>("Normal");
        }

        protected override bool ShouldTrigger(Flow flow, CustomEventArgs data)
        {
            return data.name == flow.GetValue<GameObject>(gameObject).GetInstanceID().ToString();
        }

        // Setting the value on our port.
        protected override void AssignArguments(Flow flow, CustomEventArgs data)
        {
            flow.SetValue(clickPoint, data.arguments[0]);
            flow.SetValue(clickNormal, data.arguments[1]);
        }
    }
}
#endif
