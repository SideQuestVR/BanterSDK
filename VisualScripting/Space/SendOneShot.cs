#if BANTER_VISUAL_SCRIPTING
using UnityEngine;
using Unity.VisualScripting;
using Banter.SDK;
using System.Linq;
using Banter.Utilities.Async;

namespace Banter.VisualScripting
{
    [UnitTitle("Send a One Shot Message")]
    [UnitShortTitle("SendOneShot")]
    [UnitCategory("Banter")]
    [TypeIcon(typeof(BanterObjectId))]
    public class SendOneShot : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput allInstances;

        [DoNotSerialize]
        public ValueInput data;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var _allInstances = flow.GetValue<bool>(allInstances);
                var _data = flow.GetValue<string>(data);
                UnityMainThreadTaskScheduler.Default.Enqueue(() =>
                {
                    BanterScene.Instance().events.OnOneShot.Invoke(_data, _allInstances);
                });
                return outputTrigger;
            });
            outputTrigger = ControlOutput("");
            data = ValueInput("Data", "");
            allInstances = ValueInput("All Instances", false);
        }
    }
}
#endif
