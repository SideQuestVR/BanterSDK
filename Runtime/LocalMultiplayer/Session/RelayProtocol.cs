using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace BS.LocalMultiplayer
{
    /// <summary>
    /// The wire constants of the local relay protocol, v1 (C:\workspace\SideQuest.Ora\Protocol\RELAY.md). Message
    /// names are PacketParty's wherever the relay stands in for the PacketParty server, so the ported Greenfield
    /// bridges keep their message handling; the transform lanes, payloads, one-shots and the manifest are
    /// relay-defined.
    /// </summary>
    internal static class RelayProtocol
    {
        public const int ProtocolVersion = 1;
        public const string SubProtocol = "sq-relay.v1";
        public const string WsPath = "/__sq-relay";
        public const string HubInfoPath = "/__sq-relay/hub";
        /// <summary>OraManager.staticPort's default: the page and the relay share the web server's port.</summary>
        public const int DefaultPort = 42068;

        // Handshake and presence (RELAY.md 2, 3).
        public const string Hello = "hello";
        public const string Welcome = "welcome";
        public const string RoomReady = "room.ready";
        public const string PeerJoined = "peerJoined";
        public const string PeerLeft = "peerLeft";

        // Errors (RELAY.md 9). A relay.error or signal.error carrying a requestId fails that request.
        public const string RelayError = "relay.error";
        public const string SignalError = "signal.error";

        // Relay-defined lanes (RELAY.md 6).
        public const string ParticipantFrame = "tf.participant";
        public const string ObjectFrames = "tf.objects";
        public const string ObjectPayload = "obj.payload";
        public const string OneShot = "oneshot";
        public const string Manifest = "manifest";

        // PacketParty's room clock (RELAY.md 8) and the owner's clear (RELAY.md 4).
        public const string RoomClockPing = "signal.roomClockPing";
        public const string RoomClockPong = "signal.roomClockPong";
        public const string AdminClearRoomState = "admin.clearRoomState";
        public const string AdminResult = "admin.result";

        // PacketParty's user state (RELAY.md 5): the engine channel writes these, the session surfaces them.
        public const string UserStateBatch = "app.userState.batch";
        public const string UserStateResult = "app.userState.result";
        public const string UserStateSnapshot = "app.userState.snapshot";
        public const string UserStateChanged = "app.userState.changed";
        public const string UserStateRemoved = "app.userState.removed";

        /// <summary>Content-authored user props live under this root (UserStateService.PropsRoot); every other root key
        /// belongs to the engine (attachment_&lt;Id&gt;, pilot, ...).</summary>
        public const string PropsRoot = "props";

        // Close codes (RELAY.md 9).
        public const int CloseNormal = 1000;
        public const int CloseHubShuttingDown = 1001;
        public const int CloseNoStatus = 1005;
        public const int CloseAbnormal = 1006;
        public const int CloseProtocolMismatch = 4000;
        public const int CloseProjectMismatch = 4001;
        public const int CloseHelloTimeout = 4002;
        public const int CloseInvalidHello = 4003;
        public const int CloseRoomFull = 4004;
        public const int CloseBackpressure = 4008;

        // Request failures the host raises itself (Contracts.cs, LocalRelayException).
        public const string ErrorNotConnected = "not_connected";
        public const string ErrorTimeout = "timeout";
        public const string ErrorClosed = "closed";

        public const string RoleOwner = "owner";
        public const string RoleModerator = "moderator";
        public const string RoleMember = "member";

        public static string RoleWire(PlayerRole role)
        {
            switch (role)
            {
                case PlayerRole.Owner: return RoleOwner;
                case PlayerRole.Moderator: return RoleModerator;
                default: return RoleMember;
            }
        }

        public static PlayerRole ParseRole(string role)
        {
            if (string.Equals(role, RoleOwner, StringComparison.Ordinal)) return PlayerRole.Owner;
            if (string.Equals(role, RoleModerator, StringComparison.Ordinal)) return PlayerRole.Moderator;
            return PlayerRole.Member;
        }

        /// <summary>True for a user-state path the engine owns: anything outside the content props' root.</summary>
        public static bool IsEnginePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (string.Equals(path, PropsRoot, StringComparison.Ordinal)) return false;
            return !path.StartsWith(PropsRoot + ".", StringComparison.Ordinal);
        }

        /// <summary>
        /// The first message on every socket (RELAY.md 2). <paramref name="resumeSessionId"/> and
        /// <paramref name="resumeToken"/> reattach a dropped session within its grace, as PacketPartyClient's
        /// socket-only reconnect does; leave them null for a fresh join.
        /// </summary>
        public static JObject BuildHello(LocalIdentity identity, string roomId, string sdkVersion, ObjectManifest manifest,
            string resumeSessionId = null, string resumeToken = null)
        {
            var hello = new JObject
            {
                ["type"] = Hello,
                ["protocolVersion"] = ProtocolVersion,
                ["projectRoot"] = identity.MainProjectRoot ?? "",
                ["roomId"] = roomId ?? "",
                ["clientId"] = identity.ClientId ?? "",
                // The relay caps these (displayName 64, slot 32) and refuses the hello otherwise, which would
                // retry forever: trim instead.
                ["displayName"] = Truncate(identity.DisplayName, 64),
                ["slot"] = Truncate(identity.Slot, 32),
                ["role"] = RoleWire(identity.Role),
                ["editorPid"] = identity.MainProcessId,
                ["sdkVersion"] = sdkVersion ?? "",
                ["manifest"] = ManifestJson(manifest),
            };
            if (!string.IsNullOrEmpty(resumeSessionId) && !string.IsNullOrEmpty(resumeToken))
            {
                hello["resume"] = new JObject
                {
                    ["sessionId"] = resumeSessionId,
                    ["resumeToken"] = resumeToken,
                };
            }
            return hello;
        }

        /// <summary>The manifest's wire shape, {ids, runtimeIds} (RELAY.md 6.5).</summary>
        public static JObject ManifestJson(ObjectManifest manifest)
        {
            var ids = new JArray();
            var runtimeIds = new JArray();
            if (manifest != null)
            {
                if (manifest.Ids != null)
                {
                    foreach (var id in manifest.Ids)
                    {
                        if (id != null) ids.Add(id);
                    }
                }
                if (manifest.RuntimeIds != null)
                {
                    foreach (var id in manifest.RuntimeIds)
                    {
                        if (id != null) runtimeIds.Add(id);
                    }
                }
            }
            return new JObject { ["ids"] = ids, ["runtimeIds"] = runtimeIds };
        }

        /// <summary>Reads a manifest from presenceMetadata.manifest or a relayed manifest message.</summary>
        public static ObjectManifest ParseManifest(JObject json)
        {
            var manifest = new ObjectManifest();
            if (json == null) return manifest;
            ReadStrings(json["ids"] as JArray, manifest.Ids);
            ReadStrings(json["runtimeIds"] as JArray, manifest.RuntimeIds);
            return manifest;
        }

        public static bool SameManifest(ObjectManifest a, ObjectManifest b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            return SameList(a.Ids, b.Ids) && SameList(a.RuntimeIds, b.RuntimeIds);
        }

        /// <summary>A string field of a relay message, or "" (the relay never sends nulls we care about).</summary>
        public static string Str(JObject message, string name)
        {
            if (message == null) return "";
            var token = message[name];
            if (token == null || token.Type == JTokenType.Null) return "";
            return token.Type == JTokenType.String ? (string)token : token.ToString();
        }

        public static long Long(JObject message, string name, long fallback = 0)
        {
            var token = message?[name];
            if (token == null) return fallback;
            switch (token.Type)
            {
                case JTokenType.Integer:
                    return token.Value<long>();
                case JTokenType.Float:
                    return (long)token.Value<double>();
                case JTokenType.String:
                    return long.TryParse((string)token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;
                default:
                    return fallback;
            }
        }

        public static bool Bool(JObject message, string name, bool fallback = false)
        {
            var token = message?[name];
            if (token == null || token.Type != JTokenType.Boolean) return fallback;
            return token.Value<bool>();
        }

        static void ReadStrings(JArray array, List<string> into)
        {
            if (array == null) return;
            foreach (var item in array)
            {
                if (item != null && item.Type == JTokenType.String) into.Add((string)item);
            }
        }

        static bool SameList(List<string> a, List<string> b)
        {
            var countA = a != null ? a.Count : 0;
            var countB = b != null ? b.Count : 0;
            if (countA != countB) return false;
            for (var i = 0; i < countA; i++)
            {
                if (!string.Equals(a[i], b[i], StringComparison.Ordinal)) return false;
            }
            return true;
        }

        static string Truncate(string value, int max)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Length <= max ? value : value.Substring(0, max);
        }
    }
}
