#if BANTER_VISUAL_SCRIPTING
using UnityEngine;
using Unity.VisualScripting;
using Banter.SDK;
using System.Linq;

namespace Banter.VisualScripting
{
    [UnitTitle("Set TimeScale")]
    [UnitShortTitle("Set TimeScale")]
    [UnitCategory("Banter")]
    [TypeIcon(typeof(BanterObjectId))]
    public class SetTimeScale : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput scale;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var valScale = flow.GetValue<float>(scale);
                BanterScene.Instance().mainThread?.Enqueue(() =>
                {
                    BanterScene.Instance().events.OnTimeScaleChanged?.Invoke(valScale);
                });
                return outputTrigger;
            });
            outputTrigger = ControlOutput("");
            scale = ValueInput("Fast", 1.0f);
        }
    }
}
#endif
