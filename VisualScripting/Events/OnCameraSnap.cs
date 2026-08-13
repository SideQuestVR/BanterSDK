#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using BS;
using System.Diagnostics;

namespace BS.VisualScripting
{

    [UnitTitle("On Camera Snap")]
    [UnitShortTitle("On Camera Snap")]
    [UnitCategory("Events\\Banter\\AI")]
    [TypeIcon(typeof(BSObjectId))]
    public class OnCameraSnap : EventUnit<CustomEventArgs>
    {

        [DoNotSerialize]
        public ValueOutput result;

        protected override bool register => true;

        public override EventHook GetHook(GraphReference reference)
        {
            return new EventHook("OnCameraSnap");
        }

        protected override void Definition()
        {
            base.Definition();
            // Setting the value on our port.
            // id = ValueInput("Return ID", string.Empty);

            result = ValueOutput<string>("Data");
        }

        protected override bool ShouldTrigger(Flow flow, CustomEventArgs data)
        {
            return true; // data.name == flow.GetValue<string>(id)?.Trim();
        }

        // Setting the value on our port.
        protected override void AssignArguments(Flow flow, CustomEventArgs data)
        {   
            flow.SetValue(result, data.arguments[0]);
        }
    }
}
#endif
