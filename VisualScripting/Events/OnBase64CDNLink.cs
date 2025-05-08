#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;

namespace Banter.VisualScripting
{

    [UnitTitle("On Base64 CDN Link")]
    [UnitShortTitle("On Base64 CDN Link")]
    [UnitCategory("Events\\Banter\\AI")]
    [TypeIcon(typeof(BanterObjectId))]
    public class OnBase64CDNLink : EventUnit<CustomEventArgs>
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
            return new EventHook("OnBase64CDNLink");
        }

        protected override void Definition()
        {
            base.Definition();
            // Setting the value on our port.
            // id = ValueInput("Return ID", string.Empty);

            result = ValueOutput<long>("Data");
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
