#if BANTER_VISUAL_SCRIPTING
using UnityEngine;
using Unity.VisualScripting;
using Banter.SDK;
using UnityEngine.Networking;
using System.Collections;
using System.Runtime.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Banter.VisualScripting
{

    [UnitTitle("Trigger Broadcast Event")]
    [UnitShortTitle("Send Global Event")]
    [UnitCategory("Banter\\Utilities")]
    [TypeIcon(typeof(BanterObjectId))]
    public class SendGlobalEvent : Unit
    {        
         [SerializeAs(nameof(argumentCount))]
        private int _argumentCount;

        [DoNotSerialize]
        public List<ValueInput> arguments { get; private set; }

        [DoNotSerialize]
        [Inspectable, UnitHeaderInspectable("Arguments")]
        public int argumentCount
        {
            get => _argumentCount;
            set => _argumentCount = Mathf.Clamp(value, 0, 10);
        }

        /// <summary>
        /// The entry point to trigger the event.
        /// </summary>
        [DoNotSerialize]
        [PortLabelHidden]
        public ControlInput enter { get; private set; }

        /// <summary>
        /// The name of the event.
        /// </summary>
        [DoNotSerialize]
        [PortLabelHidden]
        public ValueInput name { get; private set; }

        /// <summary>
        /// The action to do after the event has been triggered.
        /// </summary>
        [DoNotSerialize]
        [PortLabelHidden]
        public ControlOutput exit { get; private set; }

        protected override void Definition()
        {
            enter = ControlInput(nameof(enter), Trigger);

            exit = ControlOutput(nameof(exit));

            name = ValueInput(nameof(name), string.Empty);

            arguments = new List<ValueInput>();

            for (var i = 0; i < argumentCount; i++)
            {
                var argument = ValueInput<object>("argument_" + i);
                arguments.Add(argument);
                Requirement(argument, enter);
            }

            Requirement(name, enter);
            Succession(enter, exit);
        }

        private ControlOutput Trigger(Flow flow)
        {
            var name = flow.GetValue<string>(this.name);
            var arguments = this.arguments.Select(flow.GetConvertedValue).ToArray();

            EventBus.Trigger("OnGlobalEvent", new CustomEventArgs(name, arguments));

            return exit;
        }
    }
}
#endif
