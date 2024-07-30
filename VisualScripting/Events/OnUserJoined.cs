#if BANTER_VISUAL_SCRIPTING
using UnityEngine;
using Unity.VisualScripting;
using Banter.SDK;

namespace Banter.VisualScripting
{
    [UnitTitle("On User Joined")]
    [UnitShortTitle("User Joined")]
    [UnitCategory("Events\\Banter")]
    [TypeIcon(typeof(BanterObjectId))]
    public class OnUserJoined : EventUnit<BanterUser>
    {
        [DoNotSerialize]
        public ValueOutput info;

        protected override bool register => true;

        public override EventHook GetHook(GraphReference reference)
        {
            return new EventHook("OnUserJoined");
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
