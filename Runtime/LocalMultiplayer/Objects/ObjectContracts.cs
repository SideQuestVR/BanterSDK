// <mirror source="Packages/com.sidequest.packetparty/Runtime/Contracts/SyncedObjectContracts.cs" sha256="2d50d0828a9d5c5618cbe6d8268eeaaed881fbfa5d394290bd5ae77a32cc446e" mode="verbatim" />
// The app.object.* DTOs, verbatim apart from the namespace: the relay speaks PacketParty's object protocol
// (scrombus packetparty-protocol/src/objects.ts, RELAY.md 6.1). ObjectLaneMessages and ObjectJson are additions:
// the relay's obj.payload lane (RELAY.md 6.4) and production's JsonSettings.Default for parsing.
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace BS.LocalMultiplayer.Objects
{
    /// <summary>
    /// `app.object.*` websocket message names (Phase 5 object authority).
    /// Requests carry a requestId; the server replies `app.object.result`,
    /// broadcasts lifecycle events (writer included), and pushes an
    /// `app.object.snapshot` after welcome.
    /// </summary>
    public static class ObjectMessages
    {
        public const string Create = "app.object.create";
        public const string Delete = "app.object.delete";
        public const string Transfer = "app.object.transfer";
        public const string Lock = "app.object.lock";
        public const string Unlock = "app.object.unlock";
        public const string Acquire = "app.object.acquire";
        public const string CommitState = "app.object.commitState";
        public const string Get = "app.object.get";
        public const string Result = "app.object.result";
        public const string Created = "app.object.created";
        public const string Deleted = "app.object.deleted";
        public const string OwnerChanged = "app.object.ownerChanged";
        public const string Updated = "app.object.updated";
        public const string Snapshot = "app.object.snapshot";
    }

    /// <summary>
    /// The relay-defined object lane that stands in for PacketParty's reliable object data channel
    /// (RELAY.md 6.4): {objectId, gen, seq, key, body (base64), reliable}, stamped with <c>from</c> on delivery.
    /// </summary>
    public static class ObjectLaneMessages
    {
        public const string Payload = "obj.payload";
    }

    /// <summary>Where an object came from; drives the default end-of-owner-session policy.</summary>
    public enum SyncedObjectOrigin
    {
        Scene,
        Spawned
    }

    /// <summary>Server action when the active simulation authority disconnects.</summary>
    public enum SyncedObjectDisconnectPolicy
    {
        Transfer,
        Destroy
    }

    /// <summary>
    /// A synced object's authoritative registry record. Ownership is keyed to
    /// roomSessionId — never clientId (shareable) or peer/connection ids
    /// (change on reconnect).
    /// </summary>
    public sealed class SyncedObjectRecord
    {
        [JsonProperty("objectId")]
        public string ObjectId { get; set; }

        [JsonProperty("typeKey")]
        public string TypeKey { get; set; }

        /// <summary>"scene" or "spawned".</summary>
        [JsonProperty("origin")]
        public string Origin { get; set; }

        /// <summary>Every visible object has exactly one simulation authority.</summary>
        [JsonProperty("ownerRoomSessionId")]
        public string OwnerRoomSessionId { get; set; }

        /// <summary>"transfer" or "destroy" when the active owner disconnects.</summary>
        [JsonProperty("ownerDisconnectPolicy")]
        public string OwnerDisconnectPolicy { get; set; }

        /// <summary>Monotonic epoch incremented every time ownership changes.</summary>
        [JsonProperty("ownershipGeneration")]
        public uint OwnershipGeneration { get; set; }

        [JsonProperty("locked")]
        public bool Locked { get; set; }

        [JsonProperty("components")]
        public Dictionary<string, string[]> Components { get; set; }

        [JsonProperty("metadata", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, JToken> Metadata { get; set; }

        [JsonProperty("state", NullValueHandling = NullValueHandling.Ignore)]
        public SyncedObjectState State { get; set; }
        [JsonProperty("unlockedTtlMs", NullValueHandling = NullValueHandling.Ignore)]
        public long? UnlockedTtlMs { get; set; }
        [JsonProperty("unlockedSinceEpochMs", NullValueHandling = NullValueHandling.Ignore)]
        public long? UnlockedSinceEpochMs { get; set; }

        [JsonProperty("revision")]
        public long Revision { get; set; }

        [JsonProperty("createdAtEpochMs")]
        public long CreatedAtEpochMs { get; set; }

        [JsonProperty("createdByRoomSessionId", NullValueHandling = NullValueHandling.Ignore)]
        public string CreatedByRoomSessionId { get; set; }
    }

    public sealed class SyncedObjectTransformState
    {
        [JsonProperty("position")]
        public float[] Position { get; set; }
        [JsonProperty("rotation", NullValueHandling = NullValueHandling.Ignore)]
        public float[] Rotation { get; set; }
        [JsonProperty("velocity", NullValueHandling = NullValueHandling.Ignore)]
        public float[] Velocity { get; set; }
    }

    public sealed class SyncedObjectState
    {
        [JsonProperty("transform", NullValueHandling = NullValueHandling.Ignore)]
        public SyncedObjectTransformState Transform { get; set; }
        [JsonProperty("components", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, string> Components { get; set; }
        [JsonProperty("ownershipGeneration")]
        public uint OwnershipGeneration { get; set; }
        [JsonProperty("updatedAtEpochMs")]
        public long UpdatedAtEpochMs { get; set; }
    }

    public sealed class ObjectUpdatedEvent
    {
        [JsonProperty("object")]
        public SyncedObjectRecord Object { get; set; }
        [JsonProperty("reason")]
        public string Reason { get; set; }
        [JsonProperty("revision")]
        public long Revision { get; set; }
    }

    public sealed class ObjectResult
    {
        [JsonProperty("metadata")]
        public JObject Metadata { get; set; }

        [JsonProperty("ok")]
        public bool Ok { get; set; }

        [JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)]
        public string Error { get; set; }

        /// <summary>
        /// Authoritative record after the op — also present on ownership
        /// denials (server correction) so callers can repair a stale mirror.
        /// </summary>
        [JsonProperty("object", NullValueHandling = NullValueHandling.Ignore)]
        public SyncedObjectRecord Object { get; set; }

        /// <summary>Full registry for a target-less get.</summary>
        [JsonProperty("objects", NullValueHandling = NullValueHandling.Ignore)]
        public List<SyncedObjectRecord> Objects { get; set; }

        [JsonProperty("revision")]
        public long Revision { get; set; }
    }

    public sealed class ObjectCreatedEvent
    {
        [JsonProperty("object")]
        public SyncedObjectRecord Object { get; set; }

        [JsonProperty("revision")]
        public long Revision { get; set; }
    }

    public sealed class ObjectDeletedEvent
    {
        [JsonProperty("objectId")]
        public string ObjectId { get; set; }

        /// <summary>owner_disconnected|expired|deleted|admin.</summary>
        [JsonProperty("reason")]
        public string Reason { get; set; }

        [JsonProperty("revision")]
        public long Revision { get; set; }
    }

    public sealed class ObjectOwnerChangedEvent
    {
        [JsonProperty("object")]
        public SyncedObjectRecord Object { get; set; }

        [JsonProperty("previousOwnerRoomSessionId")]
        public string PreviousOwnerRoomSessionId { get; set; }

        [JsonProperty("reason")]
        public string Reason { get; set; }

        [JsonProperty("revision")]
        public long Revision { get; set; }
    }

    public sealed class ObjectSnapshotEvent
    {
        [JsonProperty("revision")]
        public long Revision { get; set; }

        [JsonProperty("objects")]
        public List<SyncedObjectRecord> Objects { get; set; }
    }

    /// <summary>
    /// The serializer the object DTOs are read with: production's <c>PacketParty.Util.JsonSettings.Default</c>
    /// (camelCase, nulls ignored, unknown members ignored, floats as double). Main thread only.
    /// </summary>
    static class ObjectJson
    {
        public static readonly JsonSerializer Serializer = JsonSerializer.Create(new JsonSerializerSettings
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
        });
    }
}
