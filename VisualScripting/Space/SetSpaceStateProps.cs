using Unity.VisualScripting;
using BS;

namespace BS.VisualScripting
{
    [UnitTitle("Set Space State Property")]
    [UnitShortTitle("Set Space Prop")]
    [UnitCategory("BS\\Networking")]
    [TypeIcon(typeof(BSObjectId))]
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
                var propIsPublic = flow.GetValue<bool>(isPublic);

                if (propIsPublic)
                {
                    BSScene.Instance().events.OnPublicSpaceStateChanged.Invoke(propKey, propValue);
                }
                else
                {
                    BSScene.Instance().events.OnProtectedSpaceStateChanged.Invoke(propKey, propValue);
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
