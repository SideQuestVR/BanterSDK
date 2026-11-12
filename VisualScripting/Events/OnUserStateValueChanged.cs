using Unity.VisualScripting;
using BS;

namespace BS.VisualScripting
{
    /// <summary>Any participant's prop changed — including your own echo.</summary>
    [UnitTitle("On User State Value Changed")]
    [UnitShortTitle("User Value Changed")]
    [UnitCategory("Events\\BS\\User")]
    [TypeIcon(typeof(BSObjectId))]
    public class OnUserStateValueChanged : EventUnit<CustomEventArgs>
    {
        [DoNotSerialize][PortLabelHidden] public ValueInput key { get; private set; }
        [DoNotSerialize] public ValueOutput value;
        [DoNotSerialize] public ValueOutput json;
        [DoNotSerialize] public ValueOutput userId;
        [DoNotSerialize] public ValueOutput isLocal;
        [DoNotSerialize] public ValueOutput deleted;

        protected override bool register => true;

        public override EventHook GetHook(GraphReference reference) => new EventHook("OnUserStateValueChanged");

        protected override void Definition()
        {
            base.Definition();
            key = ValueInput("Key", string.Empty);
            value = ValueOutput<string>("Value");
            json = ValueOutput<string>("JSON");
            userId = ValueOutput<string>("UserId");
            isLocal = ValueOutput<bool>("Is Local");
            deleted = ValueOutput<bool>("Deleted");
        }

        protected override bool ShouldTrigger(Flow flow, CustomEventArgs data)
        {
            var compare = flow.GetValue<string>(key)?.Trim();
            return string.IsNullOrEmpty(compare) || data.name == compare;
        }

        protected override void AssignArguments(Flow flow, CustomEventArgs data)
        {
            flow.SetValue(value, data.arguments[0]);
            flow.SetValue(json, data.arguments[1]);
            flow.SetValue(userId, data.arguments[2]);
            flow.SetValue(isLocal, data.arguments[3]);
            flow.SetValue(deleted, data.arguments[4]);
        }
    }

    /// <summary>Outcome of one user-state write tagged with a Request Id.</summary>
    [UnitTitle("On User State Result")]
    [UnitShortTitle("User State Result")]
    [UnitCategory("Events\\BS\\User")]
    [TypeIcon(typeof(BSObjectId))]
    public class OnUserStateResult : EventUnit<CustomEventArgs>
    {
        [DoNotSerialize][PortLabelHidden] public ValueInput requestId { get; private set; }
        [DoNotSerialize] public ValueOutput ok;
        [DoNotSerialize] public ValueOutput error;
        [DoNotSerialize] public ValueOutput key;
        [DoNotSerialize] public ValueOutput json;

        protected override bool register => true;

        public override EventHook GetHook(GraphReference reference) => new EventHook("OnUserStateResult");

        protected override void Definition()
        {
            base.Definition();
            requestId = ValueInput("Request Id", string.Empty);
            ok = ValueOutput<bool>("Ok");
            error = ValueOutput<string>("Error");
            key = ValueOutput<string>("Key");
            json = ValueOutput<string>("JSON");
        }

        protected override bool ShouldTrigger(Flow flow, CustomEventArgs data)
        {
            var compare = flow.GetValue<string>(requestId)?.Trim();
            return !string.IsNullOrEmpty(compare) && data.name == compare;
        }

        protected override void AssignArguments(Flow flow, CustomEventArgs data)
        {
            flow.SetValue(ok, data.arguments[0]);
            flow.SetValue(error, data.arguments[1]);
            flow.SetValue(key, data.arguments[2]);
            flow.SetValue(json, data.arguments[3]);
        }
    }

    /// <summary>
    /// ANY failed user-state write, including uncorrelated ones — the legacy page-side
    /// <c>SetUserProps</c> path is fire-and-forget and can carry no Request Id.
    /// </summary>
    [UnitTitle("On User State Error")]
    [UnitShortTitle("User State Error")]
    [UnitCategory("Events\\BS\\User")]
    [TypeIcon(typeof(BSObjectId))]
    public class OnUserStateError : EventUnit<CustomEventArgs>
    {
        [DoNotSerialize][PortLabelHidden] public ValueInput key { get; private set; }
        [DoNotSerialize] public ValueOutput code;
        [DoNotSerialize] public ValueOutput message;

        protected override bool register => true;

        public override EventHook GetHook(GraphReference reference) => new EventHook("OnUserStateError");

        protected override void Definition()
        {
            base.Definition();
            key = ValueInput("Key", string.Empty);
            code = ValueOutput<string>("Code");
            message = ValueOutput<string>("Message");
        }

        protected override bool ShouldTrigger(Flow flow, CustomEventArgs data)
        {
            var compare = flow.GetValue<string>(key)?.Trim();
            return string.IsNullOrEmpty(compare) || data.name == compare;
        }

        protected override void AssignArguments(Flow flow, CustomEventArgs data)
        {
            flow.SetValue(code, data.arguments[0]);
            flow.SetValue(message, data.arguments[1]);
        }
    }
}
