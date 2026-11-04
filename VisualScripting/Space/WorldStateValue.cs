using Unity.VisualScripting;
using BS;
using Newtonsoft.Json.Linq;

namespace BS.VisualScripting
{
    [UnitTitle("Set World State Value")]
    [UnitShortTitle("Set World Value")]
    [UnitCategory("BS\\Networking")]
    [TypeIcon(typeof(BSObjectId))]
    public class SetWorldStateValue : Unit
    {
        [DoNotSerialize] public ControlInput inputTrigger;
        [DoNotSerialize] public ControlOutput outputTrigger;
        [DoNotSerialize] public ValueInput path;
        [DoNotSerialize] public ValueInput value;
        [DoNotSerialize] public ValueInput valueIsJson;
        [DoNotSerialize] public ValueInput isPublic;
        [DoNotSerialize] public ValueInput requestId;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) =>
            {
                var p = flow.GetValue<string>(path);
                var raw = flow.GetValue<string>(value);
                var asJson = flow.GetValue<bool>(valueIsJson);
                var pub = flow.GetValue<bool>(isPublic);
                var req = flow.GetValue<string>(requestId);

                if (!StateUnitUtil.TryBuildValue(raw, asJson, out JToken token))
                {
                    StateUnitUtil.ReportBadJson("OnWorldStateError", p);
                    return outputTrigger;
                }

                StateUnitUtil.Send(BSStateTarget.World, "set", new JObject
                {
                    ["path"] = p,
                    ["value"] = token,
                    ["protected"] = !pub
                }, req);
                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            path = ValueInput("Path", string.Empty);
            value = ValueInput("Value", string.Empty);
            valueIsJson = ValueInput("Value Is JSON?", false);
            isPublic = ValueInput("Is Public Property?", true);
            requestId = ValueInput("Request Id", string.Empty);
        }
    }

    [UnitTitle("Delete World State Value")]
    [UnitShortTitle("Delete World Value")]
    [UnitCategory("BS\\Networking")]
    [TypeIcon(typeof(BSObjectId))]
    public class DeleteWorldStateValue : Unit
    {
        [DoNotSerialize] public ControlInput inputTrigger;
        [DoNotSerialize] public ControlOutput outputTrigger;
        [DoNotSerialize] public ValueInput path;
        [DoNotSerialize] public ValueInput isPublic;
        [DoNotSerialize] public ValueInput requestId;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) =>
            {
                StateUnitUtil.Send(BSStateTarget.World, "delete", new JObject
                {
                    ["path"] = flow.GetValue<string>(path),
                    ["protected"] = !flow.GetValue<bool>(isPublic)
                }, flow.GetValue<string>(requestId));
                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            path = ValueInput("Path", string.Empty);
            isPublic = ValueInput("Is Public Property?", true);
            requestId = ValueInput("Request Id", string.Empty);
        }
    }

    /// <summary>
    /// Pure value unit — no control ports, because it reads the local mirror synchronously and so
    /// has nothing to await and nothing to fail.
    /// </summary>
    [UnitTitle("Get World State Value")]
    [UnitShortTitle("Get World Value")]
    [UnitCategory("BS\\Networking")]
    [TypeIcon(typeof(BSObjectId))]
    public class GetWorldStateValue : Unit
    {
        [DoNotSerialize] public ValueInput path;
        [DoNotSerialize] public ValueOutput value;
        [DoNotSerialize] public ValueOutput json;
        [DoNotSerialize] public ValueOutput exists;
        [DoNotSerialize] public ValueOutput isPublic;

        protected override void Definition()
        {
            path = ValueInput("Path", string.Empty);
            value = ValueOutput<string>("Value", f => Read(f, "value", ""));
            json = ValueOutput<string>("JSON", f => Read(f, "json", ""));
            exists = ValueOutput<bool>("Exists", f => ReadBool(f, "exists"));
            isPublic = ValueOutput<bool>("Is Public Property?", f => ReadBool(f, "isPublic"));
        }

        private JObject Envelope(Flow flow)
        {
            var scene = BSScene.Instance();
            if (scene == null) return new JObject();
            try { return JObject.Parse(scene.events.GetWorldStateValue(flow.GetValue<string>(path)) ?? "{}"); }
            catch (System.Exception) { return new JObject(); }
        }

        private string Read(Flow flow, string field, string fallback) => (string)Envelope(flow)[field] ?? fallback;
        private bool ReadBool(Flow flow, string field) => (bool?)Envelope(flow)[field] ?? false;
    }
}
