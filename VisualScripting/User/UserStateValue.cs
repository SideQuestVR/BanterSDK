using Unity.VisualScripting;
using BS;
using Newtonsoft.Json.Linq;

namespace BS.VisualScripting
{
    /// <summary>
    /// Set one of MY user props.
    ///
    /// There is deliberately no user-id input: user state is owner-writes-only (the server derives
    /// the owner from the connection and ignores anything a client claims), so a port that could
    /// only ever hold one legal value would be a trap. The title says "My" for the same reason.
    ///
    /// "Moderators Can Write?" is the scope, and it governs WRITES only — never who can read.
    /// Everything in a room is readable by every participant. Left false, the prop is yours alone.
    /// </summary>
    [UnitTitle("Set My User State Value")]
    [UnitShortTitle("Set My User Value")]
    [UnitCategory("BS\\User")]
    [TypeIcon(typeof(BSObjectId))]
    public class SetUserStateValue : Unit
    {
        [DoNotSerialize] public ControlInput inputTrigger;
        [DoNotSerialize] public ControlOutput outputTrigger;
        [DoNotSerialize] public ValueInput key;
        [DoNotSerialize] public ValueInput value;
        [DoNotSerialize] public ValueInput valueIsJson;
        [DoNotSerialize] public ValueInput moderatorsCanWrite;
        [DoNotSerialize] public ValueInput requestId;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) =>
            {
                var k = flow.GetValue<string>(key);
                var raw = flow.GetValue<string>(value);
                var asJson = flow.GetValue<bool>(valueIsJson);
                var modWritable = flow.GetValue<bool>(moderatorsCanWrite);
                var req = flow.GetValue<string>(requestId);

                if (!StateUnitUtil.TryBuildValue(raw, asJson, out JToken token))
                {
                    StateUnitUtil.ReportBadJson("OnUserStateError", k);
                    return outputTrigger;
                }

                StateUnitUtil.Send(BSStateTarget.User, "set", new JObject
                {
                    ["path"] = k,
                    ["value"] = token,
                    ["moderatorsCanWrite"] = modWritable
                }, req);
                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            key = ValueInput("Key", string.Empty);
            value = ValueInput("Value", string.Empty);
            valueIsJson = ValueInput("Value Is JSON?", false);
            moderatorsCanWrite = ValueInput("Moderators Can Write?", false);
            requestId = ValueInput("Request Id", string.Empty);
        }
    }

    [UnitTitle("Delete My User State Value")]
    [UnitShortTitle("Delete My User Value")]
    [UnitCategory("BS\\User")]
    [TypeIcon(typeof(BSObjectId))]
    public class DeleteUserStateValue : Unit
    {
        [DoNotSerialize] public ControlInput inputTrigger;
        [DoNotSerialize] public ControlOutput outputTrigger;
        [DoNotSerialize] public ValueInput key;
        [DoNotSerialize] public ValueInput moderatorsCanWrite;
        [DoNotSerialize] public ValueInput requestId;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) =>
            {
                StateUnitUtil.Send(BSStateTarget.User, "delete", new JObject
                {
                    ["path"] = flow.GetValue<string>(key),
                    ["moderatorsCanWrite"] = flow.GetValue<bool>(moderatorsCanWrite)
                }, flow.GetValue<string>(requestId));
                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            key = ValueInput("Key", string.Empty);
            moderatorsCanWrite = ValueInput("Moderators Can Write?", false);
            requestId = ValueInput("Request Id", string.Empty);
        }
    }

    /// <summary>
    /// Read any participant's prop from the local mirror. This is the "read another user" surface —
    /// user props are broadcast to everyone, so reading needs no round trip.
    /// </summary>
    [UnitTitle("Get User State Value")]
    [UnitShortTitle("Get User Value")]
    [UnitCategory("BS\\User")]
    [TypeIcon(typeof(BSObjectId))]
    public class GetUserStateValue : Unit
    {
        [DoNotSerialize] public ValueInput uid;
        [DoNotSerialize] public ValueInput key;
        [DoNotSerialize] public ValueOutput value;
        [DoNotSerialize] public ValueOutput json;
        [DoNotSerialize] public ValueOutput exists;

        protected override void Definition()
        {
            // "me" matches the convention the existing *UserSavedValue nodes established.
            uid = ValueInput("UserId Or Me", "me");
            key = ValueInput("Key", string.Empty);
            value = ValueOutput<string>("Value", f => Read(f, "value", ""));
            json = ValueOutput<string>("JSON", f => Read(f, "json", ""));
            exists = ValueOutput<bool>("Exists", f => (bool?)Envelope(f)["exists"] ?? false);
        }

        private JObject Envelope(Flow flow)
        {
            var scene = BSScene.Instance();
            if (scene == null) return new JObject();
            try
            {
                var id = flow.GetValue<string>(uid);
                if (id == "me") id = "";
                return JObject.Parse(scene.events.GetUserStateValue(id, flow.GetValue<string>(key)) ?? "{}");
            }
            catch (System.Exception) { return new JObject(); }
        }

        private string Read(Flow flow, string field, string fallback) => (string)Envelope(flow)[field] ?? fallback;
    }
}
