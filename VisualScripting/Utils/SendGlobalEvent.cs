#if BANTER_VISUAL_SCRIPTING
using UnityEngine;
using Unity.VisualScripting;
using Banter.SDK;
using UnityEngine.Networking;
using System.Collections;
using System.Runtime.Serialization;
using System;

namespace Banter.VisualScripting
{

    [UnitTitle("Send a Global Custom Event")]
    [UnitShortTitle("Send Global Event")]
    [UnitCategory("Banter")]
    [TypeIcon(typeof(BanterObjectId))]
    public class SendGLobalEvent : Unit
    {
        [DoNotSerialize]
        public ValueInput obj;

        [DoNotSerialize]
        [PortLabelHidden]
        public ControlInput input;

        [DoNotSerialize]
        [PortLabelHidden]
        public ControlOutput output;

        protected override void Definition()
        {
            input = ControlInput("", (flow) => {
                EventBus.Trigger("OnGlobalEvent", new CustomEventArgs("OnGlobalEvent", new object[] { flow.GetValue<object>(this.obj) }));
                return output;
            });
            output = ControlOutput("");
            obj = ValueInput<object>("Object", null);
        }
    }
}
#endif
