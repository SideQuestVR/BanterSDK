using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;

namespace BS.LocalMultiplayer.Overlay
{
    /// <summary>
    /// The overlay's show/hide key: an Input System <see cref="Key"/> named in the settings file
    /// (<see cref="LocalMultiplayerSettings.overlayKey"/>, "F8" by default). Names are the enum's, matched
    /// without regard to case. The string-only members are what the editor window uses, so it needs no
    /// Input System reference of its own.
    /// </summary>
    public static class OverlayKeys
    {
        public const string DefaultName = "F8";

        // Built once from the enum; read-only afterwards, so nothing here needs resetting between plays.
        static Dictionary<string, Entry> s_ByName;

        struct Entry
        {
            public Key Key;
            public string Name;
        }

        /// <summary>True when <paramref name="name"/> names a keyboard key.</summary>
        public static bool IsValid(string name) => TryParse(name, out _);

        /// <summary>The key's own spelling ("f8" gives "F8"), or null when no key has that name.</summary>
        public static string Canonical(string name)
        {
            return TryGet(name, out var entry) ? entry.Name : null;
        }

        /// <summary>The key for a settings name. Numbers, "None" and the Input System's dummy IME key are refused.</summary>
        internal static bool TryParse(string name, out Key key)
        {
            if (TryGet(name, out var entry))
            {
                key = entry.Key;
                return true;
            }
            key = Key.None;
            return false;
        }

        static bool TryGet(string name, out Entry entry)
        {
            entry = default;
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }
            return ByName.TryGetValue(name.Trim(), out entry);
        }

        static Dictionary<string, Entry> ByName
        {
            get
            {
                if (s_ByName != null)
                {
                    return s_ByName;
                }
                var map = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
                // Names rather than values: Enum.TryParse would also take "12", and a value with two names would
                // come back under whichever name ToString picks.
                foreach (var name in Enum.GetNames(typeof(Key)))
                {
                    // None indexes no key, and IMESelected is an obsolete dummy that the keyboard refuses.
                    if (name == "None" || name == "IMESelected")
                    {
                        continue;
                    }
                    map[name] = new Entry { Key = (Key)Enum.Parse(typeof(Key), name), Name = name };
                }
                s_ByName = map;
                return s_ByName;
            }
        }
    }
}
