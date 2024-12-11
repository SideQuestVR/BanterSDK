#if BANTER_VISUAL_SCRIPTING
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
            portal = ValueInput<BanterPortal>("TargetPortal");
            portal.SetDefaultValue(null);
            portal.NullMeansSelf();

            input = ControlInput("", (flow) => {
                var target = flow.GetValue<BanterPortal>(portal);
                target.url = flow.GetValue<string>(url);
                target.UpdateCallback(new System.Collections.Generic.List<PropertyName>() { PropertyName.url });
                return output;
            });
            output = ControlOutput("");
        }
    }
}
#endif
