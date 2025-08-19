#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using UnityEngine;

namespace Banter.VisualScripting
{

    [UnitTitle("On Space Browser Texture")]
    [UnitShortTitle("On Space Browser Texture")]
    [UnitCategory("Events\\Banter\\Utils")]
    [TypeIcon(typeof(BanterObjectId))]
    public class OnSpaceBrowserTexture : EventUnit<CustomEventArgs>
    {
        [DoNotSerialize]
        public ValueOutput result;

        protected override bool register => true;

        public override EventHook GetHook(GraphReference reference)
        {
            return new EventHook("OnSpaceBrowserTexture");
        }

        protected override void Definition()
        {
            base.Definition();

            result = ValueOutput<Texture2D>("Texture");
        }

        protected override bool ShouldTrigger(Flow flow, CustomEventArgs data)
        {
            return true; 
        }

        protected override void AssignArguments(Flow flow, CustomEventArgs data)
        {
            flow.SetValue(result, data.arguments[0]);
        }
    }
}
#endif
