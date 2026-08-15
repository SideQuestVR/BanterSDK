#if BANTER_VISUAL_SCRIPTING
using UnityEngine;
using Unity.VisualScripting;
using BS;
using System.Linq;
using BS.Utilities.Async;

namespace BS.VisualScripting
{
    [UnitTitle("Stop Speech To Text")]
    [UnitShortTitle("StopSTT")]
    [UnitCategory("BS\\AI")]
    [TypeIcon(typeof(BSObjectId))]
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
                UnityMainThreadTaskScheduler.Default.Enqueue(TaskRunner.Track(() =>
                {
                    BSScene.Instance().events.OnTTsStoped.Invoke(_returnId);
                }, $"{nameof(StopSTT)}.{nameof(Definition)}"));
                return outputTrigger;
            });
            outputTrigger = ControlOutput("");
            returnId = ValueInput("Return Id", "");
        }
    }
}
#endif
