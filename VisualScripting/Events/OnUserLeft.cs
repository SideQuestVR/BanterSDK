#if BANTER_VISUAL_SCRIPTING
using UnityEngine;
using Unity.VisualScripting;
using Banter.SDK;

namespace Banter.VisualScripting
{
    [UnitTitle("On User Left")]
    [UnitShortTitle("User Left")]
    [UnitCategory("Events\\Banter")]
    [TypeIcon(typeof(BanterObjectId))]
    public class OnUserLeft : EventUnit<CustomEventArgs>
    {
        [DoNotSerialize]
        public ValueOutput name;

        [DoNotSerialize]
        public ValueOutput id;

        [DoNotSerialize]
        public ValueOutput uid;

        [DoNotSerialize]
        public ValueOutput color;

        [DoNotSerialize]
        public ValueOutput isLocal;

        protected override bool register => true;

        public override EventHook GetHook(GraphReference reference)
        {
            return new EventHook("OnUserLeft");
        }

        protected override void Definition()
        {
            base.Definition();

            name = ValueOutput<string>("name");
            id = ValueOutput<string>("id");
            uid = ValueOutput<string>("uid");
            color = ValueOutput<Color>("color");
            isLocal = ValueOutput<bool>("isLocal");
        }

        protected override bool ShouldTrigger(Flow flow, CustomEventArgs data)
        {
            return true;
        }

        // Setting the value on our port.
        protected override void AssignArguments(Flow flow, CustomEventArgs data)
        {
            // name
            flow.SetValue(name, data.arguments[0]);
            // id
            flow.SetValue(id, data.arguments[1]);
            // uid
            flow.SetValue(uid, data.arguments[2]);
            // Color is string, convert
            if (ColorUtility.TryParseHtmlString((string)data.arguments[3], out Color converted))
            {
                flow.SetValue(color, converted);
            }
            else
            {
                flow.SetValue(color, Color.white);
            }
            // isLocal
            flow.SetValue(isLocal, data.arguments[4]);
        }
    }
}
#endif