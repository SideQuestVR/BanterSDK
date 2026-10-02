// <mirror source="Packages/com.sidequest.packetparty/Runtime/Contracts/RoomStateContracts.cs" sha256="58fe3357aeda7b4c022d6eb1b454e062913975887c837df567d7c5fef6e5377b" mode="port" />
// <mirror source="Packages/com.sidequest.packetparty/Runtime/Contracts/UserStateContracts.cs" sha256="0b91705d38f3faecc385f14751be09340607ef56dd83cd85c8e8288dee40988f" mode="port" />
// <mirror source="Packages/com.sidequest.packetparty/Runtime/Util/JsonSettings.cs" sha256="3c60de9688b70cf32211d5445928e5f471a776818d423cec74a483447d6bb410" mode="port" />
// The PacketParty client contracts the state services parse the relay's app.state.* / app.userState.* messages into
// (RELAY.md 4 and 5 use exactly these shapes). The class bodies are verbatim; StateTree and the editable-own-state
// helpers are left out because nothing here uses them.
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace BS.LocalMultiplayer.State
{
    /// <summary>
    /// Room-state websocket protocol (`app.state.*`) contracts. Mirrors
    /// <c>roomState.ts</c> in <c>@packetparty/protocol</c>: ops ride the runtime
    /// WS connection with requestId correlation; the server replies with
    /// <c>app.state.result</c>, pushes <c>app.state.changed</c> to the room, and
    /// sends an <c>app.state.snapshot</c> after the welcome.
    /// </summary>
    public static class RoomStateMessages
    {
        public const string Set = "app.state.set";
        public const string Get = "app.state.get";
        public const string Delete = "app.state.delete";
        public const string Increment = "app.state.increment";
        public const string Decrement = "app.state.decrement";
        public const string Toggle = "app.state.toggle";
        public const string CompareAndSet = "app.state.compareAndSet";
        public const string Batch = "app.state.batch";
        public const string Protect = "app.state.protect";
        public const string Result = "app.state.result";
        public const string Changed = "app.state.changed";
        public const string Snapshot = "app.state.snapshot";
    }

    /// <summary>
    /// Write policy for durable room (space) state. Both scopes are readable by every
    /// participant in the room; <see cref="Protected"/> is write protection, not secrecy.
    /// </summary>
    public enum RoomStateScope
    {
        /// <summary>
        /// Any live room participant may write the path. A public mutation is rejected if
        /// the path has previously been made protected.
        /// </summary>
        Public,

        /// <summary>
        /// The server atomically makes the path write-protected and applies the mutation
        /// after either a correctly bound runtime JWT granting <c>canModerate</c> or the
        /// app's authorization webhook authorizes it. Protection is raise-only for
        /// clients; protected values remain readable by the room.
        /// </summary>
        Protected
    }

    /// <summary>Reply to any app.state.* request.</summary>
    public sealed class RoomStateResult
    {
        [JsonProperty("metadata")]
        public JObject Metadata { get; set; }

        [JsonProperty("ok")]
        public bool Ok { get; set; }

        [JsonProperty("revision")]
        public long Revision { get; set; }

        /// <summary>Single-path read value, CAS current-value on conflict, or op result value.</summary>
        [JsonProperty("value")]
        public JToken Value { get; set; }

        /// <summary>Full flattened snapshot for a path-less get.</summary>
        [JsonProperty("state")]
        public Dictionary<string, JToken> State { get; set; }

        /// <summary>
        /// Authoritative protected-prefix replacement returned with a path-less get.
        /// This lets clients repair both values and write-policy metadata after a
        /// revision gap.
        /// </summary>
        [JsonProperty("protectedPrefixes")]
        public List<string> ProtectedPrefixes { get; set; }

        [JsonProperty("error")]
        public string Error { get; set; }

        /// <summary>
        /// Per-operation outcomes for <c>app.state.batch</c>, in request order.
        /// A batch reports <c>error: "batch_failed"</c> at the top level and the
        /// offending op only here, so this is the only way to tell which failed.
        /// </summary>
        [JsonProperty("outcomes")]
        public List<RoomStateOutcome> Outcomes { get; set; }
    }

    /// <summary>One entry of <see cref="RoomStateResult.Outcomes"/>.</summary>
    public sealed class RoomStateOutcome
    {
        [JsonProperty("ok")]
        public bool Ok { get; set; }

        [JsonProperty("error")]
        public string Error { get; set; }

        /// <summary>Current value on a cas_conflict / type_mismatch, or the op's result.</summary>
        [JsonProperty("value")]
        public JToken Value { get; set; }
    }

    public sealed class RoomStateChange
    {
        [JsonProperty("path")]
        public string Path { get; set; }

        [JsonProperty("value")]
        public JToken Value { get; set; }

        [JsonProperty("deleted")]
        public bool Deleted { get; set; }
    }

    /// <summary>Server push: accepted changes, broadcast to every peer in the room.</summary>
    public sealed class RoomStateChangedEvent
    {
        [JsonProperty("scope")]
        public string Scope { get; set; }

        [JsonProperty("revision")]
        public long Revision { get; set; }

        [JsonProperty("changes")]
        public List<RoomStateChange> Changes { get; set; } = new List<RoomStateChange>();

        /// <summary>
        /// Authoritative replacement for the protected-prefix set when protection changed.
        /// Null on ordinary value-only changes and on events from older servers.
        /// </summary>
        [JsonProperty("protectedPrefixes")]
        public List<string> ProtectedPrefixes { get; set; }
    }

    /// <summary>
    /// Authoritative room-state snapshot, received during late-join hydration or emitted
    /// locally after automatic revision-gap reconciliation.
    /// </summary>
    public sealed class RoomStateSnapshotEvent
    {
        [JsonProperty("revision")]
        public long Revision { get; set; }

        /// <summary>Values writable by any live room participant.</summary>
        [JsonProperty("public")]
        public Dictionary<string, JToken> Public { get; set; } = new Dictionary<string, JToken>();

        /// <summary>Write-protected values. These are visible to all room participants.</summary>
        [JsonProperty("protected")]
        public Dictionary<string, JToken> Protected { get; set; } = new Dictionary<string, JToken>();

        /// <summary>
        /// Raise-only prefixes whose values require protected-write authorization.
        /// </summary>
        [JsonProperty("protectedPrefixes")]
        public List<string> ProtectedPrefixes { get; set; } = new List<string>();
    }

    /// <summary>Owner-only, room-session-scoped ephemeral user-state messages.</summary>
    public static class UserStateMessages
    {
        public const string Set = "app.userState.set";
        public const string Get = "app.userState.get";
        public const string Delete = "app.userState.delete";
        public const string Increment = "app.userState.increment";
        public const string Decrement = "app.userState.decrement";
        public const string Toggle = "app.userState.toggle";
        public const string CompareAndSet = "app.userState.compareAndSet";
        public const string Batch = "app.userState.batch";
        public const string Result = "app.userState.result";
        public const string Changed = "app.userState.changed";
        public const string Snapshot = "app.userState.snapshot";
        public const string Removed = "app.userState.removed";
    }

    public sealed class UserStateResult
    {
        [JsonProperty("ok")] public bool Ok { get; set; }
        [JsonProperty("revision")] public long Revision { get; set; }
        [JsonProperty("value")] public JToken Value { get; set; }
        [JsonProperty("state")] public Dictionary<string, JToken> State { get; set; }
        [JsonProperty("error")] public string Error { get; set; }
        [JsonProperty("outcomes")] public List<UserStateOutcome> Outcomes { get; set; }
    }

    public sealed class UserStateOutcome
    {
        [JsonProperty("ok")] public bool Ok { get; set; }
        [JsonProperty("error")] public string Error { get; set; }
        [JsonProperty("value")] public JToken Value { get; set; }
    }

    public sealed class UserStateChangedEvent
    {
        [JsonProperty("roomSessionId")] public string RoomSessionId { get; set; }
        [JsonProperty("revision")] public long Revision { get; set; }
        [JsonProperty("changes")] public List<RoomStateChange> Changes { get; set; } = new List<RoomStateChange>();
    }

    public sealed class UserStateSnapshotEvent
    {
        [JsonProperty("revision")] public long Revision { get; set; }
        [JsonProperty("users")]
        public Dictionary<string, Dictionary<string, JToken>> Users { get; set; } =
            new Dictionary<string, Dictionary<string, JToken>>();
    }

    public sealed class UserStateRemovedEvent
    {
        [JsonProperty("roomSessionId")] public string RoomSessionId { get; set; }
        [JsonProperty("revision")] public long Revision { get; set; }
    }

    /// <summary>
    /// PacketParty's <c>JsonSettings.Default</c>, which the client deserializes every state message and reply
    /// with. It matters beyond names: <c>NullValueHandling.Ignore</c> leaves a JSON <c>null</c> as a C# null
    /// rather than a JValue, which decides whether a reply's <c>value</c> is passed on.
    /// </summary>
    public static class StateJson
    {
        public static readonly JsonSerializerSettings Default = new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy
                {
                    ProcessDictionaryKeys = false,
                    OverrideSpecifiedNames = false
                }
            },
            NullValueHandling = NullValueHandling.Ignore,
            DateFormatHandling = DateFormatHandling.IsoDateFormat,
            DateTimeZoneHandling = DateTimeZoneHandling.Utc,
            FloatParseHandling = FloatParseHandling.Double,
            MissingMemberHandling = MissingMemberHandling.Ignore
        };

        /// <summary><c>body.ToObject&lt;T&gt;(JsonSerializer.Create(JsonSettings.Default))</c>, as the client parses.</summary>
        public static T Parse<T>(JObject body) where T : class =>
            body?.ToObject<T>(JsonSerializer.Create(Default));
    }
}
