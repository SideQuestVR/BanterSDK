#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;

namespace Banter.VisualScripting
{
    [UnitTitle("Set Space State Value")]
    [UnitShortTitle("Set Space State")]
    [UnitCategory("Banter")]
    [TypeIcon(typeof(BanterObjectId))]
    public class SetSpaceStateProps : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput key;

        [DoNotSerialize]
        public ValueInput value;

        [DoNotSerialize]
        public ValueInput isProtected;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var propKey = flow.GetValue<string>(key);
                var propValue = flow.GetValue<string>(value);
                var protectedProperty = flow.GetValue<bool>(isProtected);

                if (protectedProperty)
                {
                    BanterScene.Instance().events.OnProtectedSpaceStateChanged.Invoke(propKey, propValue);
                }
                else
                {
                    BanterScene.Instance().events.OnPublicSpaceStateChanged.Invoke(propKey, propValue);
                }

                return outputTrigger;
            });
            
            outputTrigger = ControlOutput("");
            key = ValueInput("Key", string.Empty);
            value = ValueInput("Value", string.Empty);
            isProtected = ValueInput("Protected Property", false);
        }
    }
}
#endif
