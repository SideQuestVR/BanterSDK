#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;

namespace Banter.VisualScripting
{
    [UnitTitle("On Controller Axis Update")]
    [UnitShortTitle("Axis Update")]
    [UnitCategory("Events\\Banter\\Controller")]
    [TypeIcon(typeof(BanterObjectId))]
    public class OnControllerAxisUpdate : EventUnit<CustomEventArgs>
    {
        [DoNotSerialize]
        public ValueOutput handSide;

        [DoNotSerialize]
        public ValueOutput xAxis;

        [DoNotSerialize]
        public ValueOutput yAxis;

        protected override bool register => true;

        public override EventHook GetHook(GraphReference reference)
        {
            return new EventHook("OnControllerAxisUpdate");
        }

        protected override void Definition()
        {
            base.Definition();
            handSide = ValueOutput<int>("Hand Side");
            xAxis = ValueOutput<float>("X Axis");
            yAxis = ValueOutput<float>("Y Axis");
        }

        protected override bool ShouldTrigger(Flow flow, CustomEventArgs data)
        {
            return true;
        }

        protected override void AssignArguments(Flow flow, CustomEventArgs data)
        {
            flow.SetValue(handSide, data.arguments[0]);
            flow.SetValue(xAxis, data.arguments[1]);
            flow.SetValue(yAxis, data.arguments[2]);
        }
    }
}
#endif