#if BANTER_VISUAL_SCRIPTING
using UnityEngine;
using Unity.VisualScripting;
using Banter.SDK;

namespace Banter.VisualScripting
{
    [UnitTitle("Set Portal URL")]
    [UnitShortTitle("Set Portal URL")]
    [UnitCategory("Banter")]
    [TypeIcon(typeof(BanterObjectId))]
    public class SetPortalUrl : Unit
    {
        [DoNotSerialize]
        public ValueInput url;

        [DoNotSerialize]
        [NullMeansSelf]
        public ValueInput portal;

        [DoNotSerialize]
        [PortLabelHidden]
        public ControlInput input;

        [DoNotSerialize]
        [PortLabelHidden]
        public ControlOutput output;

        protected override void Definition()
        {
            url = ValueInput<string>("URL");
            portal = ValueInput<Portal>("TargetPortal");
            portal.SetDefaultValue(null);
            portal.NullMeansSelf();

            input = ControlInput("", (flow) => {
                var target = flow.GetValue<Portal>(portal);
                target.url = flow.GetValue<string>(url);
                return output;
            });
            output = ControlOutput("");
        }
    }
}
#endif
