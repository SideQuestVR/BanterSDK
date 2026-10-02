// <mirror source="Assets/Systems/Networking/OneShot/OneShotService.cs" sha256="ecd52830d31fee69e74199f8d3ed3e25305cf4fdcdd75abe7d1cba53ebb60435" mode="port" />
// OneShotService (OneShotService.cs:77-206): IsOpen, Send and OnData, with PacketParty's "greenfield-oneshot-v1" data
// channel replaced by the relay's "oneshot" message (RELAY.md 6.4). The MonoBehaviour is a plain class OneShotModule
// owns; there is no channel to open, so the lane is open exactly while the session is joined.
using System;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace BS.LocalMultiplayer
{
    /// <summary>
    /// The room's fire-and-forget broadcast lane: a message reaches everyone currently in the room, never the sender.
    /// Nothing is stored (late joiners see nothing) and nothing is acknowledged. Frames obey production's
    /// <see cref="OneShotCodec"/> rules on both ends: at most 4096 UTF-8 bytes of <c>topic\n data</c>, and topics of
    /// letters, digits, <c>-</c>, <c>_</c> and <c>.</c> up to 32 characters.
    /// </summary>
    public sealed class OneShotLane
    {
        /// <summary>The relay message that carries the lane (RELAY.md 6.4).</summary>
        public const string MessageType = "oneshot";

        private readonly ILocalSession _client;
        private readonly Action<JObject> _onData;
        private bool _attached;

        /// <summary>Raised on the main thread for every one-shot received from a peer.</summary>
        public event Action<OneShotMessage> Received;

        public OneShotLane(ILocalSession client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _onData = OnData;
        }

        /// <summary>True while joined: production's "connected with the lane's outbound channel open".</summary>
        public bool IsOpen => _client.State == SessionState.Joined;

        public void Attach()
        {
            if (_attached) return;
            _attached = true;
            _client.On(MessageType, _onData);
        }

        public void Detach()
        {
            if (!_attached) return;
            _attached = false;
            _client.Off(MessageType, _onData);
            Received = null;
        }

        /// <summary>Broadcast <paramref name="data"/> under <paramref name="topic"/> to everyone in the room. False when offline or the frame is invalid.</summary>
        public bool Send(string topic, string data)
        {
            if (!IsOpen) return false;
            if (!OneShotCodec.TryEncode(topic, data, out _)) return false;
            try
            {
                // The relay carries the frame's two halves as fields; the codec above already held the whole frame
                // to production's limits.
                return _client.Send(MessageType, new JObject
                {
                    ["topic"] = topic,
                    ["data"] = data ?? string.Empty
                });
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LocalMP][OneShot] Send failed: {e.Message}");
                return false;
            }
        }

        private void OnData(JObject message)
        {
            if (!_attached || message == null) return;
            var topicField = AsString(message["topic"]);
            if (topicField == null) return;
            var dataField = AsString(message["data"]) ?? string.Empty;

            // Rebuild production's frame and run the verbatim decoder over it, so a relayed one-shot passes exactly
            // the rules one off the data channel does.
            if (!OneShotCodec.TryDecode(Encoding.UTF8.GetBytes(topicField + "\n" + dataField), out var topic, out var data))
            {
                return;
            }

            // The sender identity is stamped by the relay, as PacketParty's server stamps it on the data producer.
            var fromRoomSessionId = AsString(message["fromRoomSessionId"]);
            // Production never consumes its own data producer (PacketPartyClient.cs:4164-4170). The relay and the
            // session already drop our own echo; the lane keeps the rule too.
            if (!string.IsNullOrEmpty(fromRoomSessionId)
                && string.Equals(fromRoomSessionId, _client.OwnRoomSessionId, StringComparison.Ordinal))
            {
                return;
            }

            var handler = Received;
            if (handler == null) return;
            try { handler(new OneShotMessage(fromRoomSessionId, AsString(message["fromUserId"]), topic, data)); }
            catch (Exception e) { Debug.LogException(e); }
        }

        private static string AsString(JToken token) =>
            token != null && token.Type == JTokenType.String ? (string)token : null;
    }
}
