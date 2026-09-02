using System;

namespace BS
{
    /// <summary>
    /// A shared time base for deterministic, network-free synchronized motion/animation. Every client reads
    /// the same value, so anything driven by it (<see cref="SyncLoop"/>, a spline mover, a Playable's time)
    /// lands in the same phase on all clients WITHOUT syncing any transforms.
    ///
    /// Source is the UTC wall clock — all devices agree when their clocks are NTP-synced (the usual case; a
    /// badly-set device clock can be off by seconds). PacketParty exposes no shared server clock today; if one
    /// ever does, set <see cref="ServerOffsetSeconds"/> so <see cref="NowSeconds"/> tracks it and every
    /// consumer benefits without changes.
    /// </summary>
    public static class SyncedClock
    {
        /// <summary>
        /// Added to the UTC time. Leave at 0 for the plain-UTC fallback; set it to (serverNow - localNow)
        /// once a networked/server clock is available to tighten agreement across clients.
        /// </summary>
        public static double ServerOffsetSeconds = 0.0;

        /// <summary>Shared time in seconds. Identical on every client (modulo device clock skew / server offset).</summary>
        public static double NowSeconds =>
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0 + ServerOffsetSeconds;

        /// <summary>
        /// Shared phase in [0,1) for a loop of length <paramref name="periodSeconds"/>. Drop straight into a
        /// looping mover/animation, e.g. <c>spline.Evaluate(SyncedClock.Phase01(loopSeconds), ...)</c>.
        /// </summary>
        public static float Phase01(double periodSeconds)
        {
            if (periodSeconds <= 0.0) return 0f;
            double t = NowSeconds / periodSeconds;
            return (float)(t - Math.Floor(t));
        }
    }
}
