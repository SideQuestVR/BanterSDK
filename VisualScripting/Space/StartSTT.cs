using UnityEngine;
using Unity.VisualScripting;
using BS;
using System.Linq;
using BS.Utilities.Async;

namespace BS.VisualScripting
{
    [UnitTitle("Start Speech To Text")]
    [UnitShortTitle("StartSTT")]
    [UnitCategory("BS\\AI")]
    [TypeIcon(typeof(BSObjectId))]
    public class StartSTT : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;
    
        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput detectSpeech;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var _detectSpeech = flow.GetValue<bool>(detectSpeech);
                UnityMainThreadTaskScheduler.Default.Enqueue(TaskRunner.Track(() =>
                {
                    BSScene.Instance().events.OnTTsStarted.Invoke(_detectSpeech);
                }, $"{nameof(StartSTT)}.{nameof(Definition)}"));
                return outputTrigger;
            });
            outputTrigger = ControlOutput("");
            detectSpeech = ValueInput("Detect Speech", false);
        }
    }
}
