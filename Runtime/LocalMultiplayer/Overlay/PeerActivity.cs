using System;
using System.Collections.Generic;

namespace BS.LocalMultiplayer.Overlay
{
    /// <summary>
    /// What the overlay and the window show per player beyond the session's peer list: the measured pose rate
    /// (an unfocused Multiplayer Play Mode player runs throttled, and its orb stutters), the seat it sits on
    /// (its "pilot" engine user state) and how many attachments it broadcasts ("attachment_&lt;Id&gt;"). Keyed by
    /// room session id. Engine user state includes this player's own echo, so it covers this player too.
    /// Fed from the session's events by <see cref="OverlayModule"/>; main thread only.
    /// </summary>
    public sealed class PeerActivity
    {
        // The engine root keys AttachmentNetworkBridge writes (AttachmentNetworkBridge.cs:32-33).
        const string PilotKey = "pilot";
        const string AttachmentPrefix = "attachment_";

        // Pose rates are counted over windows of at least this long.
        const double WindowSeconds = 1.0;

        public sealed class Entry
        {
            /// <summary>Pose frames per second over the last window; negative until a frame has arrived.</summary>
            public float PoseHz = -1f;

            /// <summary>The seat this player sits on (the "pilot" key's seat id); empty while standing.</summary>
            public string SeatId = string.Empty;

            /// <summary>The attachments this player broadcasts.</summary>
            public int Attachments => AttachmentKeys.Count;

            internal int Frames;
            internal bool EverFramed;
            internal readonly HashSet<string> AttachmentKeys = new HashSet<string>(StringComparer.Ordinal);
        }

        readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        double _windowStart = double.NaN;

        internal int Count => _entries.Count;

        public bool TryGet(string roomSessionId, out Entry entry)
        {
            entry = null;
            return !string.IsNullOrEmpty(roomSessionId) && _entries.TryGetValue(roomSessionId, out entry);
        }

        /// <summary>One tf.participant frame arrived from this player. Allocates only for a new player.</summary>
        internal void OnParticipantFrame(string roomSessionId)
        {
            if (string.IsNullOrEmpty(roomSessionId))
            {
                return;
            }
            var entry = GetOrAdd(roomSessionId);
            entry.Frames++;
            entry.EverFramed = true;
        }

        /// <summary>One engine user-state path changed. True when what is shown changed.</summary>
        internal bool OnEngineState(string roomSessionId, string path, string value, bool deleted)
        {
            if (string.IsNullOrEmpty(roomSessionId) || string.IsNullOrEmpty(path))
            {
                return false;
            }
            var cleared = deleted || string.IsNullOrEmpty(value);
            if (string.Equals(path, PilotKey, StringComparison.Ordinal))
            {
                // "" is how a player stands up: a set, not a delete (AttachmentNetworkBridge.cs:117-133).
                var seat = cleared ? string.Empty : SeatOf(value);
                if (!_entries.TryGetValue(roomSessionId, out var entry))
                {
                    if (seat.Length == 0)
                    {
                        return false;
                    }
                    entry = GetOrAdd(roomSessionId);
                }
                if (string.Equals(entry.SeatId, seat, StringComparison.Ordinal))
                {
                    return false;
                }
                entry.SeatId = seat;
                return true;
            }
            if (path.StartsWith(AttachmentPrefix, StringComparison.Ordinal))
            {
                // Detaching writes "" too (AttachmentNetworkBridge.cs:109-115).
                if (cleared)
                {
                    return _entries.TryGetValue(roomSessionId, out var known) && known.AttachmentKeys.Remove(path);
                }
                return GetOrAdd(roomSessionId).AttachmentKeys.Add(path);
            }
            return false;
        }

        /// <summary>The room's engine user state on join: it replaces whatever was known.</summary>
        internal void OnSnapshot(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> users)
        {
            foreach (var entry in _entries.Values)
            {
                entry.SeatId = string.Empty;
                entry.AttachmentKeys.Clear();
            }
            if (users == null)
            {
                return;
            }
            foreach (var user in users)
            {
                if (user.Value == null)
                {
                    continue;
                }
                foreach (var path in user.Value)
                {
                    OnEngineState(user.Key, path.Key, path.Value, false);
                }
            }
        }

        /// <summary>The player's user state was removed (it left and its grace ran out).</summary>
        internal bool OnRemoved(string roomSessionId)
        {
            return !string.IsNullOrEmpty(roomSessionId) && _entries.Remove(roomSessionId);
        }

        /// <summary>This player left the room: everything known about the others is gone.</summary>
        internal void Clear()
        {
            _entries.Clear();
            _windowStart = double.NaN;
        }

        /// <summary>
        /// Closes the pose-rate window once at least a second has passed (<paramref name="now"/> in seconds).
        /// True when a shown rate changed.
        /// </summary>
        internal bool Tick(double now)
        {
            if (double.IsNaN(_windowStart))
            {
                _windowStart = now;
                return false;
            }
            var elapsed = now - _windowStart;
            if (elapsed < WindowSeconds)
            {
                return false;
            }
            var changed = false;
            foreach (var entry in _entries.Values)
            {
                if (!entry.EverFramed)
                {
                    // Never sent a pose (this player's own entry, or a peer still loading): stays "not measured".
                    continue;
                }
                var hz = (float)(entry.Frames / elapsed);
                if (Math.Round(hz) != Math.Round(entry.PoseHz))
                {
                    changed = true;
                }
                entry.PoseHz = hz;
                entry.Frames = 0;
            }
            _windowStart = now;
            return changed;
        }

        /// <summary>The seat id of a pilot value "seatId|hx|hy|hz|qx|qy|qz|qw" (a seat id alone is valid too).</summary>
        internal static string SeatOf(string pilotValue)
        {
            if (string.IsNullOrEmpty(pilotValue))
            {
                return string.Empty;
            }
            var bar = pilotValue.IndexOf('|');
            return bar < 0 ? pilotValue : pilotValue.Substring(0, bar);
        }

        Entry GetOrAdd(string roomSessionId)
        {
            if (!_entries.TryGetValue(roomSessionId, out var entry))
            {
                entry = new Entry();
                _entries.Add(roomSessionId, entry);
            }
            return entry;
        }
    }
}
