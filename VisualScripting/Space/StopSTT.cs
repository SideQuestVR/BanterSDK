#if BANTER_VISUAL_SCRIPTING
using UnityEngine;
using Unity.VisualScripting;
using Banter.SDK;
using System.Linq;
using Banter.Utilities.Async;

namespace Banter.VisualScripting
{
    [UnitTitle("Stop Speech To Text")]
    [UnitShortTitle("StopSTT")]
    [UnitCategory("Banter")]
    [TypeIcon(typeof(BanterObjectId))]
    public class StopSTT : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;
    
        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput returnId;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var _returnId = flow.GetValue<string>(returnId);
                UnityMainThreadTaskScheduler.Default.Enqueue(() =>
                {
                    BanterScene.Instance().events.OnTTsStoped.Invoke(_returnId);
                });
                return outputTrigger;
            });
            outputTrigger = ControlOutput("");
            returnId = ValueInput("Return Id", "");
        }
    }
}
#endif
