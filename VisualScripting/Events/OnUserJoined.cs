#if BANTER_VISUAL_SCRIPTING
using UnityEngine;
using Unity.VisualScripting;
using BS;

namespace BS.VisualScripting
{
    [UnitTitle("On User Joined")]
    [UnitShortTitle("User Joined")]
    [UnitCategory("Events\\Banter\\User")]
    [TypeIcon(typeof(BSObjectId))]
    public class OnUserJoined : EventUnit<BSUser>
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

            info = ValueOutput<BSUser>("User Info");
        }

        protected override bool ShouldTrigger(Flow flow, BSUser data)
        {
            return true;
        }

        // Setting the value on our port.
        protected override void AssignArguments(Flow flow, BSUser data)
        {
            // name
            flow.SetValue(info, data);
        }
    }
}
#endif
