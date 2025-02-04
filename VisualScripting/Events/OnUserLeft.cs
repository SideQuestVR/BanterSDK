#if BANTER_VISUAL_SCRIPTING
using UnityEngine;
using Unity.VisualScripting;
using Banter.SDK;

namespace Banter.VisualScripting
{
    [UnitTitle("On User Left")]
    [UnitShortTitle("User Left")]
    [UnitCategory("Events\\Banter\\User")]
    [TypeIcon(typeof(BanterObjectId))]
    public class OnUserLeft : EventUnit<BanterUser>
    {
        [DoNotSerialize]
        public ValueOutput info;

        protected override bool register => true;

        public override EventHook GetHook(GraphReference reference)
        {
            return new EventHook("OnUserLeft");
        }

        protected override void Definition()
        {
            base.Definition();

            info = ValueOutput<BanterUser>("User Info");
        }

        protected override bool ShouldTrigger(Flow flow, BanterUser data)
        {
            return true;
        }

        // Setting the value on our port.
        protected override void AssignArguments(Flow flow, BanterUser data)
        {
            // name
            flow.SetValue(info, data);
        }
    }
}
#endif
