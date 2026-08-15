#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using BS;

namespace BS.VisualScripting
{
    [UnitTitle("Set User Avatar")]
    [UnitShortTitle("Set Avatar")]
    [UnitCategory("BS\\User")]
    [TypeIcon(typeof(BSObjectId))]
    public class SetAvatar : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput localAvatarUrl;

        [DoNotSerialize]
        public ValueInput remoteAvatarUrl;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var local = flow.GetValue<string>(localAvatarUrl);
                var remote = flow.GetValue<string>(remoteAvatarUrl);
                BSScene.Instance().events.OnAvatarSet?.Invoke(remote, local);

                return outputTrigger;
            });
            outputTrigger = ControlOutput("");
            localAvatarUrl = ValueInput("Local Avatar URL", string.Empty);
            remoteAvatarUrl = ValueInput("Remote Avatar URL", string.Empty);
        }
    }
}
#endif
