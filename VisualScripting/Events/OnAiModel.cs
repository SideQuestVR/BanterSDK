using Unity.VisualScripting;
using BS;

namespace BS.VisualScripting
{

    [UnitTitle("On Ai Model")]
    [UnitShortTitle("On Ai Model")]
    [UnitCategory("Events\\BS\\AI")]
    [TypeIcon(typeof(BSObjectId))]
    public class OnAiModel : EventUnit<CustomEventArgs>
    {

        //            EventBus.Trigger("OnAiModel", new CustomEventArgs(id, new object[] { data }));
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
            return new EventHook("OnAiModel");
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
            return true; //data.name == flow.GetValue<string>(id)?.Trim();
        }

        // Setting the value on our port.
        protected override void AssignArguments(Flow flow, CustomEventArgs data)
        {
            flow.SetValue(result, data.arguments[0]);
        }
    }
}
