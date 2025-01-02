#if BANTER_VISUAL_SCRIPTING
using UnityEngine;
using Unity.VisualScripting;
using Banter.SDK;
using System.Linq;
using Banter.Utilities.Async;

namespace Banter.VisualScripting
{
    [UnitTitle("Stop Text To Speech")]
    [UnitShortTitle("StopTTS")]
    [UnitCategory("Banter")]
    [TypeIcon(typeof(BanterObjectId))]
    public class StopTTS : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ValueInput returnId;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var returnId = flow.GetValue<string>(returnId);
                UnityMainThreadTaskScheduler.Default.Enqueue(() =>
                {
                    BanterScene.Instance().events.OnTTsStoped.Invoke(returnId);
                });
            });
            returnId = ValueInput("Return Id", "");
        }
    }
}
#endif
