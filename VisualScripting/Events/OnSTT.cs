#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;

namespace Banter.VisualScripting
{

    [UnitTitle("On Ai SpeechToText Callback Received")]
    [UnitShortTitle("On Ai SpeechToText")]
    [UnitCategory("Events\\Banter")]
    [TypeIcon(typeof(BanterObjectId))]
    public class OnSTT : EventUnit<CustomEventArgs>
    {

        //#if BANTER_VISUAL_SCRIPTING
        //            EventBus.Trigger("OnSTT", new CustomEventArgs(id, new object[] { data }));
        //#endif
        //
        [DoNotSerialize]
        [PortLabelHidden]
        public ValueInput id { get; private set; }

        [DoNotSerialize]
        public ValueOutput result;

        protected override bool register => true;

        public override EventHook GetHook(GraphReference reference)
        {
            return new EventHook("OnSTT");
        }

        protected override void Definition()
        {
            base.Definition();
            // Setting the value on our port.
            id = ValueInput("Return ID", string.Empty);

            result = ValueOutput<string>("Data");
        }

        protected override bool ShouldTrigger(Flow flow, CustomEventArgs data)
        {
            return data.name == flow.GetValue<string>(id)?.Trim();
        }

        // Setting the value on our port.
        protected override void AssignArguments(Flow flow, CustomEventArgs data)
        {
            flow.SetValue(result, data.arguments[0]);
        }
    }
}
#endif