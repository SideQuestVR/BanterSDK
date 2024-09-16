#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;

namespace Banter.VisualScripting
{
    [UnitTitle("Trigger Custom Event")]
    [UnitShortTitle("Trigger Event")]
    [UnitCategory("Banter")]
    [TypeIcon(typeof(BanterObjectId))]
    public class TriggerUnityEvent : Unit
    {
        [DoNotSerialize]
        [PortLabelHidden]
        public ValueInput targetGameObject;
        [DoNotSerialize]
        public ControlOutput triggered;
        [DoNotSerialize]
        public ControlInput trigger;

        protected override void Definition()
        {
            targetGameObject = ValueInput<VisualScriptingEvent>("Target", null).NullMeansSelf();
            trigger = ControlInput("", (flow) => {
                var target = flow.GetValue<VisualScriptingEvent>(targetGameObject);
                target.OnCustomEvent?.Invoke();

                return triggered;
            });

            triggered = ControlOutput("");
            Succession(trigger, triggered);
        }
    }
}
#endif
