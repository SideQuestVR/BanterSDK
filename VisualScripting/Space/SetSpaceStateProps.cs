#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;

namespace Banter.VisualScripting
{
    [UnitTitle("Set Space State Property")]
    [UnitShortTitle("Set Space Prop")]
    [UnitCategory("Banter")]
    [TypeIcon(typeof(BanterObjectId))]
    public class SetSpaceStateProp : Unit
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
        public ValueInput isPublic;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var propKey = flow.GetValue<string>(key);
                var propValue = flow.GetValue<string>(value);
                var protectedProperty = flow.GetValue<bool>(isPublic);

                if (protectedProperty)
                {
                    BanterScene.Instance().events.OnProtectedSpaceStateChanged.Invoke(propKey, propValue);
                    //BanterScene.Instance().SetProps(APICommands.SET_PROTECTED_SPACE_PROPS, $"{propKey}{MessageDelimiters.TERTIARY}{propValue}");
                }
                else
                {
                    BanterScene.Instance().events.OnPublicSpaceStateChanged.Invoke(propKey, propValue);
                    //BanterScene.Instance().SetProps(APICommands.SET_PUBLIC_SPACE_PROPS, $"{propKey}{MessageDelimiters.TERTIARY}{propValue}");
                }

                return outputTrigger;
            });
            
            outputTrigger = ControlOutput("");
            key = ValueInput("Property Name", string.Empty);
            value = ValueInput("Value", string.Empty);
            isPublic = ValueInput("Is Public Property?", false);
        }
    }
}
#endif
