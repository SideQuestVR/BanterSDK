#if BANTER_VISUAL_SCRIPTING
using UnityEngine;
using Unity.VisualScripting;
using Banter.SDK;
using System.Linq;

namespace Banter.VisualScripting
{
    [UnitTitle("Set Player Speed")]
    [UnitShortTitle("Set Speed")]
    [UnitCategory("Banter")]
    [TypeIcon(typeof(BanterObjectId))]
    public class SetPlayerSpeed : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput fast;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var valFast = flow.GetValue<bool>(fast);
                BanterScene.Instance().mainThread?.Enqueue(() =>
                {
                    BanterScene.Instance().events.OnPlayerSpeedChanged?.Invoke(valFast);
                });
                return outputTrigger;
            });
            outputTrigger = ControlOutput("");
            fast = ValueInput("Fast", false);
        }
    }
}
#endif
