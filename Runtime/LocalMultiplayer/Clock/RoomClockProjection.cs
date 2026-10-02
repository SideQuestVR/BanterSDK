// <mirror source="Packages/com.sidequest.packetparty/Runtime/PacketPartyRoomClock.cs" sha256="3133369b2b95b8bfa95cc47915cee0c45d4dea8ada67c9324886c63d57e10c58" mode="port" />
using System;

namespace BS.LocalMultiplayer
{
    /// <summary>
    /// A monotonic projection of the server-authoritative number of milliseconds
    /// elapsed since the current logical room was created: PacketPartyRoomClock, with the time source
    /// passed in as a delegate (<see cref="MachineClock.NowMs"/> in the host, a fake in tests).
    /// </summary>
    /// <remarks>
    /// The clock is synchronized from the welcome's seed and refined by room-clock pings. The projection
    /// is independent of Unity's time scale and continues through resumed sessions.
    /// </remarks>
    internal sealed class RoomClockProjection
    {
        // Correct at no more than ten percent of real time. A clock that is ahead
        // therefore slows to 0.9x, but never stops or moves backwards; a clock that
        // is behind catches up at 1.1x without a visible discontinuity.
        internal const double SlewFraction = 0.10d;
        internal const double MaximumAcceptedRoundTripMilliseconds = 10_000d;

        private readonly Func<double> _timeSource;
        private readonly object _gate = new object();
        private bool _isSynchronized;
        private string _roomKey;
        private long? _createdAtEpochMilliseconds;
        private double _elapsedMilliseconds;
        private double _lastEvaluationMilliseconds;
        private double _pendingCorrectionMilliseconds;
        private double _estimatedRoundTripMilliseconds = double.NaN;

        internal RoomClockProjection(Func<double> timeSource)
        {
            _timeSource = timeSource ?? throw new ArgumentNullException(nameof(timeSource));
        }

        /// <summary>
        /// Whether this clock has an authoritative room origin and a usable local
        /// projection. This is false before connecting and after a hard disconnect.
        /// </summary>
        public bool IsSynchronized
        {
            get
            {
                lock (_gate) return _isSynchronized;
            }
        }

        /// <summary>
        /// Milliseconds elapsed since the logical room was created. Returns zero while
        /// <see cref="IsSynchronized"/> is false; use
        /// <see cref="TryGetElapsedMilliseconds"/> when reading during connection setup.
        /// </summary>
        public double ElapsedMilliseconds
        {
            get
            {
                lock (_gate)
                {
                    return _isSynchronized ? Evaluate(_timeSource()) : 0d;
                }
            }
        }

        /// <summary>
        /// Seconds elapsed since logical room creation. This is a convenience for APIs
        /// such as <c>PlayableDirector.time</c>, which use double-precision seconds.
        /// </summary>
        public double ElapsedSeconds => ElapsedMilliseconds * 0.001d;

        /// <summary>
        /// The most recently accepted signaling round-trip estimate, in milliseconds,
        /// or <see cref="double.NaN"/> until a ping sample has been accepted. Initial
        /// synchronization selects the lowest-latency sample in its burst.
        /// </summary>
        public double EstimatedRoundTripMilliseconds
        {
            get
            {
                lock (_gate) return _estimatedRoundTripMilliseconds;
            }
        }

        /// <summary>
        /// Server-authoritative Unix epoch milliseconds at which this logical room was
        /// created, or null while the clock is unavailable.
        /// </summary>
        public long? CreatedAtEpochMilliseconds
        {
            get
            {
                lock (_gate) return _createdAtEpochMilliseconds;
            }
        }

        /// <summary>
        /// Attempts to read the current room time without using zero as an availability
        /// sentinel.
        /// </summary>
        public bool TryGetElapsedMilliseconds(out double elapsedMilliseconds)
        {
            lock (_gate)
            {
                if (!_isSynchronized)
                {
                    elapsedMilliseconds = 0d;
                    return false;
                }

                elapsedMilliseconds = Evaluate(_timeSource());
                return true;
            }
        }

        internal void ApplySeed(
            string roomKey,
            long createdAtEpochMilliseconds,
            double roomTimeMilliseconds,
            double receivedAtMonotonicMilliseconds,
            bool smoothCorrection)
        {
            ApplyObservation(
                roomKey,
                createdAtEpochMilliseconds,
                roomTimeMilliseconds,
                receivedAtMonotonicMilliseconds,
                double.NaN,
                smoothCorrection,
                requireCurrentIncarnation: false);
        }

        internal bool ApplyMeasurement(
            string roomKey,
            long createdAtEpochMilliseconds,
            double roomTimeMilliseconds,
            double measuredAtMonotonicMilliseconds,
            double estimatedRoundTripMilliseconds,
            bool smoothCorrection)
        {
            return ApplyObservation(
                roomKey,
                createdAtEpochMilliseconds,
                roomTimeMilliseconds,
                measuredAtMonotonicMilliseconds,
                estimatedRoundTripMilliseconds,
                smoothCorrection,
                requireCurrentIncarnation: true);
        }

        internal void Reset()
        {
            lock (_gate)
            {
                _isSynchronized = false;
                _roomKey = null;
                _createdAtEpochMilliseconds = null;
                _elapsedMilliseconds = 0d;
                _lastEvaluationMilliseconds = _timeSource();
                _pendingCorrectionMilliseconds = 0d;
                _estimatedRoundTripMilliseconds = double.NaN;
            }
        }

        internal static bool TryCalculateNetworkMeasurement(
            double sentAtMonotonicMilliseconds,
            double receivedAtMonotonicMilliseconds,
            long serverReceiveRoomTimeMilliseconds,
            long serverSendRoomTimeMilliseconds,
            out double roomTimeAtReceiptMilliseconds,
            out double networkRoundTripMilliseconds)
        {
            roomTimeAtReceiptMilliseconds = 0d;
            networkRoundTripMilliseconds = 0d;
            if (serverReceiveRoomTimeMilliseconds < 0
                || serverSendRoomTimeMilliseconds < serverReceiveRoomTimeMilliseconds)
            {
                return false;
            }

            double localRoundTripMilliseconds =
                receivedAtMonotonicMilliseconds - sentAtMonotonicMilliseconds;
            double serverProcessingMilliseconds =
                (double)serverSendRoomTimeMilliseconds - serverReceiveRoomTimeMilliseconds;
            if (!IsFiniteNonNegative(sentAtMonotonicMilliseconds)
                || !IsFiniteNonNegative(receivedAtMonotonicMilliseconds)
                || localRoundTripMilliseconds < 0d
                // Redis and Stopwatch timestamps have different millisecond
                // quantization. Tolerate only the rounding-sized negative residual.
                || serverProcessingMilliseconds - localRoundTripMilliseconds > 2d)
            {
                return false;
            }

            networkRoundTripMilliseconds = Math.Max(
                0d,
                localRoundTripMilliseconds - serverProcessingMilliseconds);
            if (networkRoundTripMilliseconds > MaximumAcceptedRoundTripMilliseconds)
            {
                networkRoundTripMilliseconds = 0d;
                return false;
            }
            roomTimeAtReceiptMilliseconds =
                serverSendRoomTimeMilliseconds + networkRoundTripMilliseconds * 0.5d;
            return true;
        }

        private bool ApplyObservation(
            string roomKey,
            long createdAtEpochMilliseconds,
            double roomTimeMilliseconds,
            double measuredAtMonotonicMilliseconds,
            double estimatedRoundTripMilliseconds,
            bool smoothCorrection,
            bool requireCurrentIncarnation)
        {
            if (string.IsNullOrEmpty(roomKey))
                throw new ArgumentException("room key is required", nameof(roomKey));
            if (createdAtEpochMilliseconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(createdAtEpochMilliseconds));
            if (!IsFiniteNonNegative(roomTimeMilliseconds))
                throw new ArgumentOutOfRangeException(nameof(roomTimeMilliseconds));
            if (!IsFiniteNonNegative(measuredAtMonotonicMilliseconds))
                throw new ArgumentOutOfRangeException(nameof(measuredAtMonotonicMilliseconds));
            if (!double.IsNaN(estimatedRoundTripMilliseconds)
                && !IsFiniteNonNegative(estimatedRoundTripMilliseconds))
                throw new ArgumentOutOfRangeException(nameof(estimatedRoundTripMilliseconds));

            lock (_gate)
            {
                bool sameIncarnation = _isSynchronized
                    && string.Equals(_roomKey, roomKey, StringComparison.Ordinal)
                    && _createdAtEpochMilliseconds == createdAtEpochMilliseconds;
                if (requireCurrentIncarnation && !sameIncarnation)
                {
                    return false;
                }

                double now = _timeSource();
                double projectedAtNow = roomTimeMilliseconds + Math.Max(0d, now - measuredAtMonotonicMilliseconds);

                if (!sameIncarnation)
                {
                    _estimatedRoundTripMilliseconds = double.NaN;
                }

                if (!sameIncarnation || !smoothCorrection)
                {
                    _elapsedMilliseconds = projectedAtNow;
                    _lastEvaluationMilliseconds = now;
                    _pendingCorrectionMilliseconds = 0d;
                }
                else
                {
                    double current = Evaluate(now);
                    _pendingCorrectionMilliseconds = projectedAtNow - current;
                }

                _isSynchronized = true;
                _roomKey = roomKey;
                _createdAtEpochMilliseconds = createdAtEpochMilliseconds;
                if (!double.IsNaN(estimatedRoundTripMilliseconds))
                {
                    _estimatedRoundTripMilliseconds = estimatedRoundTripMilliseconds;
                }
                return true;
            }
        }

        private double Evaluate(double nowMilliseconds)
        {
            double delta = Math.Max(0d, nowMilliseconds - _lastEvaluationMilliseconds);
            if (delta <= 0d) return _elapsedMilliseconds;

            double maximumCorrection = delta * SlewFraction;
            double correction = Math.Max(
                -maximumCorrection,
                Math.Min(maximumCorrection, _pendingCorrectionMilliseconds));
            _elapsedMilliseconds += delta + correction;
            _lastEvaluationMilliseconds = nowMilliseconds;
            _pendingCorrectionMilliseconds -= correction;
            return _elapsedMilliseconds;
        }

        private static bool IsFiniteNonNegative(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0d;
        }
    }
}
