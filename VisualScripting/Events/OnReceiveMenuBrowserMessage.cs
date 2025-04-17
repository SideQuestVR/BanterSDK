#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using System.Diagnostics;

namespace Banter.VisualScripting
{

    [UnitTitle("On Receive Menu Browser Message Callback Received")]
    [UnitShortTitle("On Receive Menu Browser Message")]
    [UnitCategory("Events\\Banter\\Browser")]
    [TypeIcon(typeof(BanterObjectId))]
    public class OnReceiveMenuBrowserMessage : EventUnit<CustomEventArgs>
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
            return new EventHook("OnReceiveMenuBrowserMessage");
        }

        protected override void Definition()
        {
            base.Definition();
            // Setting the value on our port.
            // id = ValueInput("Return ID", string.Empty);

            result = ValueOutput<string>("Message");
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
