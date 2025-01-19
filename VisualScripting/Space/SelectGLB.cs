#if BANTER_VISUAL_SCRIPTING
using UnityEngine;
using Unity.VisualScripting;
using Banter.SDK;
using System.Linq;
using Banter.Utilities.Async;

namespace Banter.VisualScripting
{
    [UnitTitle("Select GLB from file")]
    [UnitShortTitle("SelectGLB")]
    [UnitCategory("Banter")]
    [TypeIcon(typeof(BanterObjectId))]
    public class SelectGLB : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;
    
        [DoNotSerialize]
        public ControlOutput outputTrigger;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                UnityMainThreadTaskScheduler.Default.Enqueue(() =>
                {
                    BanterScene.Instance().events.OnSelectGlb.Invoke();
                });
                return outputTrigger;
            });
            outputTrigger = ControlOutput("");
        }
    }
}
#endif
