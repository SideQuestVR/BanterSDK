using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.VisualScripting;
using UnityEngine;

namespace BS.VisualScripting
{
    /// <summary>
    /// What the "BS\Recording" units share: the request helper over <see cref="BSHostExtensions"/>
    /// and the bridge that turns host-extension events into Visual Scripting events.
    /// </summary>
    /// <remarks>
    /// A static helper rather than a base Unit, like PersistFileUnitSupport, so the node catalogue
    /// never lists an abstract entry.
    ///
    /// The wire names below are the contract with the app's "recording" and "newsroom" extension
    /// handlers and with the page API (BS.Recording / BS.Newsroom); they never change.
    /// </remarks>
    internal static class RecordingUnitSupport
    {
        internal const string RecordingExtension = "recording";
        internal const string NewsroomExtension = "newsroom";

        internal const string StateEvent = "state";
        internal const string ProgramEvent = "program";

        internal const string StateHook = "OnRecordingStateChanged";
        internal const string ProgramHook = "OnProgramChanged";

        /// <summary>The Error output when the app has not answered by the time the unit exits.</summary>
        internal const string Pending = "pending";

        /// <summary>
        /// Send one request and return the reply if the app answered synchronously (the recording
        /// and newsroom handlers do), or null if the answer is still outstanding.
        /// </summary>
        internal static JObject RequestNow(string extension, string op, JObject body, GameObject target = null)
        {
            string reply = null;
            BSHostExtensions.Request(extension, op, body.ToString(Formatting.None), r => reply = r, target);
            if (reply == null) return null;
            try { return JObject.Parse(reply); }
            catch (JsonException) { return new JObject { ["ok"] = false, ["error"] = "internal_error" }; }
        }

        /// <summary>"" on success, the reply's error code on failure, <see cref="Pending"/> with no reply yet.</summary>
        internal static string ErrorOf(JObject reply)
        {
            if (reply == null) return Pending;
            if ((bool?)reply["ok"] ?? false) return "";
            return (string)reply["error"] ?? "internal_error";
        }

        internal static string StringOf(JToken token, string field)
        {
            if (!(token is JObject obj)) return "";
            var value = obj[field];
            if (value == null || value.Type == JTokenType.Null) return "";
            return value.Type == JTokenType.String ? (string)value : value.ToString(Formatting.None);
        }

        // BSHostExtensions clears its listeners at SubsystemRegistration, which always runs before
        // this, so the subscription survives entering play mode with or without a domain reload.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void HookEvents()
        {
            BSHostExtensions.Emitted -= OnEmitted;
            BSHostExtensions.Emitted += OnEmitted;
        }

        // Main thread (BSHostExtensions guarantees it), which EventBus requires.
        private static void OnEmitted(string extension, string evt, string json)
        {
            if (extension == RecordingExtension && evt == StateEvent)
            {
                var data = Parse(json);
                // Positional; OnRecordingStateChanged reads them by index.
                EventBus.Trigger(StateHook, new CustomEventArgs(StateHook, new object[]
                {
                    StringOf(data, "state"),
                    StringOf(data, "takeId"),
                    StringOf(data, "error"),
                    json ?? ""
                }));
            }
            else if (extension == NewsroomExtension && evt == ProgramEvent)
            {
                var data = Parse(json);
                // Positional; OnProgramChanged reads them by index.
                EventBus.Trigger(ProgramHook, new CustomEventArgs(ProgramHook, new object[]
                {
                    StringOf(data, "program"),
                    StringOf(data, "preview"),
                    StringOf(data, "takeId"),
                    json ?? ""
                }));
            }
        }

        private static JToken Parse(string json)
        {
            try { return JToken.Parse(string.IsNullOrEmpty(json) ? "null" : json); }
            catch (JsonException) { return null; }
        }
    }
}
