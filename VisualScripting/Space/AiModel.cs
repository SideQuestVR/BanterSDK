#if BANTER_VISUAL_SCRIPTING
using UnityEngine;
using Unity.VisualScripting;
using Banter.SDK;
using System.Linq;
using Banter.Utilities.Async;

namespace Banter.VisualScripting
{
    [UnitTitle("Generate Ai Model")]
    [UnitShortTitle("AiModel")]
    [UnitCategory("Banter")]
    [TypeIcon(typeof(BanterObjectId))]
    public class AiModel : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;
    
        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput base64Image;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var _base64Image = flow.GetValue<string>(base64Image);
                UnityMainThreadTaskScheduler.Default.Enqueue(() =>
                {
                    BanterScene.Instance().events.OnAiModel.Invoke(_base64Image);
                });
                return outputTrigger;
            });
            outputTrigger = ControlOutput("");
            base64Image = ValueInput("Base64 Image", "");
        }
    }
}
#endif
