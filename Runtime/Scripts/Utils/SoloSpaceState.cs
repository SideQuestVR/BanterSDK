using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BS.Utilities.Async;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using EventBus = Unity.VisualScripting.EventBus;
using CustomEventArgs = Unity.VisualScripting.CustomEventArgs;

namespace BS
{
    /// <summary>
    /// Space state for SDK Play mode with no network host: one player, kept in memory until Play stops. It answers the
    /// page and Visual Scripting the way a room does, with objects flattened to their leaves, so set, merge and delete
    /// behave as they do in the app. A room's limits (value size, key count, who may write a protected key) don't apply.
    /// Steps aside while <see cref="BSNetworkHost.Active"/>. The app has its own space state and never attaches this.
    /// </summary>
    internal sealed class SoloSpaceState
    {
        internal struct Change
        {
            public string Key;
            public JToken Value;
            public JToken Previous;
            public bool Deleted;
            public bool IsPublic;
        }

        const string Ok = "{\"ok\":true}";

        static readonly string[] Delimiters =
        {
            MessageDelimiters.PRIMARY, MessageDelimiters.SECONDARY, MessageDelimiters.TERTIARY,
            MessageDelimiters.BATCH, MessageDelimiters.WINDOW
        };

        readonly Dictionary<string, JToken> values = new Dictionary<string, JToken>();
        readonly HashSet<string> protectedKeys = new HashSet<string>();
        long revision;

        internal event Action<Change> Changed;

        /// <summary>Starts answering these events. <paramref name="link"/> is read each time: the page's link comes later.</summary>
        internal static SoloSpaceState Attach(BSSceneEvents events, Func<BSLink> link)
        {
            var state = new SoloSpaceState();
            state.Changed += change => state.Publish(link(), change);
            // A host's bridge puts its own reader here while it runs, and this one back when it stops.
            events.GetSpaceStateValue = state.Read;
            events.OnPublicSpaceStateChanged.AddListener((key, value) => state.WriteFromPage(key, value, false));
            events.OnProtectedSpaceStateChanged.AddListener((key, value) => state.WriteFromPage(key, value, true));
            events.OnStateRequest.AddListener(request =>
            {
                if (BSNetworkHost.Active || request == null || request.Target != BSStateTarget.Space) return;
                request.Handled = true;
                request.Respond(state.Serve(request.Op, request.Json));
            });
            // A page reload loses the page's copy, and a room sends it again.
            events.OnSceneReady.AddListener(() =>
            {
                if (!BSNetworkHost.Active) state.SendAll(link());
            });
            return state;
        }

        /// <summary>The reply to one JSON state op, as the page and the Visual Scripting nodes expect it.</summary>
        internal string Serve(string op, string json)
        {
            JObject body;
            try { body = JObject.Parse(string.IsNullOrEmpty(json) ? "{}" : json); }
            catch (JsonException) { return Failure("invalid_value"); }

            var path = (string)body["path"];
            var value = body["value"];
            var isProtected = (bool?)body["protected"] ?? false;
            switch (op)
            {
                case "get":
                    var reply = new JObject { ["ok"] = true };
                    if (!string.IsNullOrEmpty(path) && values.TryGetValue(path, out var current)) reply["value"] = current;
                    return reply.ToString(Formatting.None);
                case "getAll":
                    return new JObject
                    {
                        ["ok"] = true,
                        ["revision"] = revision,
                        ["public"] = Scope(false),
                        ["protected"] = Scope(true)
                    }.ToString(Formatting.None);
                case "set":
                case "merge":
                    if (string.IsNullOrEmpty(path)) return Failure("invalid_path");
                    if (value == null) return Failure("invalid_value");
                    // A room's set is a delete and a write, so its listeners see both, whatever the value.
                    if (op == "set") Remove(path);
                    Write(path, value, isProtected);
                    return Ok;
                case "delete":
                    if (string.IsNullOrEmpty(path)) return Failure("invalid_path");
                    return Remove(path) ? Ok : Failure("not_found");
                default:
                    return Failure("unknown_message_type");
            }
        }

        /// <summary>A write from SetPublicSpaceProps or SetProtectedSpaceProps, or Set Space State Property. Always a string.</summary>
        internal void WriteProp(string key, string value, bool isProtected)
        {
            if (!string.IsNullOrEmpty(key)) Write(key, JValue.CreateString(value ?? ""), isProtected);
        }

        /// <summary>The envelope <see cref="BSSceneEvents.GetSpaceStateValue"/> returns.</summary>
        internal string Read(string key)
        {
            if (string.IsNullOrEmpty(key) || !values.TryGetValue(key, out var value)) return "{\"exists\":false}";
            return new JObject
            {
                ["exists"] = true,
                ["value"] = Render(value),
                ["json"] = value.ToString(Formatting.None),
                ["isPublic"] = !protectedKeys.Contains(key)
            }.ToString(Formatting.None);
        }

        // The bus can deliver these off the main thread, and every other touch of this state is on it.
        void WriteFromPage(string key, string value, bool isProtected)
        {
            if (BSNetworkHost.Active) return;
            UnityMainThreadTaskScheduler.Default.Enqueue(TaskRunner.Track(() => WriteProp(key, value, isProtected),
                $"{nameof(SoloSpaceState)}.{nameof(WriteFromPage)}"));
        }

        void Write(string path, JToken value, bool isProtected)
        {
            if (value is JObject map && map.HasValues)
            {
                foreach (var property in map.Properties()) Write(path + "." + property.Name, property.Value, isProtected);
                return;
            }
            values.TryGetValue(path, out var previous);
            if (previous != null && JToken.DeepEquals(previous, value)) return;
            if (isProtected) protectedKeys.Add(path);
            values[path] = value;
            revision++;
            Changed?.Invoke(new Change { Key = path, Value = value, Previous = previous, IsPublic = !protectedKeys.Contains(path) });
        }

        bool Remove(string path)
        {
            var under = path + ".";
            var gone = values.Keys.Where(key => key == path || key.StartsWith(under, StringComparison.Ordinal)).ToList();
            foreach (var key in gone)
            {
                var previous = values[key];
                values.Remove(key);
                var isPublic = !protectedKeys.Remove(key);
                revision++;
                Changed?.Invoke(new Change { Key = key, Previous = previous, Deleted = true, IsPublic = isPublic });
            }
            return gone.Count > 0;
        }

        JObject Scope(bool isProtected)
        {
            var scope = new JObject();
            foreach (var pair in values)
            {
                if (protectedKeys.Contains(pair.Key) == isProtected) scope[pair.Key] = pair.Value.DeepClone();
            }
            return scope;
        }

        void SendAll(BSLink link)
        {
            if (link == null) return;
            var legacyPublic = new JObject();
            var legacyProtected = new JObject();
            foreach (var pair in values)
            {
                (protectedKeys.Contains(pair.Key) ? legacyProtected : legacyPublic)[Clean(pair.Key)] = Clean(Render(pair.Value));
            }
            link.OnFullSpaceState(new JObject { ["protected"] = legacyProtected, ["public"] = legacyPublic }.ToString(Formatting.None));
            link.OnFullSpaceStateJson(Base64(new JObject
            {
                ["revision"] = revision,
                ["full"] = true,
                ["canProtect"] = true,
                ["public"] = Scope(false),
                ["protected"] = Scope(true)
            }));
        }

        // A graph that throws must not cost the page its reply.
        void Publish(BSLink link, Change change)
        {
            var value = change.Deleted ? "" : Render(change.Value);
            try
            {
                if (link != null)
                {
                    link.OnSpaceStateChanged(Clean(change.Key), Clean(value), Clean(Render(change.Previous)), change.IsPublic);
                    link.OnSpaceStateJson(Base64(new JObject
                    {
                        ["revision"] = revision,
                        ["full"] = false,
                        ["changes"] = new JArray
                        {
                            new JObject
                            {
                                ["path"] = change.Key,
                                ["value"] = change.Deleted ? null : change.Value?.DeepClone(),
                                ["oldValue"] = change.Previous?.DeepClone(),
                                ["scope"] = change.IsPublic ? "public" : "protected",
                                ["deleted"] = change.Deleted
                            }
                        }
                    }));
                }
                // Argument orders the OnSpaceStatePropsChanged and OnSpaceStateValueChanged nodes read.
                EventBus.Trigger("OnSpaceStatePropsChanged", new CustomEventArgs(change.Key, new object[] { value, change.IsPublic }));
                EventBus.Trigger("OnSpaceStateValueChanged", new CustomEventArgs(change.Key, new object[]
                {
                    value,
                    change.Deleted || change.Value == null ? "" : change.Value.ToString(Formatting.None),
                    change.IsPublic,
                    change.Deleted
                }));
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogException(e);
            }
        }

        static string Failure(string code) => new JObject { ["ok"] = false, ["error"] = code }.ToString(Formatting.None);

        static string Render(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return "";
            return token.Type == JTokenType.String ? token.Value<string>() : token.ToString(Formatting.None);
        }

        // The string-only events go on the bus as they are, so they can't carry its delimiters.
        static string Clean(string text)
        {
            foreach (var delimiter in Delimiters) text = text.Replace(delimiter, "");
            return text;
        }

        static string Base64(JToken token) => Convert.ToBase64String(Encoding.UTF8.GetBytes(token.ToString(Formatting.None)));
    }
}
