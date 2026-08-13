#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using BS;
using UnityEngine;

namespace BS.VisualScripting
{
    [UnitTitle("Add Force To Player")]
    [UnitShortTitle("Add Player Force")]
    [UnitCategory("Banter\\User")]
    [TypeIcon(typeof(BSObjectId))]
    public class AddPlayerForce : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput force;

        [DoNotSerialize]
        public ValueInput forceMode;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var _force = flow.GetValue<Vector3>(force);
                var _forceMode = flow.GetValue<ForceMode>(forceMode);
                BSScene.Instance().events.OnAddPlayerForce?.Invoke(_force, _forceMode);

                return outputTrigger;
            });
            outputTrigger = ControlOutput("");
            force = ValueInput("Force", Vector3.zero);
            forceMode = ValueInput("Mode", ForceMode.Force);
        }
    }
}
#endif
