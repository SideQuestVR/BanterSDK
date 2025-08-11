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
    [UnitCategory("Banter\\AI")]
    [TypeIcon(typeof(BanterObjectId))]
    public class AiImage : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;
    
        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput prompt;

        [DoNotSerialize]
        public ValueInput ratio;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var _prompt = flow.GetValue<string>(prompt);
                var _ratio = flow.GetValue<AiImageRatio>(ratio);
                UnityMainThreadTaskScheduler.Default.Enqueue(TaskRunner.Track(() =>
                {
                    BanterScene.Instance().events.OnAiImage.Invoke(_prompt, _ratio);
                }, $"{nameof(AiImage)}.{nameof(Definition)}"));
                return outputTrigger;
            });
            outputTrigger = ControlOutput("");
            prompt = ValueInput("Prompt", "");
            ratio = ValueInput("Ratio", AiImageRatio._1_1);
        }
    }
}
#endif
