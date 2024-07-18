#if BANTER_VISUAL_SCRIPTING
using UnityEngine;
using Unity.VisualScripting;
using Banter.SDK;

namespace Banter.VisualScripting
{
    [UnitTitle("On Space State Properties Changed")]
    [UnitShortTitle("Space State Changed")]
    [UnitCategory("Events\\Banter")]
    [TypeIcon(typeof(BanterObjectId))]
    public class OnSpaceStatePropsChanged : EventUnit<CustomEventArgs>
    {
        [DoNotSerialize]
        [PortLabelHidden]
        public ValueInput propName { get; private set; }

        [DoNotSerialize]
        public ValueOutput newValue;

        [DoNotSerialize]
        public ValueOutput protectedProperty;


        protected override bool register => true;

        public override EventHook GetHook(GraphReference reference)
        {
            return new EventHook("OnSpaceStatePropsChanged");
        }

        protected override void Definition()
        {
            base.Definition();

            propName = ValueInput("Property name", string.Empty);
            newValue = ValueOutput<string>("New Value");
            protectedProperty = ValueOutput<bool>("Protected Property");
        }

        protected override bool ShouldTrigger(Flow flow, CustomEventArgs data)
        {
            var compare = flow.GetValue<string>(propName)?.Trim();
            if (string.IsNullOrEmpty(compare))
            {
                return true;
            }
            return data.name == flow.GetValue<string>(propName)?.Trim();
        }

        // Setting the value on our port.
        protected override void AssignArguments(Flow flow, CustomEventArgs data)
        {
            flow.SetValue(newValue, data.arguments[0]);
            flow.SetValue(protectedProperty, data.arguments[1]);
        }
    }
}
#endif