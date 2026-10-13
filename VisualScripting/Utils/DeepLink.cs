using Unity.VisualScripting;
using BS.Utilities.Async;

namespace BS.VisualScripting
{
    /// <summary>
    /// Launches another Quest app by its App ID, optionally passing it a message: the graph twin of
    /// <c>scene.DeepLink(appId, message)</c>. Quest only; on Windows and in Play mode nothing happens.
    /// </summary>
    [UnitTitle("Deep Link")]
    [UnitShortTitle("DeepLink")]
    [UnitCategory("BS\\Utils")]
    [TypeIcon(typeof(BSObjectId))]
    public class DeepLink : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput appId;

        [DoNotSerialize]
        public ValueInput message;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) =>
            {
                var _appId = flow.GetValue<string>(appId);
                var _message = flow.GetValue<string>(message);
                UnityMainThreadTaskScheduler.Default.Enqueue(TaskRunner.Track(() =>
                {
                    BSScene.Instance().events.OnDeepLink.Invoke(_appId, _message ?? string.Empty);
                }, $"{nameof(DeepLink)}.{nameof(Definition)}"));
                return outputTrigger;
            });
            outputTrigger = ControlOutput("");
            appId = ValueInput("App ID", string.Empty);
            message = ValueInput("Message", string.Empty);
        }
    }
}
