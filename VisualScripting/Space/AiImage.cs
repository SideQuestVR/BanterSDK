#if BANTER_VISUAL_SCRIPTING
using UnityEngine;
using Unity.VisualScripting;
using Banter.SDK;
using System.Linq;
using Banter.Utilities.Async;

namespace Banter.VisualScripting
{
    [UnitTitle("Generate Ai Image")]
    [UnitShortTitle("AiImage")]
    [UnitCategory("Banter")]
    [TypeIcon(typeof(BanterObjectId))]
    public class AiImage : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;
    
        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput prompt;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var _prompt = flow.GetValue<string>(prompt);
                UnityMainThreadTaskScheduler.Default.Enqueue(() =>
                {
                    BanterScene.Instance().events.OnAiImage.Invoke(_prompt);
                });
                return outputTrigger;
            });
            outputTrigger = ControlOutput("");
            prompt = ValueInput("Prompt", "");
        }
    }
}
#endif
