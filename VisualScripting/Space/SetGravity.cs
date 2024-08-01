#if BANTER_VISUAL_SCRIPTING
using UnityEngine;
using Unity.VisualScripting;
using Banter.SDK;
using System.Linq;

namespace Banter.VisualScripting
{
    [UnitTitle("Set Space Gravity")]
    [UnitShortTitle("Set Gravity")]
    [UnitCategory("Banter")]
    [TypeIcon(typeof(BanterObjectId))]
    public class SetGravity : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput gravity;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var valGravity = flow.GetValue<Vector3>(gravity);

                BanterScene.Instance().mainThread?.Enqueue(() => Physics.gravity = valGravity);

                return outputTrigger;
            });
            outputTrigger = ControlOutput("");
            gravity = ValueInput("Gravity", new Vector3(0, -9.81f, 0));
        }
    }
}
#endif
