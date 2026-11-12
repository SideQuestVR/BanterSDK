using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Events;

namespace BS
{
    /// <summary>
    /// Gives the local player XP for something they did in this world. Set an <b>Event Key</b>, then call
    /// <see cref="Grant"/> from a UnityEvent, a Visual Scripting unit, a page script or your own code.
    ///
    /// <para><b>The world never decides the XP.</b> It sends only the key. An admin binds one XP hook to the
    /// world and writes a rule per key, and that rule decides whether the key pays, how much, and how often.
    /// A world that fires a key with no rule earns nothing, and a world with no bound hook sends nothing at
    /// all. Outside the Altspace app (a creator project, play mode in the editor) nothing listens, so the
    /// call is a no-op and only logs.</para>
    ///
    /// <para><b>Who earns:</b> the player on the client where <see cref="Grant"/> runs. Call it only on the
    /// client of the player who did it; calling it on every client pays everyone present, which is right for
    /// a shared moment and wrong for a personal one.</para>
    /// </summary>
    [AddComponentMenu("BS/Grant XP")]
    public class BSGrantXP : MonoBehaviour
    {
        /// <summary>
        /// What a key may contain. The same rule as the app's own gate, which rejects anything else before a
        /// request is sent; kept here so a creator sees the problem in the Inspector instead of silence at
        /// runtime. The two must stay in step.
        /// </summary>
        static readonly Regex KeyPattern = new Regex("^[a-z0-9_.-]{1,64}$", RegexOptions.Compiled);

        [Tooltip("The key the server rule matches, e.g. basketball_win. Lowercase letters, digits, _ . - (1-64).")]
        [SerializeField] string eventKey = "";

        [Tooltip("Smallest gap between two grants of this key from this component, in seconds. Stops a key " +
                 "fired every frame from flooding; 0 sends every call. The app and the server cap it again.")]
        [SerializeField] float minimumSeconds = 1f;

        [Tooltip("Raised locally after a grant is handed to the app. Not a confirmation of XP: the rule decides that.")]
        public UnityEvent onGranted = new UnityEvent();

        float _lastSentAt = float.NegativeInfinity;

        /// <summary>The key this component grants. Settable from a page script or a Visual Scripting unit.</summary>
        public string EventKey
        {
            get => eventKey;
            set => eventKey = value ?? "";
        }

        /// <summary>Whether <see cref="EventKey"/> is a key the app would accept.</summary>
        public bool KeyIsValid => IsValidKey(eventKey);

        /// <summary>Whether <paramref name="key"/> is a key the app would accept.</summary>
        public static bool IsValidKey(string key) => !string.IsNullOrEmpty(key) && KeyPattern.IsMatch(key);

        /// <summary>
        /// The local player earned <see cref="EventKey"/>. Safe to call from a UnityEvent, a Visual Scripting
        /// unit, a page script or your own code; safe to call when nothing is listening.
        /// </summary>
        public void Grant() => GrantKey(eventKey);

        /// <summary>
        /// The local player earned <paramref name="key"/>, ignoring <see cref="EventKey"/>. For one component
        /// serving several actions. The cooldown is shared with <see cref="Grant"/>.
        /// </summary>
        public void GrantKey(string key)
        {
            if (!isActiveAndEnabled) return;
            if (!IsValidKey(key))
            {
                Debug.LogWarning($"[BSGrantXP] '{key}' on {name} is not a valid event key " +
                                 "(lowercase letters, digits, _ . - and 1-64 characters); nothing sent.", this);
                return;
            }
            if (minimumSeconds > 0f && Time.unscaledTime - _lastSentAt < minimumSeconds) return;
            _lastSentAt = Time.unscaledTime;

            var scene = BSScene.Instance();
            if (scene == null || scene.events == null)
            {
                Debug.Log($"[BSGrantXP] '{key}' fired with no scene to send it to.", this);
                return;
            }
            scene.events.OnGrantXp.Invoke(key);
            onGranted.Invoke();
        }

        void OnValidate()
        {
            if (minimumSeconds < 0f) minimumSeconds = 0f;
            if (!string.IsNullOrEmpty(eventKey) && !IsValidKey(eventKey))
                Debug.LogWarning($"[BSGrantXP] '{eventKey}' on {name} is not a valid event key " +
                                 "(lowercase letters, digits, _ . - and 1-64 characters).", this);
        }
    }
}
