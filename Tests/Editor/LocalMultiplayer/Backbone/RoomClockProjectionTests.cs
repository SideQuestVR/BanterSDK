// <mirror source="Packages/com.sidequest.packetparty/Tests/Editor/PacketPartyRoomClockTests.cs" sha256="1bcfedec74835dcb7326c0780151c3de4e7cb8385c1d6a795a9a85c25d8ff0f4" mode="port" />
using NUnit.Framework;

namespace BS.LocalMultiplayer.Tests
{
    /// <summary>
    /// PacketParty's own room clock tests, run against the local port (RoomClockProjection). The client
    /// facade test is left out: there is no PacketPartyClient here.
    /// </summary>
    public sealed class RoomClockProjectionTests
    {
        private const long FirstOrigin = 1_788_290_000_000L;

        private sealed class FakeMonotonicTimeSource
        {
            public double NowMilliseconds { get; set; }
        }

        [Test]
        public void BeforeSeed_IsUnavailableAndTryGetReturnsFalse()
        {
            var clock = CreateClock(out _);

            Assert.IsFalse(clock.IsSynchronized);
            Assert.AreEqual(0d, clock.ElapsedMilliseconds);
            Assert.IsFalse(clock.TryGetElapsedMilliseconds(out double elapsed));
            Assert.AreEqual(0d, elapsed);
            Assert.IsTrue(double.IsNaN(clock.EstimatedRoundTripMilliseconds));
            Assert.IsNull(clock.CreatedAtEpochMilliseconds);
        }

        [Test]
        public void Seed_AdvancesWithMonotonicTimeAndIgnoresFrameTimeScale()
        {
            var clock = CreateClock(out var time);
            clock.ApplySeed("room-a", FirstOrigin, 12_500d, time.NowMilliseconds, smoothCorrection: false);

            time.NowMilliseconds += 250d;

            Assert.IsTrue(clock.IsSynchronized);
            Assert.AreEqual(12_750d, clock.ElapsedMilliseconds, 0.0001d);
            Assert.AreEqual(12.75d, clock.ElapsedSeconds, 0.0001d);
            Assert.AreEqual(FirstOrigin, clock.CreatedAtEpochMilliseconds);
        }

        [Test]
        public void SameOriginBackwardCorrection_SlowsWithoutMovingBackward()
        {
            var clock = CreateClock(out var time);
            clock.ApplySeed("room-a", FirstOrigin, 5_000d, time.NowMilliseconds, smoothCorrection: false);
            time.NowMilliseconds += 100d;
            double beforeCorrection = clock.ElapsedMilliseconds;

            clock.ApplyMeasurement(
                "room-a",
                FirstOrigin,
                roomTimeMilliseconds: 5_000d,
                measuredAtMonotonicMilliseconds: time.NowMilliseconds,
                estimatedRoundTripMilliseconds: 20d,
                smoothCorrection: true);
            Assert.AreEqual(beforeCorrection, clock.ElapsedMilliseconds, 0.0001d);

            time.NowMilliseconds += 100d;
            double afterCorrection = clock.ElapsedMilliseconds;
            Assert.GreaterOrEqual(afterCorrection, beforeCorrection);
            Assert.AreEqual(beforeCorrection + 90d, afterCorrection, 0.0001d);
            Assert.AreEqual(20d, clock.EstimatedRoundTripMilliseconds, 0.0001d);
        }

        [Test]
        public void SameOriginForwardCorrection_CatchesUpWithoutJumping()
        {
            var clock = CreateClock(out var time);
            clock.ApplySeed("room-a", FirstOrigin, 1_000d, time.NowMilliseconds, smoothCorrection: false);

            clock.ApplyMeasurement(
                "room-a",
                FirstOrigin,
                roomTimeMilliseconds: 1_100d,
                measuredAtMonotonicMilliseconds: time.NowMilliseconds,
                estimatedRoundTripMilliseconds: 8d,
                smoothCorrection: true);
            Assert.AreEqual(1_000d, clock.ElapsedMilliseconds, 0.0001d);

            time.NowMilliseconds += 100d;
            Assert.AreEqual(1_110d, clock.ElapsedMilliseconds, 0.0001d);
        }

        [Test]
        public void SameOriginReconnect_KeepsAdvancingAcrossTheOfflineGap()
        {
            var clock = CreateClock(out var time);
            clock.ApplySeed("room-a", FirstOrigin, 1_000d, time.NowMilliseconds, smoothCorrection: false);

            // A partial teardown does not reset the facade. No Unity frames need to run
            // for the Stopwatch-backed projection to include the reconnect interval.
            time.NowMilliseconds += 5_000d;
            Assert.AreEqual(6_000d, clock.ElapsedMilliseconds, 0.0001d);

            clock.ApplySeed(
                "room-a",
                FirstOrigin,
                roomTimeMilliseconds: 6_010d,
                receivedAtMonotonicMilliseconds: time.NowMilliseconds,
                smoothCorrection: true);
            Assert.AreEqual(6_000d, clock.ElapsedMilliseconds, 0.0001d);
        }

        [Test]
        public void NewRoomOrigin_ResetsTimelineIntentionally()
        {
            var clock = CreateClock(out var time);
            clock.ApplySeed("room-a", FirstOrigin, 30_000d, time.NowMilliseconds, smoothCorrection: false);
            Assert.IsTrue(clock.ApplyMeasurement(
                "room-a",
                FirstOrigin,
                30_000d,
                time.NowMilliseconds,
                estimatedRoundTripMilliseconds: 15d,
                smoothCorrection: false));
            time.NowMilliseconds += 500d;
            Assert.AreEqual(30_500d, clock.ElapsedMilliseconds, 0.0001d);

            long nextOrigin = FirstOrigin + 30_500L;
            clock.ApplySeed("room-a", nextOrigin, 25d, time.NowMilliseconds, smoothCorrection: true);

            Assert.AreEqual(25d, clock.ElapsedMilliseconds, 0.0001d);
            Assert.AreEqual(nextOrigin, clock.CreatedAtEpochMilliseconds);
            Assert.IsTrue(double.IsNaN(clock.EstimatedRoundTripMilliseconds));
        }

        [Test]
        public void StaleMeasurement_CannotReplaceOrResurrectTheSeededIncarnation()
        {
            var clock = CreateClock(out var time);
            clock.ApplySeed("room-new", FirstOrigin + 1_000L, 25d, time.NowMilliseconds, smoothCorrection: false);

            Assert.IsFalse(clock.ApplyMeasurement(
                "room-old",
                FirstOrigin,
                roomTimeMilliseconds: 30_000d,
                measuredAtMonotonicMilliseconds: time.NowMilliseconds,
                estimatedRoundTripMilliseconds: 10d,
                smoothCorrection: true));
            Assert.AreEqual(25d, clock.ElapsedMilliseconds, 0.0001d);
            Assert.AreEqual(FirstOrigin + 1_000L, clock.CreatedAtEpochMilliseconds);

            Assert.IsFalse(clock.ApplyMeasurement(
                "room-old",
                FirstOrigin + 1_000L,
                roomTimeMilliseconds: 30_000d,
                measuredAtMonotonicMilliseconds: time.NowMilliseconds,
                estimatedRoundTripMilliseconds: 10d,
                smoothCorrection: true));
            Assert.AreEqual(25d, clock.ElapsedMilliseconds, 0.0001d);

            clock.Reset();
            Assert.IsFalse(clock.ApplyMeasurement(
                "room-new",
                FirstOrigin + 1_000L,
                roomTimeMilliseconds: 50d,
                measuredAtMonotonicMilliseconds: time.NowMilliseconds,
                estimatedRoundTripMilliseconds: 10d,
                smoothCorrection: true));
            Assert.IsFalse(clock.IsSynchronized);
        }

        [Test]
        public void HardReset_ClearsAvailabilityAndDiagnostics()
        {
            var clock = CreateClock(out var time);
            clock.ApplySeed("room-a", FirstOrigin, 2_000d, time.NowMilliseconds, smoothCorrection: false);
            clock.ApplyMeasurement(
                "room-a",
                FirstOrigin,
                roomTimeMilliseconds: 2_000d,
                measuredAtMonotonicMilliseconds: time.NowMilliseconds,
                estimatedRoundTripMilliseconds: 12d,
                smoothCorrection: false);

            clock.Reset();

            Assert.IsFalse(clock.IsSynchronized);
            Assert.IsNull(clock.CreatedAtEpochMilliseconds);
            Assert.IsTrue(double.IsNaN(clock.EstimatedRoundTripMilliseconds));
        }

        [Test]
        public void NetworkMeasurement_RemovesServerProcessingAndAddsHalfTheNetworkRtt()
        {
            bool valid = RoomClockProjection.TryCalculateNetworkMeasurement(
                sentAtMonotonicMilliseconds: 1_000d,
                receivedAtMonotonicMilliseconds: 1_050d,
                serverReceiveRoomTimeMilliseconds: 20_010L,
                serverSendRoomTimeMilliseconds: 20_020L,
                out double roomTimeAtReceiptMilliseconds,
                out double roundTripMilliseconds);

            Assert.IsTrue(valid);
            Assert.AreEqual(40d, roundTripMilliseconds, 0.0001d);
            Assert.AreEqual(20_040d, roomTimeAtReceiptMilliseconds, 0.0001d);
        }

        [Test]
        public void NetworkMeasurement_RejectsImpossibleServerInterval()
        {
            bool valid = RoomClockProjection.TryCalculateNetworkMeasurement(
                sentAtMonotonicMilliseconds: 1_000d,
                receivedAtMonotonicMilliseconds: 1_010d,
                serverReceiveRoomTimeMilliseconds: 20_000L,
                serverSendRoomTimeMilliseconds: 20_020L,
                out _,
                out _);

            Assert.IsFalse(valid);
        }

        [Test]
        public void NetworkMeasurement_RejectsStalledRoundTrip()
        {
            bool valid = RoomClockProjection.TryCalculateNetworkMeasurement(
                sentAtMonotonicMilliseconds: 1_000d,
                receivedAtMonotonicMilliseconds: 12_001d,
                serverReceiveRoomTimeMilliseconds: 20_000L,
                serverSendRoomTimeMilliseconds: 20_000L,
                out _,
                out _);

            Assert.IsFalse(valid);
        }

        [Test]
        public void DoubleProjection_RemainsPreciseBeyondUintAndFloatRanges()
        {
            var clock = CreateClock(out var time);
            const double sixtyDaysMilliseconds = 60d * 24d * 60d * 60d * 1000d;
            clock.ApplySeed("room-a", FirstOrigin, sixtyDaysMilliseconds, time.NowMilliseconds, smoothCorrection: false);

            time.NowMilliseconds += 0.25d;

            Assert.AreEqual(sixtyDaysMilliseconds + 0.25d, clock.ElapsedMilliseconds, 0.001d);
        }

        private static RoomClockProjection CreateClock(out FakeMonotonicTimeSource time)
        {
            time = new FakeMonotonicTimeSource { NowMilliseconds = 10_000d };
            var source = time;
            return new RoomClockProjection(() => source.NowMilliseconds);
        }
    }
}
