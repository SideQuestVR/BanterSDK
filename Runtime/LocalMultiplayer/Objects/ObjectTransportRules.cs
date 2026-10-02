// <mirror source="Packages/com.sidequest.packetparty/Runtime/Data/ObjectTransport.cs" sha256="c54de870e9cefe27b0502d40c29734d0a140a3f72f8344268e921134261736f1" mode="port" />
// <mirror source="Packages/com.sidequest.packetparty/Runtime/Transforms/TransformFrameCodec.cs" sha256="fb4be0cbab30fd8881dee2f8ebf6edddbcbac266f8f5b933968d6c99701a9393" mode="port" />
// The receive guards of the object lanes: ObjectTransport.cs:16-44 (payload kinds, the half-window u16 compare and
// ShouldAcceptEnvelope) and TransformFrameCodec.cs:210-214 (IsNewerSequence). The binary envelope itself is gone:
// the relay carries the same fields as JSON (RELAY.md 6.2, 6.4), so the guard takes them as arguments.
using System;

namespace BS.LocalMultiplayer.Objects
{
    /// <summary>
    /// Which object frames and payloads a receiver accepts, exactly as the Greenfield client decides: only from the
    /// record's current owner, at the record's ownership generation, with a newer sequence. The relay applies the same
    /// owner and generation filter (RELAY.md 6.2); receivers check again, as production's do.
    /// </summary>
    public static class ObjectTransportRules
    {
        public const byte KindOpaqueBinary = 1;
        public const byte KindOpaqueJson = 2;
        public const byte KindBatch = 3;

        /// <summary>Half-window u16 compare: is <paramref name="candidate"/> newer than <paramref name="last"/>?</summary>
        public static bool IsSeqNewer(int candidate, int last)
        {
            int delta = (candidate - last) & 0xFFFF;
            return delta != 0 && delta < 0x8000;
        }

        /// <summary>
        /// ObjectTransport.ShouldAcceptEnvelope with the envelope's fields passed in: the sender must be the
        /// record's owner, the envelope must carry the record's ownership generation, and its sequence must be
        /// newer than the last one accepted on this lane (none yet = accept).
        /// </summary>
        public static bool ShouldAcceptEnvelope(
            string ownerRoomSessionId,
            uint currentOwnershipGeneration,
            uint envelopeOwnershipGeneration,
            int envelopeSeq,
            string senderRoomSessionId,
            int? lastSeq)
        {
            if (!string.Equals(ownerRoomSessionId, senderRoomSessionId, StringComparison.Ordinal)) return false;
            if (currentOwnershipGeneration != envelopeOwnershipGeneration) return false;
            return !lastSeq.HasValue || IsSeqNewer(envelopeSeq, lastSeq.Value);
        }

        /// <summary>TransformFrameCodec.IsNewerSequence: u16 sequence order with wraparound.</summary>
        public static bool IsNewerSequence(ushort candidate, ushort previous)
        {
            ushort difference = unchecked((ushort)(candidate - previous));
            return difference != 0 && difference < 0x8000;
        }

        /// <summary>
        /// The sender and generation half of the transform-frame guard (the sequence half is
        /// <see cref="TransformPlaybackBuffer.Push(SynchronizedTransformFrame, double)"/>). Production binds a frame to
        /// its publisher's descriptor, which the server only grants to the current owner; here the frame names the
        /// generation it was published under.
        /// </summary>
        public static bool ShouldAcceptFrame(
            string ownerRoomSessionId,
            uint currentOwnershipGeneration,
            string senderRoomSessionId,
            uint frameOwnershipGeneration)
        {
            if (string.IsNullOrEmpty(senderRoomSessionId)) return false;
            if (!string.Equals(ownerRoomSessionId, senderRoomSessionId, StringComparison.Ordinal)) return false;
            return currentOwnershipGeneration == frameOwnershipGeneration;
        }
    }
}
