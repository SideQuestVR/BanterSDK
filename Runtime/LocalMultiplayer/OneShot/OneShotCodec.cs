// <mirror source="Assets/Systems/Networking/OneShot/OneShotService.cs" sha256="ecd52830d31fee69e74199f8d3ed3e25305cf4fdcdd75abe7d1cba53ebb60435" mode="verbatim" />
// Verbatim copy of OneShotMessage and OneShotCodec (OneShotService.cs:10-75); only the namespace differs. The
// lane itself (OneShotService) is ported in OneShotModule.cs.
using System;
using System.Text;

namespace BS.LocalMultiplayer
{
    /// <summary>A room-wide fire-and-forget message: who sent it, a topic and a payload.</summary>
    public readonly struct OneShotMessage
    {
        public OneShotMessage(string roomSessionId, string userId, string topic, string data)
        {
            RoomSessionId = roomSessionId;
            UserId = userId;
            Topic = topic;
            Data = data;
        }

        /// <summary>The sender's per-join identity (the key remote players and user state are looked up by).</summary>
        public string RoomSessionId { get; }
        /// <summary>The sender's stable PacketParty user id.</summary>
        public string UserId { get; }
        public string Topic { get; }
        public string Data { get; }
    }

    /// <summary>
    /// Frame format of the one-shot lane: <c>topic\n data</c> in UTF-8. Topics are short
    /// identifiers; the payload is opaque and may itself contain newlines (only the first one
    /// splits). Pure so it is unit-tested.
    /// </summary>
    public static class OneShotCodec
    {
        public const int MaxTopicLength = 32;
        public const int MaxPayloadBytes = 4096;
        private const char Separator = '\n';

        public static bool IsValidTopic(string topic)
        {
            if (string.IsNullOrEmpty(topic) || topic.Length > MaxTopicLength) return false;
            foreach (var c in topic)
                if (!(char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.')) return false;
            return true;
        }

        public static bool TryEncode(string topic, string data, out byte[] bytes)
        {
            bytes = null;
            if (!IsValidTopic(topic)) return false;
            data ??= string.Empty;
            var encoded = Encoding.UTF8.GetBytes(topic + Separator + data);
            if (encoded.Length > MaxPayloadBytes) return false;
            bytes = encoded;
            return true;
        }

        public static bool TryDecode(byte[] bytes, out string topic, out string data)
        {
            topic = null;
            data = null;
            if (bytes == null || bytes.Length == 0 || bytes.Length > MaxPayloadBytes) return false;
            string text;
            try { text = Encoding.UTF8.GetString(bytes); }
            catch (Exception) { return false; }
            var split = text.IndexOf(Separator);
            if (split <= 0) return false;
            var candidate = text.Substring(0, split);
            if (!IsValidTopic(candidate)) return false;
            topic = candidate;
            data = text.Substring(split + 1);
            return true;
        }
    }
}
