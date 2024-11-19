#if BANTER_VISUAL_SCRIPTING
using UnityEngine;
using Unity.VisualScripting;
using Banter.SDK;
using System.Linq;
using Banter.Utilities.Async;

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
                UnityMainThreadTaskScheduler.Default.Enqueue(() =>
                {
                    Time.timeScale = valScale;
                });
                return outputTrigger;
            });
            outputTrigger = ControlOutput("");
            scale = ValueInput("Fast", 1.0f);
        }
    }
}
#endif
