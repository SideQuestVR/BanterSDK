using Unity.VisualScripting;
using BS;
using UnityEngine;

namespace BS.VisualScripting
{

    [UnitTitle("On World Browser Texture")]
    [UnitShortTitle("On World Browser Texture")]
    [UnitCategory("Events\\BS\\Utils")]
    [TypeIcon(typeof(BSObjectId))]
    [RenamedFrom("BS.VisualScripting.OnSpaceBrowserTexture")]
    public class OnWorldBrowserTexture : EventUnit<CustomEventArgs>
    {
        [DoNotSerialize]
        public ValueOutput result;

        protected override bool register => true;

        public override EventHook GetHook(GraphReference reference)
        {
            return new EventHook("OnWorldBrowserTexture");
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
