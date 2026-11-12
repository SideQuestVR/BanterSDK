using System;
using NUnit.Framework;

namespace BS.LocalMultiplayer.Tests
{
    /// <summary>
    /// Every Multiplayer Play Mode player stamps frames with MachineClock and plays remote frames back against its own
    /// MachineClock, so all editor processes must agree on it. Unity's Mono Stopwatch counts from each process's first
    /// read, so the clock has to be anchored to something every process shares: the system clock.
    /// </summary>
    public class MachineClockTests
    {
        static double UnixNowMs() => (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;

        [Test]
        public void NowMs_IsUnixEpochMilliseconds_SoEveryProcessSharesIt()
        {
            double before = UnixNowMs();
            double now = MachineClock.NowMs;
            double after = UnixNowMs();
            // The system clock can tick as coarsely as 15.6 ms; anything per-process would be off by minutes or hours.
            Assert.That(now, Is.InRange(before - 50, after + 50));
        }

        [Test]
        public void NowMs_NeverRunsBackwards()
        {
            double previous = MachineClock.NowMs;
            for (int i = 0; i < 10000; i++)
            {
                double now = MachineClock.NowMs;
                Assert.GreaterOrEqual(now, previous);
                previous = now;
            }
        }

        [Test]
        public void NowMs_HasSubMillisecondResolution()
        {
            // Frames 33 ms apart interpolate smoothly only with a fine clock; a 15.6 ms system tick would judder.
            double start = MachineClock.NowMs;
            double now = start;
            int spins = 0;
            while (now == start && spins++ < 1000000) now = MachineClock.NowMs;
            Assert.That(now - start, Is.GreaterThan(0).And.LessThan(1.0));
        }
    }
}
