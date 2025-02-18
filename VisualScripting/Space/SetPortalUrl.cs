#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using System;

namespace Banter.VisualScripting
{
    [UnitTitle("Set Portal URL")]
    [UnitShortTitle("Set Portal URL")]
    [UnitCategory("Banter")]
    [Obsolete("Use BanterPortal.Url instead")]
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
            portal = ValueInput<BanterPortal>("TargetPortal");
            portal.SetDefaultValue(null);
            portal.NullMeansSelf();

            input = ControlInput("", (flow) => {
                var target = flow.GetValue<BanterPortal>(portal);
                target.Url = flow.GetValue<string>(url);
                return output;
            });
            output = ControlOutput("");
        }
    }
}
#endif
