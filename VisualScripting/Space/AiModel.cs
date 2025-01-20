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

        [DoNotSerialize]
        public ValueInput simplify;

        [DoNotSerialize]
        public ValueInput textureSize;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var _base64Image = flow.GetValue<string>(base64Image);
                var _simplify = flow.GetValue<AiModelSimplify>(simplify);
                var _textureSize = flow.GetValue<int>(textureSize);
                UnityMainThreadTaskScheduler.Default.Enqueue(() =>
                {
                    BanterScene.Instance().events.OnAiModel.Invoke(_base64Image, _simplify, _textureSize);
                });
                return outputTrigger;
            });
            outputTrigger = ControlOutput("");
            base64Image = ValueInput("Base64 Image", "");
            simplify = ValueInput("Detail", AiModelSimplify.med);
            textureSize = ValueInput("Texture Size", 1024);
        }
    }
}
#endif
