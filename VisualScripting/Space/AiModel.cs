using UnityEngine;
using Unity.VisualScripting;
using BS;
using System.Linq;
using BS.Utilities.Async;

namespace BS.VisualScripting
{
    [UnitTitle("Generate Ai Model")]
    [UnitShortTitle("AiModel")]
    [UnitCategory("BS\\AI")]
    [TypeIcon(typeof(BSObjectId))]
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
                UnityMainThreadTaskScheduler.Default.Enqueue(TaskRunner.Track(() =>
                {
                    BSScene.Instance().events.OnAiModel.Invoke(_base64Image, _simplify, _textureSize);
                }, $"{nameof(AiModel)}.{nameof(Definition)}"));
                return outputTrigger;
            });
            outputTrigger = ControlOutput("");
            base64Image = ValueInput("Base64 Image", "");
            simplify = ValueInput("Detail", AiModelSimplify.med);
            textureSize = ValueInput("Texture Size", 1024);
        }
    }
}
