// <mirror source="Packages/com.sidequest.packetparty/Runtime/Data/ObjectTransport.cs" sha256="c54de870e9cefe27b0502d40c29734d0a140a3f72f8344268e921134261736f1" mode="port" />
// <mirror source="Packages/com.sidequest.packetparty/Runtime/Transforms/TransformFrameCodec.cs" sha256="fb4be0cbab30fd8881dee2f8ebf6edddbcbac266f8f5b933968d6c99701a9393" mode="port" />
// The receive guards of the object lanes: ObjectTransport.cs:16-44 (payload kinds, the half-window u16 compare and
// ShouldAcceptEnvelope) and TransformFrameCodec.cs:210-214 (IsNewerSequence). The binary envelope itself is gone:
// the relay carries the same fields as JSON (RELAY.md 6.2, 6.4), so the guard takes them as arguments.
using System;
using System.Threading;
using System.Threading.Tasks;

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

        /// <summary>
        /// Seconds before declare retry <paramref name="attempt"/> (from 0): 0.25 doubling to 10, as the client's
        /// BackoffPolicy does, spread by half either way by <paramref name="random01"/> so a refused burst doesn't come
        /// back as one.
        /// </summary>
        public static double DeclareRetrySeconds(int attempt, double random01)
        {
            double seconds = Math.Min(10d, 0.25d * Math.Pow(2d, Math.Min(attempt, 16)));
            return seconds * (1d + (random01 * 2d - 1d) * 0.5d);
        }

        /// <summary>
        /// Failures that say nothing about the object: the declare can be sent again. Refusals stay final, and a session
        /// that ended declares again on the next join.
        /// </summary>
        public static bool IsRetryableDeclareFailure(string code) =>
            code == "rate_limited" || code == "internal_error" || code == "timeout";

        /// <summary>
        /// Would this take push out whoever has the object now? <paramref name="aimedAt"/> is the owner the take was made
        /// against, which a script may mean to take from; null for a take that had to wait from the start. A late joiner's
        /// copy hasn't had the real pose yet, so taking over from anyone else publishes the authored pose and the object
        /// snaps back for everyone. As the client's SyncedObjectClaims.
        /// </summary>
        public static bool TakeWouldDisplace(bool remoteOwned, string owner, string aimedAt) =>
            remoteOwned && !string.Equals(owner, aimedAt, StringComparison.Ordinal);

        /// <summary>
        /// Runs <paramref name="attempt"/>, sending it again while the relay answers rate_limited: claims share the
        /// declare bucket, so they meet the same limit during a big world's load. True once it succeeds, false when
        /// <paramref name="mayRetry"/> says stop. Past <paramref name="budget"/> of waiting the last failure is thrown,
        /// as any other failure is. <paramref name="random01"/> spreads the delays (<see cref="DeclareRetrySeconds"/>).
        /// </summary>
        public static async Task<bool> RetryWhileRateLimitedAsync(Func<Task> attempt, Func<bool> mayRetry,
            Func<double> random01, TimeSpan budget, CancellationToken cancellation = default)
        {
            var waited = TimeSpan.Zero;
            for (int retry = 0; ; retry++)
            {
                try
                {
                    await attempt();
                    return true;
                }
                catch (LocalRelayException e) when (e.Code == "rate_limited")
                {
                    var delay = TimeSpan.FromSeconds(DeclareRetrySeconds(retry, random01()));
                    waited += delay;
                    if (waited > budget) throw;
                    await Task.Delay(delay, cancellation);
                    if (!mayRetry()) return false;
                }
            }
        }

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
