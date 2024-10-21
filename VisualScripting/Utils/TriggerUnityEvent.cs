#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;

namespace Banter.VisualScripting
{
    [UnitTitle("Trigger VisualScriptingEvent")]
    [UnitShortTitle("Trigger VisualScriptingEvent")]
    [UnitCategory("Banter")]
    [TypeIcon(typeof(BanterObjectId))]
    public class TriggerUnityEvent : Unit
    {
        [DoNotSerialize]
        [PortLabelHidden]
        [NullMeansSelf]
        public ValueInput Target { get; private set; }
        [PortLabelHidden]
        public ControlOutput triggered;
        [PortLabelHidden]
        public ControlInput trigger;

        protected override void Definition()
        {
            Target = ValueInput(typeof(VisualScriptingEvent), nameof(Target));
            Target.SetDefaultValue(null);
            Target.NullMeansSelf();

            trigger = ControlInput("", (flow) => {
                var target = flow.GetValue<VisualScriptingEvent>(Target);
                target.OnCustomEvent?.Invoke();

                return triggered;
            });

            triggered = ControlOutput("");
            Succession(trigger, triggered);
        }
    }
}
#endif
