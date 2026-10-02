// <mirror source="Assets/Systems/Attachments/AttachmentNetworkBridge.cs" sha256="190f1ddfb95d591b7adbfd2e6fb171b144878bcb1bae6a67790f047d9162f22c" mode="verbatim" />
// The codecs are AttachmentNetworkBridge.cs:299-369 verbatim (private -> internal so the bridge port and the tests
// share them). PendingPilot is :65-70, and PilotBroadcastGate is the debounce of BroadcastLocalPilot (:46-57,
// :151-161) lifted out unchanged so it can be tested. EngineUserStateKey is new: it only feeds diagnostics.
using System.Globalization;
using BS;
using UnityEngine;

namespace BS.LocalMultiplayer.Attachments
{
    /// <summary>
    /// The user-state strings production uses to network attachments and seats: "attachment_&lt;BSObjectId.Id&gt;" =
    /// <c>v1|attachmentType|avatarAttachmentType|avatarAttachmentPoint|physicsAttachmentPoint|jointAvatar|px|py|pz|rx|ry|rz|rw</c>,
    /// and "pilot" = <c>seatObjId|hx|hy|hz|qx|qy|qz|qw</c> (the hip relative to the seat). "" means detached or
    /// standing. Invariant culture, '|'-delimited.
    /// </summary>
    internal static class AttachmentWireCodec
    {
        /// <summary>User-state key prefix of one attachment, followed by the object's BSObjectId.Id.</summary>
        public const string KeyPrefix = "attachment_";
        /// <summary>User-state key of the single seat a player rides (AvatarAttachTo).</summary>
        public const string PilotKey = "pilot";

        // ---------------------------------------------------------------- serialization
        // v1 | attachmentType | avatarAttachmentType | avatarAttachmentPoint | physicsAttachmentPoint |
        //      jointAvatar | px py pz | rx ry rz rw   (invariant culture; '|'-delimited)

        // pilot state: seatObjId | hip pos (x y z) | hip rot (x y z w), seat-relative, invariant culture.
        internal static string SerializePilot(string seatObjId, Vector3 p, Quaternion r)
        {
            var c = CultureInfo.InvariantCulture;
            return string.Join("|", seatObjId,
                p.x.ToString(c), p.y.ToString(c), p.z.ToString(c),
                r.x.ToString(c), r.y.ToString(c), r.z.ToString(c), r.w.ToString(c));
        }

        internal static bool TryParsePilot(string s, out PendingPilot pp)
        {
            pp = default;
            if (string.IsNullOrEmpty(s)) return false;
            string[] t = s.Split('|');
            if (string.IsNullOrEmpty(t[0])) return false;
            pp.SeatObjId = t[0];
            pp.HipLocalRot = Quaternion.identity;
            if (t.Length < 8) return true; // seat id only (offset not yet published) — glue at seat origin
            var c = CultureInfo.InvariantCulture;
            try
            {
                pp.HipLocalPos = new Vector3(float.Parse(t[1], c), float.Parse(t[2], c), float.Parse(t[3], c));
                pp.HipLocalRot = new Quaternion(float.Parse(t[4], c), float.Parse(t[5], c), float.Parse(t[6], c), float.Parse(t[7], c));
            }
            catch { /* keep seat, identity offset */ }
            return true;
        }

        internal static string Serialize(BSAttachment d)
        {
            var c = CultureInfo.InvariantCulture;
            Vector3 p = d.attachmentPosition;
            Quaternion r = d.attachmentRotation;
            return string.Join("|", "v1",
                ((int)d.attachmentType).ToString(c),
                ((int)d.avatarAttachmentType).ToString(c),
                ((int)d.avatarAttachmentPoint).ToString(c),
                ((int)d.physicsAttachmentPoint).ToString(c),
                d.jointAvatar ? "1" : "0",
                p.x.ToString(c), p.y.ToString(c), p.z.ToString(c),
                r.x.ToString(c), r.y.ToString(c), r.z.ToString(c), r.w.ToString(c));
        }

        internal static bool TryDeserialize(string s, out BSAttachment data)
        {
            data = null;
            if (string.IsNullOrEmpty(s)) return false;
            string[] t = s.Split('|');
            if (t.Length < 13 || t[0] != "v1") return false;
            var c = CultureInfo.InvariantCulture;
            try
            {
                data = new BSAttachment
                {
                    attachmentType = (AttachmentType)int.Parse(t[1], c),
                    avatarAttachmentType = (AvatarAttachmentType)int.Parse(t[2], c),
                    avatarAttachmentPoint = (AvatarBoneName)int.Parse(t[3], c),
                    physicsAttachmentPoint = (PhysicsAttachmentPoint)int.Parse(t[4], c),
                    jointAvatar = t[5] == "1",
                    attachmentPosition = new Vector3(float.Parse(t[6], c), float.Parse(t[7], c), float.Parse(t[8], c)),
                    attachmentRotation = new Quaternion(float.Parse(t[9], c), float.Parse(t[10], c), float.Parse(t[11], c), float.Parse(t[12], c)),
                    autoSync = true,
                };
                return true;
            }
            catch { return false; }
        }
    }

    /// <summary>A peer's seat as broadcast in "pilot": the seat's BSObjectId.Id and the hip pose relative to it.</summary>
    internal struct PendingPilot
    {
        public string SeatObjId;
        public Vector3 HipLocalPos;
        public Quaternion HipLocalRot;
    }

    /// <summary>
    /// When the local pilot broadcast publishes (AttachmentNetworkBridge.BroadcastLocalPilot): only on a meaningful
    /// change of the seat-relative hip (more than 1 cm or 1 degree), no faster than every 0.1 s since user state is
    /// a low-frequency lane, and always once right after sitting down. A still rider on a moving seat sends nothing:
    /// the offset is constant relative to the seat.
    /// </summary>
    internal sealed class PilotBroadcastGate
    {
        public const float PilotPosEpsilon = 0.01f;   // 1 cm
        public const float PilotRotEpsilon = 1f;       // 1 degree
        public const float PilotMinInterval = 0.1f;    // ≤10 Hz on the user-state lane

        Vector3 _lastSentPos;
        Quaternion _lastSentRot = Quaternion.identity;
        bool _hasSent;
        float _lastSendTime;

        public bool HasSent => _hasSent;

        /// <summary>A new seat (OnLocalPilotChanged): the next check publishes whatever the pose.</summary>
        public void ForceNextSend()
        {
            _hasSent = false;
        }

        /// <summary>Whether to publish this pose now. <paramref name="time"/> is Time.time, as in production.</summary>
        public bool ShouldSend(Vector3 localPos, Quaternion localRot, float time)
        {
            bool changed = !_hasSent
                || (localPos - _lastSentPos).sqrMagnitude > PilotPosEpsilon * PilotPosEpsilon
                || Quaternion.Angle(localRot, _lastSentRot) > PilotRotEpsilon;
            if (!changed) return false;
            if (_hasSent && time - _lastSendTime < PilotMinInterval) return false;
            return true;
        }

        public void MarkSent(Vector3 localPos, Quaternion localRot, float time)
        {
            _lastSentPos = localPos;
            _lastSentRot = localRot;
            _lastSendTime = time;
            _hasSent = true;
        }
    }

    /// <summary>
    /// The room's path grammar (RELAY.md 4; scrombus packetparty-server state/keys.ts validatePath, which user state
    /// shares): dotted segments of [A-Za-z0-9_-:@], 1-64 characters each, at most 64 segments and 256 characters.
    /// Only used to warn before an attachment key goes out: production broadcasts it anyway and the room refuses
    /// the whole engine batch it rides in.
    /// </summary>
    internal static class EngineUserStateKey
    {
        public const int MaxPathLength = 256;
        public const int MaxPathSegments = 64;
        public const int MaxSegmentLength = 64;

        public enum Shape
        {
            /// <summary>One valid segment: a key of its own.</summary>
            Valid,
            /// <summary>Valid, but the dots make it a nested path that can collide with another key.</summary>
            Nested,
            /// <summary>The room refuses it (invalid_path).</summary>
            Invalid,
        }

        public static Shape Classify(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Length > MaxPathLength)
            {
                return Shape.Invalid;
            }
            var segments = 1;
            var segmentLength = 0;
            for (var i = 0; i < path.Length; i++)
            {
                var ch = path[i];
                if (ch == '.')
                {
                    if (segmentLength == 0)
                    {
                        return Shape.Invalid;
                    }
                    segments++;
                    segmentLength = 0;
                    continue;
                }
                if (!IsSegmentChar(ch) || ++segmentLength > MaxSegmentLength)
                {
                    return Shape.Invalid;
                }
            }
            if (segmentLength == 0 || segments > MaxPathSegments)
            {
                return Shape.Invalid;
            }
            return segments == 1 ? Shape.Valid : Shape.Nested;
        }

        static bool IsSegmentChar(char ch) =>
            (ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9')
            || ch == '_' || ch == '-' || ch == ':' || ch == '@';
    }
}
