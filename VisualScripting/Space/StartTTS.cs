#if BANTER_VISUAL_SCRIPTING
using UnityEngine;
using Unity.VisualScripting;
using Banter.SDK;
using System.Linq;
using Banter.Utilities.Async;

namespace Banter.VisualScripting
{
    [UnitTitle("Start Text To Speech")]
    [UnitShortTitle("StartTTS")]
    [UnitCategory("Banter")]
    [TypeIcon(typeof(BanterObjectId))]
    public class StartTTS : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ValueInput detectSpeech;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var detectSpeech = flow.GetValue<bool>(detectSpeech);
                UnityMainThreadTaskScheduler.Default.Enqueue(() =>
                {
                    BanterScene.Instance().events.OnTTsStarted.Invoke(detectSpeech);
                });
            });
            detectSpeech = ValueInput("Detect Speech", false);
        }
    }
}
#endif
