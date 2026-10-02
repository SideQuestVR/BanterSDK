using System;
using System.Collections.Generic;
using UnityEngine;

namespace BS.LocalMultiplayer
{
    /// <summary>
    /// The notes the overlay and the window list (ILocalDiagnostics): one entry per key, a Set replaces it.
    /// Modules still log to the console themselves; this is the at-a-glance list. Main thread only.
    /// </summary>
    internal sealed class LocalDiagnostics : ILocalDiagnostics
    {
        readonly List<DiagnosticEntry> _entries = new List<DiagnosticEntry>();

        public IReadOnlyList<DiagnosticEntry> Entries => _entries;

        public event Action Changed;

        public void Set(string key, DiagnosticLevel level, string message)
        {
            if (string.IsNullOrEmpty(key)) return;
            message = message ?? "";
            var index = IndexOf(key);
            if (index >= 0)
            {
                var entry = _entries[index];
                // Repeated identical notes (a retry loop) don't need to wake the overlay.
                if (entry.Level == level && string.Equals(entry.Message, message, StringComparison.Ordinal)) return;
                entry.Level = level;
                entry.Message = message;
                entry.Time = Time.realtimeSinceStartupAsDouble;
            }
            else
            {
                _entries.Add(new DiagnosticEntry
                {
                    Key = key,
                    Level = level,
                    Message = message,
                    Time = Time.realtimeSinceStartupAsDouble,
                });
            }
            RaiseChanged();
        }

        public void Clear(string key)
        {
            var index = IndexOf(key);
            if (index < 0) return;
            _entries.RemoveAt(index);
            RaiseChanged();
        }

        public void ClearPrefix(string prefix)
        {
            if (string.IsNullOrEmpty(prefix)) return;
            var removed = _entries.RemoveAll(entry => entry.Key != null && entry.Key.StartsWith(prefix, StringComparison.Ordinal));
            if (removed > 0) RaiseChanged();
        }

        int IndexOf(string key)
        {
            for (var i = 0; i < _entries.Count; i++)
            {
                if (string.Equals(_entries[i].Key, key, StringComparison.Ordinal)) return i;
            }
            return -1;
        }

        void RaiseChanged()
        {
            try
            {
                Changed?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }
}
