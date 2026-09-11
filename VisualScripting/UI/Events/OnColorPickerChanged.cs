using Unity.VisualScripting;
using BS;
using BS.VisualScripting.UI.Helpers;
using UnityEngine;

namespace BS.VisualScripting
{
    /// <summary>
    /// Fires when a colour picker element's value changes: live while the wheel is dragged and
    /// once on release. The picker reports its value as a hex string change event, so this is
    /// the text-field change event filtered to values that parse as a colour, with the parsed
    /// <see cref="UnityEngine.Color"/> as an extra output.
    /// </summary>
    [UnitTitle("On Color Picker Changed")]
    [UnitShortTitle("On Color Picker Changed")]
    [UnitCategory("Events\\BS\\UI")]
    [TypeIcon(typeof(BSObjectId))]
    public class OnColorPickerChanged : EventUnit<CustomEventArgs>
    {
        [DoNotSerialize]
        public ValueInput elementId;

        [DoNotSerialize]
        public ValueInput elementName;

        [DoNotSerialize]
        public ValueInput autoRegister;

        [DoNotSerialize]
        public ValueOutput changedElementId;

        [DoNotSerialize]
        public ValueOutput hex;

        [DoNotSerialize]
        public ValueOutput color;

        protected override bool register => true;

        private bool _eventRegistered = false;

        public override EventHook GetHook(GraphReference reference)
        {
            return new EventHook("OnUIChange");
        }

        protected override void Definition()
        {
            base.Definition();
            elementId = ValueInput<string>("Element ID", "");
            elementName = ValueInput<string>("Element Name", "");
            autoRegister = ValueInput<bool>("Auto Register", true);
            changedElementId = ValueOutput<string>("Element ID");
            hex = ValueOutput<string>("Hex");
            color = ValueOutput<Color>("Color");
        }

        public override void StartListening(GraphStack stack)
        {
            base.StartListening(stack);

            if (!_eventRegistered)
            {
                var flow = Flow.New(stack.ToReference());
                var shouldAutoRegister = flow.GetValue<bool>(autoRegister);

                if (shouldAutoRegister)
                {
                    var targetId = flow.GetValue<string>(elementId);
                    var targetName = flow.GetValue<string>(elementName);
                    string resolvedTarget = UIElementResolverHelper.ResolveElementIdOrName(targetId, targetName);

                    if (!string.IsNullOrEmpty(resolvedTarget))
                    {
                        UIEventAutoRegisterHelper.TryRegisterChangeEventWithRetry(resolvedTarget, "OnColorPickerChanged");
                        _eventRegistered = true;
                    }
                }
            }
        }

        public override void StopListening(GraphStack stack)
        {
            base.StopListening(stack);
            _eventRegistered = false;
        }

        protected override bool ShouldTrigger(Flow flow, CustomEventArgs data)
        {
            if (!data.name.StartsWith("UIChange_")) return false;
            if (data.arguments == null || data.arguments.Length < 1 || !(data.arguments[0] is string text)) return false;
            if (!TryParseColor(text, out _)) return false;

            var targetId = flow.GetValue<string>(elementId);
            var targetName = flow.GetValue<string>(elementName);
            string resolvedTarget = UIElementResolverHelper.ResolveElementIdOrName(targetId, targetName);
            if (string.IsNullOrEmpty(resolvedTarget)) return true;

            var eventElementId = data.name.Replace("UIChange_", "");
            return eventElementId == resolvedTarget;
        }

        protected override void AssignArguments(Flow flow, CustomEventArgs data)
        {
            var eventElementId = data.name.Replace("UIChange_", "");
            flow.SetValue(changedElementId, eventElementId);

            var text = data.arguments != null && data.arguments.Length >= 1 ? data.arguments[0]?.ToString() ?? "" : "";
            flow.SetValue(hex, text);
            flow.SetValue(color, TryParseColor(text, out var parsed) ? parsed : Color.white);
        }

        private static bool TryParseColor(string text, out Color parsed)
        {
            parsed = Color.white;
            if (string.IsNullOrEmpty(text)) return false;
            var s = text.Trim();
            if (!s.StartsWith("#")) s = "#" + s;
            return ColorUtility.TryParseHtmlString(s, out parsed);
        }
    }
}
