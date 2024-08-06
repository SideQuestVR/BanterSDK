#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;

namespace Banter.VisualScripting
{
    [UnitTitle("Set Space State Property")]
    [UnitShortTitle("Set Space Prop")]
    [UnitCategory("Banter")]
    [TypeIcon(typeof(BanterObjectId))]
    [RenamedFrom("SetSpaceStateProp")]
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
        public ValueInput isPublic;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var propKey = flow.GetValue<string>(key);
                var propValue = flow.GetValue<string>(value);
                var propIsPublic = flow.GetValue<bool>(isPublic);

                if (propIsPublic)
                {
                    BanterScene.Instance().events.OnPublicSpaceStateChanged.Invoke(propKey, propValue);
                }
                else
                {
                    BanterScene.Instance().events.OnProtectedSpaceStateChanged.Invoke(propKey, propValue);
                }

                return outputTrigger;
            });
            
            outputTrigger = ControlOutput("");
            key = ValueInput("Property Name", string.Empty);
            value = ValueInput("Value", string.Empty);
            isPublic = ValueInput("Is Public Property?", true);
        }
    }
}
#endif
