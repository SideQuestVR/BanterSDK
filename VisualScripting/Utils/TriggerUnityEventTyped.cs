#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using UnityEngine;

namespace Banter.VisualScripting
{
    [UnitTitle("Trigger Visual Scripting Relay")]
    [UnitShortTitle("Trigger VS Relay")]
    [UnitCategory("Banter\\Utils")]
    [TypeIcon(typeof(BanterObjectId))]
    public class TriggerUnityEventTyped : Unit
    {
        [SerializeAs(nameof(dataType))]
        private EventDataType _dataType = EventDataType.None;

        [DoNotSerialize]
        [Inspectable, UnitHeaderInspectable("Type")]
        public EventDataType dataType
        {
            get => _dataType;
            set => _dataType = value;
        }

        [DoNotSerialize]
        [NullMeansSelf]
        public ValueInput Target { get; private set; }

        [DoNotSerialize]
        public ValueInput Value { get; private set; }

        [PortLabelHidden]
        public ControlInput trigger;

        [PortLabelHidden]
        public ControlOutput triggered;

        protected override void Definition()
        {
            Target = ValueInput<VisualScriptingEventTyped>(nameof(Target), null);
            Target.NullMeansSelf();

            // Create typed input port based on enum selection
            switch (dataType)
            {
                case EventDataType.Float:
                    Value = ValueInput<float>(nameof(Value), 0f);
                    break;
                case EventDataType.Int:
                    Value = ValueInput<int>(nameof(Value), 0);
                    break;
                case EventDataType.Bool:
                    Value = ValueInput<bool>(nameof(Value), false);
                    break;
                case EventDataType.String:
                    Value = ValueInput<string>(nameof(Value), "");
                    break;
                case EventDataType.Vector2:
                    Value = ValueInput<Vector2>(nameof(Value), Vector2.zero);
                    break;
                case EventDataType.Vector3:
                    Value = ValueInput<Vector3>(nameof(Value), Vector3.zero);
                    break;
                case EventDataType.Color:
                    Value = ValueInput<Color>(nameof(Value), Color.white);
                    break;
                case EventDataType.Texture:
                    Value = ValueInput<Texture>(nameof(Value), null);
                    break;
                case EventDataType.Texture2D:
                    Value = ValueInput<Texture2D>(nameof(Value), null);
                    break;
                case EventDataType.GameObject:
                    Value = ValueInput<GameObject>(nameof(Value), null);
                    break;
                case EventDataType.Object:
                    Value = ValueInput<Object>(nameof(Value), null);
                    break;
                // None = no value port
            }

            trigger = ControlInput("", (flow) =>
            {
                var target = flow.GetValue<VisualScriptingEventTyped>(Target);
                if (target == null) return triggered;

                switch (dataType)
                {
                    case EventDataType.None:
                        target.TriggerEvent();
                        break;
                    case EventDataType.Float:
                        target.TriggerFloat(flow.GetValue<float>(Value));
                        break;
                    case EventDataType.Int:
                        target.TriggerInt(flow.GetValue<int>(Value));
                        break;
                    case EventDataType.Bool:
                        target.TriggerBool(flow.GetValue<bool>(Value));
                        break;
                    case EventDataType.String:
                        target.TriggerString(flow.GetValue<string>(Value));
                        break;
                    case EventDataType.Vector2:
                        target.TriggerVector2(flow.GetValue<Vector2>(Value));
                        break;
                    case EventDataType.Vector3:
                        target.TriggerVector3(flow.GetValue<Vector3>(Value));
                        break;
                    case EventDataType.Color:
                        target.TriggerColor(flow.GetValue<Color>(Value));
                        break;
                    case EventDataType.Texture:
                        target.TriggerTexture(flow.GetValue<Texture>(Value));
                        break;
                    case EventDataType.Texture2D:
                        target.TriggerTexture2D(flow.GetValue<Texture2D>(Value));
                        break;
                    case EventDataType.GameObject:
                        target.TriggerGameObject(flow.GetValue<GameObject>(Value));
                        break;
                    case EventDataType.Object:
                        target.TriggerObject(flow.GetValue<Object>(Value));
                        break;
                }
                return triggered;
            });

            triggered = ControlOutput("");
            Succession(trigger, triggered);
        }
    }
}
#endif
