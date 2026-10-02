// <mirror source="Assets/Systems/Networking/RoomClockSdkBridge.cs" sha256="051420fa1dfd51a96836892388832084a00e0f999ba64be97c0c2e6c8bcfbdec" mode="port" />
// <mirror source="Packages/com.sidequest.packetparty/Runtime/PacketPartyClient.cs" sha256="6ab88cbf53c894dcdd8273685d80972419478499cfec5f09de9499a52b97367d" mode="port" />
using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace BS.LocalMultiplayer
{
    /// <summary>
    /// Muxes the relay's room clock into the SDK's shared <see cref="SyncedClock"/>, as RoomClockSdkBridge does
    /// with PacketParty's (RoomClockSdkBridge.cs:48-71), so SyncLoop and the spline movers phase-lock the same
    /// way they do in production. The room clock itself is PacketPartyClient's: seeded from the welcome,
    /// refined by a burst of three signal.roomClockPing samples keeping the lowest round trip, then measured
    /// again every 30 s (PacketPartyClient.cs:3567-3772). Every player shares one machine, so the offset
    /// lands near zero; the point is the same synced and unsynced transitions as production. When the room
    /// is left the offset goes back to 0, plain UTC.
    /// </summary>
    [LocalModule(ModuleOrder.Clock)]
    [AddComponentMenu("")]
    public sealed class LocalRoomClock : MonoBehaviour, ILocalModule
    {
        const int InitialRoomClockSampleCount = 3;
        static readonly TimeSpan InitialRoomClockSyncTimeout = TimeSpan.FromSeconds(4);
        static readonly TimeSpan RoomClockRefreshInterval = TimeSpan.FromSeconds(30);

        // Whether the shared offset was last written by us, for the reset between Play sessions.
        static bool s_offsetApplied;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            if (!s_offsetApplied) return;
            SyncedClock.ServerOffsetSeconds = 0.0;
            s_offsetApplied = false;
        }

        readonly struct RoomClockMeasurement
        {
            public readonly long CreatedAtEpochMilliseconds;
            public readonly double RoomTimeAtReceiptMilliseconds;
            public readonly double ReceivedAtMonotonicMilliseconds;
            public readonly double RoundTripMilliseconds;

            public RoomClockMeasurement(long createdAtEpochMilliseconds, double roomTimeAtReceiptMilliseconds,
                double receivedAtMonotonicMilliseconds, double roundTripMilliseconds)
            {
                CreatedAtEpochMilliseconds = createdAtEpochMilliseconds;
                RoomTimeAtReceiptMilliseconds = roomTimeAtReceiptMilliseconds;
                ReceivedAtMonotonicMilliseconds = receivedAtMonotonicMilliseconds;
                RoundTripMilliseconds = roundTripMilliseconds;
            }
        }

        readonly RoomClockProjection _roomClock = new RoomClockProjection(() => MachineClock.NowMs);
        LocalSession _session;
        // The pings belong to one socket (PacketPartyClient's _roomClockSocketCts).
        CancellationTokenSource _roomClockSocketCts;
        // True once we've written a server-derived offset, so we only reset to UTC on an actual sync→unsync
        // transition (not every frame while offline).
        bool _applied;
        [NonSerialized] bool _live;

        public void Install(ILocalHost host)
        {
            _session = host.Session as LocalSession;
            if (_session == null)
            {
                throw new InvalidOperationException("LocalRoomClock needs the host's LocalSession");
            }
            _session.On(RelayProtocol.Welcome, OnWelcome);
            _session.StateChanged += OnStateChanged;
            _session.RoomLeft += OnRoomLeft;
            _live = true;
        }

        public void Uninstall()
        {
            _live = false;
            if (_session != null)
            {
                _session.Off(RelayProtocol.Welcome, OnWelcome);
                _session.StateChanged -= OnStateChanged;
                _session.RoomLeft -= OnRoomLeft;
            }
            StopRoomClockSocket();
            _roomClock.Reset();
            ResetToUtc();
        }

        void OnDisable() => ResetToUtc();

        void Update()
        {
            if (!_live) return;
            if (_roomClock.IsSynchronized && _roomClock.CreatedAtEpochMilliseconds.HasValue
                && _roomClock.TryGetElapsedMilliseconds(out double elapsedMs))
            {
                double serverNowSeconds = (_roomClock.CreatedAtEpochMilliseconds.Value + elapsedMs) * 0.001d;
                double utcNowSeconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 0.001d;
                SyncedClock.ServerOffsetSeconds = serverNowSeconds - utcNowSeconds;
                _applied = true;
                s_offsetApplied = true;
            }
            else if (_applied)
            {
                ResetToUtc();
            }
        }

        void ResetToUtc()
        {
            SyncedClock.ServerOffsetSeconds = 0.0;
            _applied = false;
            s_offsetApplied = false;
        }

        // ---------------------------------------------------------------- the room clock

        void OnWelcome(JObject welcome)
        {
            if (!_live) return;
            var receivedAt = _session.CurrentMessageReceivedAtMs;
            StopRoomClockSocket();
            var cts = new CancellationTokenSource();
            _roomClockSocketCts = cts;
            _ = SynchronizeRoomClockFromWelcomeAsync(welcome, receivedAt, cts.Token);
        }

        // A dropped socket stops the measurements but keeps the projection running through the resume.
        void OnStateChanged(SessionState state)
        {
            if (state == SessionState.Reconnecting || state == SessionState.Failed || state == SessionState.Idle)
            {
                StopRoomClockSocket();
            }
        }

        // A hard disconnect: no live socket remains to refine the projection (TeardownAsync → Reset).
        void OnRoomLeft()
        {
            StopRoomClockSocket();
            _roomClock.Reset();
        }

        void StopRoomClockSocket()
        {
            var cts = _roomClockSocketCts;
            _roomClockSocketCts = null;
            if (cts == null) return;
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        async Task SynchronizeRoomClockFromWelcomeAsync(JObject welcome, double receivedAtMonotonicMilliseconds, CancellationToken cancellation)
        {
            try
            {
                var seed = welcome["roomClock"] as JObject;
                if (seed == null)
                {
                    // An older relay without a room clock: SyncedClock stays plain UTC.
                    _roomClock.Reset();
                    return;
                }

                long createdAt = RelayProtocol.Long(seed, "roomCreatedAtEpochMs", -1);
                long serverTime = RelayProtocol.Long(seed, "serverTimeEpochMs", -1);
                long roomTime = RelayProtocol.Long(seed, "roomTimeMs", -1);
                if (createdAt <= 0 || serverTime < createdAt || roomTime < 0 || roomTime != serverTime - createdAt)
                {
                    // PacketPartyClient fails the join here (InvalidWelcome); the local host stays in the room
                    // and keeps SyncedClock on UTC instead.
                    Debug.LogWarning("[LocalMP] The relay's welcome carries an invalid room clock seed; SyncedClock stays on UTC.");
                    _roomClock.Reset();
                    return;
                }

                string roomKey = RelayProtocol.Str(welcome, "roomKey");
                if (string.IsNullOrEmpty(roomKey))
                {
                    Debug.LogWarning("[LocalMP] The relay's welcome has no roomKey for its room clock; SyncedClock stays on UTC.");
                    _roomClock.Reset();
                    return;
                }

                if (double.IsNaN(receivedAtMonotonicMilliseconds)
                    || double.IsInfinity(receivedAtMonotonicMilliseconds)
                    || receivedAtMonotonicMilliseconds < 0d)
                {
                    receivedAtMonotonicMilliseconds = MachineClock.NowMs;
                }

                bool continuingSameRoomClock = _roomClock.IsSynchronized
                    && _roomClock.CreatedAtEpochMilliseconds == createdAt;
                _roomClock.ApplySeed(
                    roomKey,
                    createdAt,
                    roomTime,
                    receivedAtMonotonicMilliseconds,
                    smoothCorrection: continuingSameRoomClock);

                RoomClockMeasurement? best = null;
                using (var burstCts = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
                {
                    burstCts.CancelAfter(InitialRoomClockSyncTimeout);
                    for (int i = 0; i < InitialRoomClockSampleCount; i++)
                    {
                        try
                        {
                            RoomClockMeasurement? measurement = await MeasureRoomClockAsync(roomKey, createdAt, burstCts.Token);
                            if (measurement.HasValue
                                && (!best.HasValue
                                    || measurement.Value.RoundTripMilliseconds < best.Value.RoundTripMilliseconds))
                            {
                                best = measurement;
                            }
                        }
                        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
                        {
                            break;
                        }
                        catch (LocalRelayException ex)
                        {
                            // The welcome seed remains useful if a transient failure prevents the optional refinement.
                            Debug.Log($"[LocalMP] room clock refinement unavailable: {ex.Message}");
                            break;
                        }
                    }
                }

                cancellation.ThrowIfCancellationRequested();
                if (best.HasValue)
                {
                    RoomClockMeasurement measurement = best.Value;
                    _roomClock.ApplyMeasurement(
                        roomKey,
                        measurement.CreatedAtEpochMilliseconds,
                        measurement.RoomTimeAtReceiptMilliseconds,
                        measurement.ReceivedAtMonotonicMilliseconds,
                        measurement.RoundTripMilliseconds,
                        smoothCorrection: true);
                }

                await RoomClockRefreshLoopAsync(roomKey, createdAt, cancellation);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        async Task<RoomClockMeasurement?> MeasureRoomClockAsync(
            string expectedRoomKey,
            long expectedCreatedAtEpochMilliseconds,
            CancellationToken cancellation)
        {
            if (!_live) throw new OperationCanceledException(cancellation);
            TimedReply response = await _session.RequestTimedAsync(RelayProtocol.RoomClockPing, null, cancellation, allowWhileJoining: true);
            JObject body = response.Body;
            if (!string.Equals(body.Value<string>("type"), RelayProtocol.RoomClockPong, StringComparison.Ordinal))
                return null;

            long? createdAtEpochMilliseconds = body.Value<long?>("roomCreatedAtEpochMs");
            string roomKey = body.Value<string>("roomKey");
            long? serverReceiveRoomTimeMilliseconds = body.Value<long?>("serverReceiveRoomTimeMs");
            long? serverSendRoomTimeMilliseconds = body.Value<long?>("serverSendRoomTimeMs");
            if (!createdAtEpochMilliseconds.HasValue
                || createdAtEpochMilliseconds.Value != expectedCreatedAtEpochMilliseconds
                || !string.Equals(roomKey, expectedRoomKey, StringComparison.Ordinal)
                || !serverReceiveRoomTimeMilliseconds.HasValue
                || !serverSendRoomTimeMilliseconds.HasValue
                || serverReceiveRoomTimeMilliseconds.Value < 0
                || serverSendRoomTimeMilliseconds.Value < serverReceiveRoomTimeMilliseconds.Value)
            {
                return null;
            }

            if (!RoomClockProjection.TryCalculateNetworkMeasurement(
                response.SentAtMs,
                response.ReceivedAtMs,
                serverReceiveRoomTimeMilliseconds.Value,
                serverSendRoomTimeMilliseconds.Value,
                out double roomTimeAtReceiptMilliseconds,
                out double networkRoundTripMilliseconds))
            {
                return null;
            }

            // The two server timestamps share the room-relative clock, so the NTP-style calculation does not
            // need to compare client and server wall clocks.
            return new RoomClockMeasurement(
                createdAtEpochMilliseconds.Value,
                roomTimeAtReceiptMilliseconds,
                response.ReceivedAtMs,
                networkRoundTripMilliseconds);
        }

        // Unlike production's, this loop stays on the main thread: the session is main-thread only.
        async Task RoomClockRefreshLoopAsync(string roomKey, long createdAtEpochMilliseconds, CancellationToken cancellation)
        {
            while (!cancellation.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(RoomClockRefreshInterval, cancellation);
                    RoomClockMeasurement? measurement = await MeasureRoomClockAsync(
                        roomKey,
                        createdAtEpochMilliseconds,
                        cancellation);
                    cancellation.ThrowIfCancellationRequested();
                    if (!measurement.HasValue
                        || _roomClock.CreatedAtEpochMilliseconds != createdAtEpochMilliseconds)
                    {
                        continue;
                    }

                    RoomClockMeasurement value = measurement.Value;
                    double previousRoundTrip = _roomClock.EstimatedRoundTripMilliseconds;
                    if (!double.IsNaN(previousRoundTrip)
                        && value.RoundTripMilliseconds > Math.Max(250d, previousRoundTrip * 4d))
                    {
                        continue;
                    }
                    _roomClock.ApplyMeasurement(
                        roomKey,
                        value.CreatedAtEpochMilliseconds,
                        value.RoomTimeAtReceiptMilliseconds,
                        value.ReceivedAtMonotonicMilliseconds,
                        value.RoundTripMilliseconds,
                        smoothCorrection: true);
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    Debug.Log($"[LocalMP] room clock refresh failed: {ex.Message}");
                }
            }
        }
    }
}
