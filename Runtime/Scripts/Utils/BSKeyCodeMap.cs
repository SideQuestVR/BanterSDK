using System.Collections.Generic;
using UnityEngine.InputSystem;
using KeyCode = UnityEngine.KeyCode;

namespace BS
{
    /// <summary>
    /// Maps Input System <see cref="Key"/> values to the Unity <see cref="KeyCode"/> the Creator SDK's
    /// page-side <c>key-press</c> / <c>key-release</c> events carry. Shared by the app's input relay and
    /// the SDK's Play mode desktop controller, so both send the same values.
    ///
    /// <para>Why a table: the Input System has no Key → KeyCode conversion. The page compares
    /// <c>e.detail.key</c> against <c>BS.KeyCode</c>, which (<c>com.sidequest.injection/Shared/key-code.ts</c>)
    /// is generated from <c>UnityEngine.KeyCode</c> with Unity's own values (e.g. <c>A</c>=97), so the
    /// value sent is simply <c>(int)KeyCode</c>. It used to be a 0-based position in an older,
    /// contiguous key-code.ts (<c>A</c>=46); those numbers no longer match anything.</para>
    /// </summary>
    public static class BSKeyCodeMap
    {
        public static bool TryMap(Key key, out int keyCode)
        {
            if (Table.TryGetValue(key, out var unityKey))
            {
                keyCode = (int)unityKey;
                return true;
            }
            keyCode = 0;
            return false;
        }

        // Covers the standard keyboard; mouse/joystick/touch keys are intentionally omitted (not raw keyboard input).
        private static readonly Dictionary<Key, KeyCode> Table = new Dictionary<Key, KeyCode>
        {
            // Control / whitespace
            { Key.Backspace, KeyCode.Backspace }, { Key.Tab, KeyCode.Tab },       { Key.Enter, KeyCode.Return },
            { Key.Pause, KeyCode.Pause },         { Key.Escape, KeyCode.Escape }, { Key.Space, KeyCode.Space },
            { Key.Delete, KeyCode.Delete },

            // Punctuation (US layout key positions)
            { Key.Quote, KeyCode.Quote },         { Key.Comma, KeyCode.Comma },         { Key.Minus, KeyCode.Minus },
            { Key.Period, KeyCode.Period },       { Key.Slash, KeyCode.Slash },         { Key.Semicolon, KeyCode.Semicolon },
            { Key.Equals, KeyCode.Equals },       { Key.LeftBracket, KeyCode.LeftBracket }, { Key.Backslash, KeyCode.Backslash },
            { Key.RightBracket, KeyCode.RightBracket }, { Key.Backquote, KeyCode.BackQuote },

            // Digits → Alpha0..Alpha9
            { Key.Digit0, KeyCode.Alpha0 }, { Key.Digit1, KeyCode.Alpha1 }, { Key.Digit2, KeyCode.Alpha2 },
            { Key.Digit3, KeyCode.Alpha3 }, { Key.Digit4, KeyCode.Alpha4 }, { Key.Digit5, KeyCode.Alpha5 },
            { Key.Digit6, KeyCode.Alpha6 }, { Key.Digit7, KeyCode.Alpha7 }, { Key.Digit8, KeyCode.Alpha8 },
            { Key.Digit9, KeyCode.Alpha9 },

            // Letters
            { Key.A, KeyCode.A }, { Key.B, KeyCode.B }, { Key.C, KeyCode.C }, { Key.D, KeyCode.D }, { Key.E, KeyCode.E },
            { Key.F, KeyCode.F }, { Key.G, KeyCode.G }, { Key.H, KeyCode.H }, { Key.I, KeyCode.I }, { Key.J, KeyCode.J },
            { Key.K, KeyCode.K }, { Key.L, KeyCode.L }, { Key.M, KeyCode.M }, { Key.N, KeyCode.N }, { Key.O, KeyCode.O },
            { Key.P, KeyCode.P }, { Key.Q, KeyCode.Q }, { Key.R, KeyCode.R }, { Key.S, KeyCode.S }, { Key.T, KeyCode.T },
            { Key.U, KeyCode.U }, { Key.V, KeyCode.V }, { Key.W, KeyCode.W }, { Key.X, KeyCode.X }, { Key.Y, KeyCode.Y },
            { Key.Z, KeyCode.Z },

            // Numpad → Keypad0..9 + operators
            { Key.Numpad0, KeyCode.Keypad0 }, { Key.Numpad1, KeyCode.Keypad1 }, { Key.Numpad2, KeyCode.Keypad2 },
            { Key.Numpad3, KeyCode.Keypad3 }, { Key.Numpad4, KeyCode.Keypad4 }, { Key.Numpad5, KeyCode.Keypad5 },
            { Key.Numpad6, KeyCode.Keypad6 }, { Key.Numpad7, KeyCode.Keypad7 }, { Key.Numpad8, KeyCode.Keypad8 },
            { Key.Numpad9, KeyCode.Keypad9 }, { Key.NumpadPeriod, KeyCode.KeypadPeriod },
            { Key.NumpadDivide, KeyCode.KeypadDivide }, { Key.NumpadMultiply, KeyCode.KeypadMultiply },
            { Key.NumpadMinus, KeyCode.KeypadMinus }, { Key.NumpadPlus, KeyCode.KeypadPlus },
            { Key.NumpadEnter, KeyCode.KeypadEnter }, { Key.NumpadEquals, KeyCode.KeypadEquals },

            // Navigation
            { Key.UpArrow, KeyCode.UpArrow }, { Key.DownArrow, KeyCode.DownArrow }, { Key.RightArrow, KeyCode.RightArrow },
            { Key.LeftArrow, KeyCode.LeftArrow }, { Key.Insert, KeyCode.Insert }, { Key.Home, KeyCode.Home },
            { Key.End, KeyCode.End }, { Key.PageUp, KeyCode.PageUp }, { Key.PageDown, KeyCode.PageDown },

            // Function
            { Key.F1, KeyCode.F1 }, { Key.F2, KeyCode.F2 }, { Key.F3, KeyCode.F3 }, { Key.F4, KeyCode.F4 },
            { Key.F5, KeyCode.F5 }, { Key.F6, KeyCode.F6 }, { Key.F7, KeyCode.F7 }, { Key.F8, KeyCode.F8 },
            { Key.F9, KeyCode.F9 }, { Key.F10, KeyCode.F10 }, { Key.F11, KeyCode.F11 }, { Key.F12, KeyCode.F12 },

            // Locks / modifiers / system
            { Key.NumLock, KeyCode.Numlock },       { Key.CapsLock, KeyCode.CapsLock },   { Key.ScrollLock, KeyCode.ScrollLock },
            { Key.RightShift, KeyCode.RightShift }, { Key.LeftShift, KeyCode.LeftShift },
            { Key.RightCtrl, KeyCode.RightControl }, { Key.LeftCtrl, KeyCode.LeftControl },
            { Key.RightAlt, KeyCode.RightAlt },     { Key.LeftAlt, KeyCode.LeftAlt },
            { Key.RightMeta, KeyCode.RightMeta },   { Key.LeftMeta, KeyCode.LeftMeta },
            { Key.PrintScreen, KeyCode.Print },     { Key.ContextMenu, KeyCode.Menu },
        };
    }
}
