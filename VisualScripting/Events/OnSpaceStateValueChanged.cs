using Unity.VisualScripting;
using BS;

namespace BS.VisualScripting
{
    /// <summary>
    /// A space-state value changed, with the real JSON alongside the string form.
    ///
    /// The older <c>OnSpaceStatePropsChanged</c> keeps working untouched; this adds the path, the
    /// JSON, and a delete flag it has no way to express.
    /// </summary>
    [UnitTitle("On Space State Value Changed")]
    [UnitShortTitle("Space Value Changed")]
    [UnitCategory("Events\\BS\\Networking")]
    [TypeIcon(typeof(BSObjectId))]
    public class OnSpaceStateValueChanged : EventUnit<CustomEventArgs>
    {
        [DoNotSerialize][PortLabelHidden] public ValueInput path { get; private set; }
        [DoNotSerialize] public ValueOutput value;
        [DoNotSerialize] public ValueOutput json;
        [DoNotSerialize] public ValueOutput isPublic;
        [DoNotSerialize] public ValueOutput deleted;

        protected override bool register => true;

        public override EventHook GetHook(GraphReference reference) => new EventHook("OnSpaceStateValueChanged");

        protected override void Definition()
        {
            base.Definition();
            path = ValueInput("Path", string.Empty);
            value = ValueOutput<string>("Value");
            json = ValueOutput<string>("JSON");
            isPublic = ValueOutput<bool>("Is Public Property?");
            deleted = ValueOutput<bool>("Deleted");
        }

        /// <summary>An empty filter matches every path — the convention the older node established.</summary>
        protected override bool ShouldTrigger(Flow flow, CustomEventArgs data)
        {
            var compare = flow.GetValue<string>(path)?.Trim();
            return string.IsNullOrEmpty(compare) || data.name == compare;
        }

        protected override void AssignArguments(Flow flow, CustomEventArgs data)
        {
            flow.SetValue(value, data.arguments[0]);
            flow.SetValue(json, data.arguments[1]);
            flow.SetValue(isPublic, data.arguments[2]);
            flow.SetValue(deleted, data.arguments[3]);
        }
    }

    /// <summary>
    /// The outcome of one write you tagged with a Request Id — success or failure.
    ///
    /// Writes are fire-and-trigger because a Visual Scripting unit cannot block a flow on a network
    /// round trip, so this is how a graph sequences on "did my write land". Same shape as the
    /// existing GetUserSavedValue -> OnGetUserState and InjectJs -> OnJsReturnValue pairs.
    /// </summary>
    [UnitTitle("On Space State Result")]
    [UnitShortTitle("Space State Result")]
    [UnitCategory("Events\\BS\\Networking")]
    [TypeIcon(typeof(BSObjectId))]
    public class OnSpaceStateResult : EventUnit<CustomEventArgs>
    {
        [DoNotSerialize][PortLabelHidden] public ValueInput requestId { get; private set; }
        [DoNotSerialize] public ValueOutput ok;
        [DoNotSerialize] public ValueOutput error;
        [DoNotSerialize] public ValueOutput path;
        [DoNotSerialize] public ValueOutput json;

        protected override bool register => true;

        public override EventHook GetHook(GraphReference reference) => new EventHook("OnSpaceStateResult");

        protected override void Definition()
        {
            base.Definition();
            requestId = ValueInput("Request Id", string.Empty);
            ok = ValueOutput<bool>("Ok");
            error = ValueOutput<string>("Error");
            path = ValueOutput<string>("Path");
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
            flow.SetValue(path, data.arguments[2]);
            flow.SetValue(json, data.arguments[3]);
        }
    }

    /// <summary>
    /// ANY failed space-state write, including ones that carry no Request Id.
    ///
    /// That is what earns it a place beside <see cref="OnSpaceStateResult"/>: the legacy
    /// <c>Set Space Prop</c> node has no Request Id port and can never gain one (port labels are
    /// serialized keys, so adding one would break every saved graph), and page-originated writes
    /// have no id either. Without this, a graph using the legacy node could never learn that a
    /// write was refused.
    /// </summary>
    [UnitTitle("On Space State Error")]
    [UnitShortTitle("Space State Error")]
    [UnitCategory("Events\\BS\\Networking")]
    [TypeIcon(typeof(BSObjectId))]
    public class OnSpaceStateError : EventUnit<CustomEventArgs>
    {
        [DoNotSerialize][PortLabelHidden] public ValueInput propName { get; private set; }
        [DoNotSerialize] public ValueOutput code;
        [DoNotSerialize] public ValueOutput message;

        protected override bool register => true;

        public override EventHook GetHook(GraphReference reference) => new EventHook("OnSpaceStateError");

        protected override void Definition()
        {
            base.Definition();
            propName = ValueInput("Property Name", string.Empty);
            code = ValueOutput<string>("Code");
            message = ValueOutput<string>("Message");
        }

        protected override bool ShouldTrigger(Flow flow, CustomEventArgs data)
        {
            var compare = flow.GetValue<string>(propName)?.Trim();
            return string.IsNullOrEmpty(compare) || data.name == compare;
        }

        protected override void AssignArguments(Flow flow, CustomEventArgs data)
        {
            flow.SetValue(code, data.arguments[0]);
            flow.SetValue(message, data.arguments[1]);
        }
    }
}
