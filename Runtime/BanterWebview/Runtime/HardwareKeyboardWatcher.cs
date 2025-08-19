using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Detects and reports key presses/releases from the hardware keyboard.
/// </summary>
namespace NativeInputBridge {

    public class KeyInputEventArgs : EventArgs {
        public KeyInputEventArgs(string keyValue, KeyFlags mods) {
            KeyValue = keyValue;
            Modifiers = mods;
        }
        public readonly string KeyValue;
        public readonly KeyFlags Modifiers;
        public override string ToString() => $"KeyValue: {KeyValue}, Modifiers: {Modifiers}";
    }

    public enum KeyFlags {
        None = 0,
        Shift = 1,
        Control = 2,
        Alt = 4,
        Meta = 8
    }

    public class HardwareKeyboardWatcher : MonoBehaviour {

        // Events for basic key input
        public event EventHandler<KeyInputEventArgs> KeyPressed;
        public event EventHandler<KeyInputEventArgs> KeyReleased;

        private class RepeatTracker {
            public string KeyName;
            public bool AlreadyRepeated;
        }

        private readonly Regex _alphaNumCheck = new Regex("[a-zA-Z0-9]");
        private List<string> _pressedKeys = new List<string>();
        private RepeatTracker _repeatData;
        private KeyFlags _currentModifiers;
        private bool _legacyInputDisabled;

        private static readonly string[] _undetectableKeys = {
            "ArrowDown", "ArrowRight", "ArrowLeft", "ArrowUp",
            "Backspace", "End", "Enter", "Escape", "Delete",
            "Help", "Home", "Insert", "PageDown", "PageUp", "Tab"
        };

        private static readonly string[] _allKeys;
        static HardwareKeyboardWatcher() {
            var mainKeys = new[] {
                "a","b","c","d","e","f","g","h","i","j","k","l","m",
                "n","o","p","q","r","s","t","u","v","w","x","y","z",
                "1","2","3","4","5","6","7","8","9","0","`","-","=",
                "[","]","\\",";","'",",",".","/"," ", "Enter"
            };
            _allKeys = mainKeys.Concat(_undetectableKeys).ToArray();
        }

        private void Awake() {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            _legacyInputDisabled = true;
            Debug.LogWarning("Legacy Input Manager is disabled. Keyboard detection will be skipped.");
#endif
        }

        private KeyFlags DetectModifiers() {
            var mods = KeyFlags.None;
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) mods |= KeyFlags.Shift;
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) mods |= KeyFlags.Control;
            if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) mods |= KeyFlags.Alt;
            if (Input.GetKey(KeyCode.LeftWindows) || Input.GetKey(KeyCode.RightWindows)) mods |= KeyFlags.Meta;
#if !(UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN)
            if (Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand)) mods |= KeyFlags.Meta;
#endif
            return mods;
        }

        private bool UnityKeyNameValid(string jsKey) {
            try {
                foreach (var name in UnityKeyCandidates(jsKey)) {
                    Input.GetKey(name);
                }
                return true;
            } catch {
                return false;
            }
        }

        private string[] UnityKeyCandidates(string jsKey) {
            return jsKey switch {
                " " => new[] { "space" },
                "Alt" => new[] { "left alt", "right alt" },
                "ArrowUp" => new[] { "up" },
                "ArrowDown" => new[] { "down" },
                "ArrowRight" => new[] { "right" },
                "ArrowLeft" => new[] { "left" },
                "Control" => new[] { "left ctrl", "right ctrl" },
                "Enter" => new[] { "return" },
                "Meta" => new[] { "left cmd", "right cmd" },
                "PageUp" => new[] { "page up" },
                "PageDown" => new[] { "page down" },
                "Shift" => new[] { "left shift", "right shift" },
                _ => NumpadCandidates(jsKey)
            };
        }

        private string[] NumpadCandidates(string key) {
            string[] numpadKeys = { "0","1","2","3","4","5","6","7","8","9","+","-","*","/", "." };
            if (numpadKeys.Contains(key)) return new[] { key, $"[{key}]" };
            return new[] { key.ToLowerInvariant() };
        }

        private bool UndetectableKeysCurrentlyPressed() {
            foreach (var key in _undetectableKeys) {
                foreach (var name in UnityKeyCandidates(key)) {
                    if (Input.GetKey(name)) return true;
                }
            }
            return false;
        }

        private bool ProcessInputString() {
            string inputStr = Input.inputString;
#if UNITY_STANDALONE_OSX && UNITY_2020_3
            if (inputStr.Length == 2 && inputStr[0] == inputStr[1]) {
                inputStr = inputStr[0].ToString();
            }
#endif
            foreach (var ch in inputStr) {
                string keyName = ch switch {
                    '\b' => "Backspace",
                    '\n' or '\r' => "Enter",
                    (char)0xF728 => "Delete",
                    _ => ch.ToString()
                };

                bool skipKeyUpBug = _currentModifiers != KeyFlags.None && keyName.Length == 1 && !_alphaNumCheck.IsMatch(keyName);
                bool altGr = _currentModifiers == (KeyFlags.Alt | KeyFlags.Control);
                if (skipKeyUpBug && altGr) _currentModifiers = KeyFlags.None;

                KeyPressed?.Invoke(this, new KeyInputEventArgs(keyName, _currentModifiers));

                if (skipKeyUpBug || !UnityKeyNameValid(keyName)) {
                    KeyReleased?.Invoke(this, new KeyInputEventArgs(keyName, _currentModifiers));
                } else {
                    _pressedKeys.Add(keyName);
                }
            }
            return inputStr.Length > 0;
        }

        private bool ProcessSpecialKeys() {
            bool modsOnly = !(_currentModifiers == KeyFlags.None || _currentModifiers == KeyFlags.Shift);
            bool undetectableActive = UndetectableKeysCurrentlyPressed();
            bool processed = false;

            if (undetectableActive || (Input.inputString.Length == 0 && modsOnly)) {
                foreach (var key in _allKeys) {
                    foreach (var name in UnityKeyCandidates(key)) {
                        if (Input.GetKeyDown(name)) {
                            KeyPressed?.Invoke(this, new KeyInputEventArgs(key, _currentModifiers));
                            _pressedKeys.Add(key);
                            processed = true;
                            if (_repeatData != null) CancelInvoke(nameof(RepeatKey));
                            _repeatData = new RepeatTracker { KeyName = key };
                            InvokeRepeating(nameof(RepeatKey), 0.5f, 0.1f);
                            break;
                        }
                    }
                }
            }
            return processed;
        }

        private void ProcessModifiersOnly() {
            foreach (KeyFlags val in Enum.GetValues(typeof(KeyFlags))) {
                if (val == KeyFlags.None) continue;
                if ((_currentModifiers & val) != 0) {
                    var keyName = val.ToString();
                    KeyPressed?.Invoke(this, new KeyInputEventArgs(keyName, KeyFlags.None));
                    _pressedKeys.Add(keyName);
                }
            }
        }

        private void ProcessReleasedKeys() {
            if (_pressedKeys.Count == 0) return;
            var copy = new List<string>(_pressedKeys);
            foreach (var key in copy) {
                bool released = false;
                try {
                    foreach (var name in UnityKeyCandidates(key)) {
                        if (Input.GetKeyUp(name)) {
                            released = true;
                            break;
                        }
                    }
                } catch (ArgumentException ex) {
                    Debug.LogError("Invalid key for GetKeyUp: " + ex);
                    _pressedKeys.Remove(key);
                    return;
                }
                if (released) {
                    _pressedKeys.Remove(key);
                    bool fireUp = true;
                    if (_repeatData?.KeyName == key) {
                        CancelInvoke(nameof(RepeatKey));
                        if (_repeatData.AlreadyRepeated) fireUp = false;
                        _repeatData = null;
                    }
                    if (fireUp) KeyReleased?.Invoke(this, new KeyInputEventArgs(key, _currentModifiers));
                }
            }
        }

        private void ProcessPressedKeys() {
            if (!(Input.anyKeyDown || Input.inputString.Length > 0)) return;
            if (ProcessSpecialKeys()) return;
            if (ProcessInputString()) return;
            ProcessModifiersOnly();
        }

        private void RepeatKey() {
            var k = _repeatData?.KeyName;
            if (k == null) {
                CancelInvoke(nameof(RepeatKey));
                return;
            }
            var e = new KeyInputEventArgs(k, _currentModifiers);
            if (!_repeatData.AlreadyRepeated) {
                KeyReleased?.Invoke(this, e);
                _repeatData.AlreadyRepeated = true;
            }
            KeyPressed?.Invoke(this, e);
            KeyReleased?.Invoke(this, e);
        }

        private void Update() {
            if (_legacyInputDisabled) return;
            _currentModifiers = DetectModifiers();
            ProcessPressedKeys();
            ProcessReleasedKeys();
        }
    }
}