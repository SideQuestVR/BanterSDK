#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using System.Diagnostics;

namespace Banter.VisualScripting
{

    [UnitTitle("On Global Custom Event Callback Received")]
    [UnitShortTitle("On Global Custom Event")]
    [UnitCategory("Events\\Banter\\Utils")]
    [TypeIcon(typeof(BanterObjectId))]
    public class OnGlobalEvent : EventUnit<CustomEventArgs>
    {

        //#if BANTER_VISUAL_SCRIPTING
        //            EventBus.Trigger("OnAiImage", new CustomEventArgs(id, new object[] { data }));
        //#endif
        //
        // [DoNotSerialize]
        // [PortLabelHidden]
        // public ValueInput id { get; private set; }

        [DoNotSerialize]
        public ValueOutput result;

        protected override bool register => true;

        public override EventHook GetHook(GraphReference reference)
        {
            return new EventHook("OnGlobalEvent");
        }

        protected override void Definition()
        {
            base.Definition();
            // Setting the value on our port.
            // id = ValueInput("Return ID", string.Empty);

            result = ValueOutput<object>("Data");
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
