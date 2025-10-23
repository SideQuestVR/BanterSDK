#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;

namespace Banter.VisualScripting
{
    [UnitTitle("Lock Player Position")]
    [UnitShortTitle("Lock Player")]
    [UnitCategory("Banter\\User")]
    [TypeIcon(typeof(BanterObjectId))]
    public class LockPlayer : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                BanterScene.Instance().events.OnLegacyPlayerLockChanged.Invoke(true);
                return outputTrigger;
            });
            outputTrigger = ControlOutput("");
        }
    }

    [UnitTitle("Unlock Player Position")]
    [UnitShortTitle("Unlock Player")]
    [UnitCategory("Banter\\User")]
    [TypeIcon(typeof(BanterObjectId))]
    public class UnlockPlayer : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                BanterScene.Instance().events.OnLegacyPlayerLockChanged.Invoke(false);
                return outputTrigger;
            });
            outputTrigger = ControlOutput("");
        }
    }
}
#endif
