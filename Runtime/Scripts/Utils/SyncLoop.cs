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

        // Edge detector: arm near the end of an interval, fire as the clock crosses the boundary. The 1s
        // window matches Banter's SyncLoop and tolerates frame-rate/clock jitter around the boundary.
        private bool _armed;
        private bool _hasRun;

        private void Update()
        {
            if (interval <= 0.0 || _hasRun) return;

            double now = SyncedClock.NowSeconds;
            double sinceLast = now - Math.Floor(now / interval) * interval; // 0..interval

            if (sinceLast > interval - 1.0)
                _armed = true;
            if (sinceLast < 1.0 && _armed)
            {
                _armed = false;
                OnSync?.Invoke();
                if (runOnce) _hasRun = true;
            }
        }
    }
}
