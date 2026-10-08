using System;
using UnityEngine;
using UnityEngine.Events;

namespace BS
{
    /// <summary>
    /// Fires <see cref="OnSync"/> at shared-clock interval boundaries, so every client triggers it at the
    /// same real-world instant. Wire it to (re)start an Animation clip or a PlayableDirector to keep scripted
    /// animation in phase across clients with NO per-object network sync — drift and late joiners re-align at
    /// each boundary. Port of Banter's SyncLoop, on top of <see cref="SyncedClock"/>.
    ///
    /// For continuous motion along a path use the spline mover instead (it reads the clock every frame);
    /// SyncLoop is for discrete "everyone (re)start now" triggers.
    /// </summary>
    [AddComponentMenu("BS/Sync/Sync Loop")]
    public class SyncLoop : MonoBehaviour
    {
        [Tooltip("Interval in seconds between OnSync fires, aligned to the shared clock.")]
        [Min(0.05f)] public double interval = 10.0;

        [Tooltip("Fire once, then stop.")]
        public bool runOnce = false;

        [Tooltip("Fired at each shared-clock interval boundary — the same instant on every client.")]
        public UnityEvent OnSync;

        // Edge detector on the boundary count: fires once when the clock enters a new interval. Banter's version
        // armed in the last second of an interval and fired in the first, windows that overlap once the
        // interval is under 2 s, so it fired every frame there.
        private double _countedInterval; // the interval _lastBoundary counts in; 0 until the first frame
        private long _lastBoundary;
        private bool _hasRun;

        private void Update()
        {
            if (interval <= 0.0 || _hasRun) return;

            double now = SyncedClock.NowSeconds;
            long boundary = (long)Math.Floor(now / interval);

            if (interval != _countedInterval)
            {
                // First frame, or the interval changed: count from here. Like Banter's, a loop that starts
                // mid-interval first fires at the next boundary.
                _countedInterval = interval;
                _lastBoundary = boundary;
                return;
            }
            // Same interval, or the clock stepped back (a server offset update): nothing new crossed, and a
            // boundary that already fired doesn't fire again.
            if (boundary <= _lastBoundary) return;
            _lastBoundary = boundary;

            // Only close to the boundary, as Banter's 1 s window did: after a long hitch the moment has passed
            // and the next boundary realigns everyone. An interval under 1 s always qualifies.
            double sinceBoundary = now - boundary * interval;
            if (sinceBoundary < Math.Min(1.0, interval))
            {
                OnSync?.Invoke();
                if (runOnce) _hasRun = true;
            }
        }
    }
}
