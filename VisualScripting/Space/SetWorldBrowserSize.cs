using UnityEngine;
using Unity.VisualScripting;
using BS;
using BS.Utilities.Async;

namespace BS.VisualScripting
{
    [UnitTitle("Set World Browser Size")]
    [UnitShortTitle("SetWorldBrowserSize")]
    [UnitCategory("BS\\Space")]
    [TypeIcon(typeof(BSObjectId))]
    public class SetWorldBrowserSize : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput width;

        [DoNotSerialize]
        public ValueInput height;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) =>
            {
                var _width = flow.GetValue<int>(width);
                var _height = flow.GetValue<int>(height);
                UnityMainThreadTaskScheduler.Default.Enqueue(TaskRunner.Track(() =>
                {
                    BSScene.Instance().SetWorldBrowserSize(_width, _height);
                }, $"{nameof(SetWorldBrowserSize)}.{nameof(Definition)}"));
                return outputTrigger;
            });
            outputTrigger = ControlOutput("");
            // 0 x 0 puts the world browser back to its default size.
            width = ValueInput("Width", 1920);
            height = ValueInput("Height", 1080);
        }
    }
}
