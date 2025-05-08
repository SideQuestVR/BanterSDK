#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using System.Diagnostics;
using UnityEngine;
using System.Collections.Generic;

namespace Banter.VisualScripting
{

    [UnitTitle("On Broadcast Event")]
    [UnitShortTitle("On Broadcast Event")]
    [UnitCategory("Events\\Banter\\Utils")]
    [TypeIcon(typeof(BanterObjectId))]
    public class OnGlobalEvent : EventUnit<CustomEventArgs>
    {

        //   public override Type MessageListenerType => null;

        public override EventHook GetHook(GraphReference reference)
        {
            return new EventHook("OnGlobalEvent");
        }
        // protected override string hookName => EventHooks.Custom;

        [SerializeAs(nameof(argumentCount))]
        private int _argumentCount;

        [DoNotSerialize]
        [Inspectable, UnitHeaderInspectable("Arguments")]
        public int argumentCount
        {
            get => _argumentCount;
            set => _argumentCount = Mathf.Clamp(value, 0, 10);
        }

        /// <summary>
        /// The name of the event.
        /// </summary>
        [DoNotSerialize]
        [PortLabelHidden]
        public ValueInput name { get; private set; }

        [DoNotSerialize]
        public List<ValueOutput> argumentPorts { get; } = new List<ValueOutput>();

        protected override bool register => true;

        protected override void Definition()
        {
            base.Definition();

            name = ValueInput(nameof(name), string.Empty);

            argumentPorts.Clear();

            for (var i = 0; i < argumentCount; i++)
            {
                argumentPorts.Add(ValueOutput<object>("argument_" + i));
            }
        }

        protected override bool ShouldTrigger(Flow flow, CustomEventArgs args)
        {
            return CompareNames(flow, name, args.name);
        }

        protected override void AssignArguments(Flow flow, CustomEventArgs args)
        {
            for (var i = 0; i < argumentCount; i++)
            {
                flow.SetValue(argumentPorts[i], args.arguments[i]);
            }
        }
    }
}
#endif
