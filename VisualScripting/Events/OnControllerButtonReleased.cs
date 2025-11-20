#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;

namespace Banter.VisualScripting
{
    [UnitTitle("On Controller Button Released")]
    [UnitShortTitle("Button Released")]
    [UnitCategory("Events\\Banter\\Controller")]
    [TypeIcon(typeof(BanterObjectId))]
    public class OnControllerButtonReleased : EventUnit<CustomEventArgs>
    {
        [DoNotSerialize]
        public ValueOutput buttonType;

        [DoNotSerialize]
        public ValueOutput handSide;

        protected override bool register => true;

        public override EventHook GetHook(GraphReference reference)
        {
            return new EventHook("OnControllerButtonReleased");
        }

        protected override void Definition()
        {
            base.Definition();
            buttonType = ValueOutput<int>("Button Type");
            handSide = ValueOutput<int>("Hand Side");
        }

        protected override bool ShouldTrigger(Flow flow, CustomEventArgs data)
        {
            return true;
        }

        protected override void AssignArguments(Flow flow, CustomEventArgs data)
        {
            flow.SetValue(buttonType, data.arguments[0]);
            flow.SetValue(handSide, data.arguments[1]);
        }
    }
}
#endif