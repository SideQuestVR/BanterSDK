using Unity.VisualScripting;
using BS;
using BS.Utilities.Async;
using Newtonsoft.Json.Linq;

namespace BS.VisualScripting
{
    /// <summary>
    /// Shared helpers for the JSON space/user state nodes.
    ///
    /// Every value port is a <c>string</c> rather than an <c>object</c>: an object ValueInput gets
    /// no inspector field, so a graph author could never type a literal into it — the port would
    /// always have to be wired from somewhere. A companion "Value Is JSON?" bool decides how the
    /// text is interpreted instead.
    /// </summary>
    internal static class StateUnitUtil
    {
        /// <summary>
        /// Turn an author's text into a JSON token. <paramref name="isJson"/> false keeps today's
        /// mental model exactly — "100" stays the string "100". True parses first, so numbers,
        /// booleans, arrays and objects all work, and malformed text fails fast rather than
        /// silently degrading to a string.
        /// </summary>
        internal static bool TryBuildValue(string text, bool isJson, out JToken value)
        {
            if (!isJson) { value = JValue.CreateString(text ?? ""); return true; }
            try { value = JToken.Parse(string.IsNullOrEmpty(text) ? "null" : text); return true; }
            catch (System.Exception) { value = null; return false; }
        }

        internal static void Send(BSStateTarget target, string op, JObject body, string requestId)
        {
            var scene = BSScene.Instance();
            if (scene == null) return;

            string hook = target == BSStateTarget.Space ? "OnSpaceStateResult" : "OnUserStateResult";
            var request = new BSStateRequest(reply =>
            {
                // Correlated results only: an empty Request Id means the author does not care, so
                // no event fires. Uncorrelated FAILURES still reach On*StateError.
                if (string.IsNullOrEmpty(requestId)) return;
                JObject parsed;
                try { parsed = JObject.Parse(reply ?? "{}"); } catch (System.Exception) { parsed = new JObject(); }
                UnityMainThreadTaskScheduler.Default.Enqueue(TaskRunner.Track(() =>
                {
                    EventBus.Trigger(hook, new CustomEventArgs(requestId, new object[]
                    {
                        (bool?)parsed["ok"] ?? false,
                        (string)parsed["error"] ?? "",
                        (string)body["path"] ?? "",
                        parsed["value"] == null ? "" : parsed["value"].ToString(Newtonsoft.Json.Formatting.None)
                    }));
                }, "StateUnitUtil.Result"));
            })
            {
                Target = target,
                Op = op,
                Path = (string)body["path"],
                Json = body.ToString(Newtonsoft.Json.Formatting.None),
                RequestId = requestId
            };

            scene.EnqueueStateOp(request);
        }

        internal static void ReportBadJson(string hook, string path)
        {
            EventBus.Trigger(hook, new CustomEventArgs(path, new object[]
            {
                "invalid_value", "the value isn't valid JSON"
            }));
        }
    }

    [UnitTitle("Set Space State Value")]
    [UnitShortTitle("Set Space Value")]
    [UnitCategory("BS\\Networking")]
    [TypeIcon(typeof(BSObjectId))]
    public class SetSpaceStateValue : Unit
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
                    StateUnitUtil.ReportBadJson("OnSpaceStateError", p);
                    return outputTrigger;
                }

                StateUnitUtil.Send(BSStateTarget.Space, "set", new JObject
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

    [UnitTitle("Delete Space State Value")]
    [UnitShortTitle("Delete Space Value")]
    [UnitCategory("BS\\Networking")]
    [TypeIcon(typeof(BSObjectId))]
    public class DeleteSpaceStateValue : Unit
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
                StateUnitUtil.Send(BSStateTarget.Space, "delete", new JObject
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
    [UnitTitle("Get Space State Value")]
    [UnitShortTitle("Get Space Value")]
    [UnitCategory("BS\\Networking")]
    [TypeIcon(typeof(BSObjectId))]
    public class GetSpaceStateValue : Unit
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
            try { return JObject.Parse(scene.events.GetSpaceStateValue(flow.GetValue<string>(path)) ?? "{}"); }
            catch (System.Exception) { return new JObject(); }
        }

        private string Read(Flow flow, string field, string fallback) => (string)Envelope(flow)[field] ?? fallback;
        private bool ReadBool(Flow flow, string field) => (bool?)Envelope(flow)[field] ?? false;
    }
}
