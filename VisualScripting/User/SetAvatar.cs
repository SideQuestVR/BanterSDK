#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;

namespace Banter.VisualScripting
{
    [UnitTitle("Set User Avatar")]
    [UnitShortTitle("Set Avatar")]
    [UnitCategory("Banter")]
    [TypeIcon(typeof(BanterObjectId))]
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
                BanterScene.Instance().events.OnAvatarSet?.Invoke(remote, local);

                return outputTrigger;
            });
            outputTrigger = ControlOutput("");
            localAvatarUrl = ValueInput("Local Avatar URL", string.Empty);
            remoteAvatarUrl = ValueInput("Remote Avatar URL", string.Empty);
        }
    }
}
#endif
