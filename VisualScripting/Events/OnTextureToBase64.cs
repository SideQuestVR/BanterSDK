#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using System.Diagnostics;

namespace Banter.VisualScripting
{

    [UnitTitle("On Texture To Base64 Callback Received")]
    [UnitShortTitle("On Texture To Base64")]
    [UnitCategory("Events\\Banter")]
    [TypeIcon(typeof(BanterObjectId))]
    public class OnTextureToBase64 : EventUnit<CustomEventArgs>
    {

        [DoNotSerialize]
        public ValueOutput result;

        protected override bool register => true;

        public override EventHook GetHook(GraphReference reference)
        {
            return new EventHook("OnTextureToBase64");
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
            return true; 
        }

        // Setting the value on our port.
        protected override void AssignArguments(Flow flow, CustomEventArgs data)
        {   
            flow.SetValue(result, data.arguments[0]);
        }
    }
}
#endif
