using Unity.VisualScripting;
using BS;

namespace BS.VisualScripting
{
    /// <summary>
    /// Fires when one of the local player's pointers moves onto UI (a canvas or UI Toolkit panel, the app's or the
    /// world's) or off it. The mouse counts as the right hand.
    /// </summary>
    [UnitTitle("On Pointer Over UI")]
    [UnitShortTitle("Pointer Over UI")]
    [UnitCategory("Events\\BS\\Controller")]
    [TypeIcon(typeof(BSObjectId))]
    public class OnPointerOverUI : EventUnit<CustomEventArgs>
    {
        [DoNotSerialize]
        public ValueOutput isOverUI;

        [DoNotSerialize]
        public ValueOutput handSide;

        protected override bool register => true;

        public override EventHook GetHook(GraphReference reference)
        {
            return new EventHook("OnPointerOverUI");
        }

        protected override void Definition()
        {
            base.Definition();
            isOverUI = ValueOutput<bool>("Is Over UI");
            handSide = ValueOutput<int>("Hand Side");
        }

        protected override bool ShouldTrigger(Flow flow, CustomEventArgs data)
        {
            return true;
        }

        protected override void AssignArguments(Flow flow, CustomEventArgs data)
        {
            flow.SetValue(isOverUI, data.arguments[0]);
            flow.SetValue(handSide, data.arguments[1]);
        }
    }
}
