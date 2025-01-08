#if BANTER_VISUAL_SCRIPTING
using UnityEngine;
using Unity.VisualScripting;
using Banter.SDK;
using System.Linq;
using Banter.Utilities.Async;

namespace Banter.VisualScripting
{
    [UnitTitle("Start Speech To Text")]
    [UnitShortTitle("StartSTT")]
    [UnitCategory("Banter")]
    [TypeIcon(typeof(BanterObjectId))]
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
                UnityMainThreadTaskScheduler.Default.Enqueue(() =>
                {
                    BanterScene.Instance().events.OnTTsStarted.Invoke(_detectSpeech);
                });
                return outputTrigger;
            });
            outputTrigger = ControlOutput("");
            detectSpeech = ValueInput("Detect Speech", false);
        }
    }
}
#endif
