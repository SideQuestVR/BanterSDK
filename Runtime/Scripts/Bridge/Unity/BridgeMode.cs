using System;
using SideQuest.Ora;
using UnityEngine;

namespace BS.Bridge
{
    internal enum BridgeLaneMode
    {
        /// <summary>Today's wire: BatchUpdater (11 ms) and the delimiter strings, byte for byte. The default.</summary>
        Legacy,
        /// <summary>Bridge 2 sessions over the carrier (M1): document epochs, one outbound queue, frames.</summary>
        Session,
    }

    /// <summary>
    /// Which lane the space view's page and BSLink use, decided once per play session (Bridge 2 plan §7, kill switch).
    /// Precedence: command line <c>--bs-bridge=session|legacy</c>, then the Editor setting (EditorUserSettings
    /// <see cref="EditorSettingKey"/>: this project, this user; menu Greenfield/Creator SDK/Bridge Lane), then
    /// <see cref="RemoteConfig"/> (TODO hook
    /// for the persisted/remote <c>bridge</c> block and its rollout percentage), then the build default (legacy in M1).
    /// The per-world <c>&lt;html bs-bridge="legacy"&gt;</c> opt-out is the page's (HELLO with OPT_OUT).
    /// Android stays on the legacy lane in M1 whatever the setting: its messages can overtake LoadStarted, which only
    /// M4's document-start signal settles.
    /// </summary>
    internal static class BridgeMode
    {
        public const string CommandLineFlag = "--bs-bridge";
        public const string EditorSettingKey = "BS.Bridge.Mode";
        /// <summary>Set by <see cref="PagePrelude"/>; read by the page's createApi (src/game/batch-updater.ts).</summary>
        public const string PageConfigGlobal = "__BS_BRIDGE_CONFIG";
        public const string PagePrelude = "window." + PageConfigGlobal + "={\"mode\":\"session\"};\n";

        /// <summary>
        /// TODO(rollout): the persisted last-good / remote <c>bridge</c> block (AppConfig, rolloutPercent over a stable
        /// device hash), read before the first CreateWindow. Return "session", "legacy" or null for no opinion.
        /// </summary>
        public static Func<string> RemoteConfig;

        static BridgeLaneMode? resolved;
        static string source = "";

        public static BridgeLaneMode Current
        {
            get
            {
                if (resolved == null) Resolve();
                return resolved.Value;
            }
        }

        public static bool Session => Current == BridgeLaneMode.Session;
        public static string Source { get { if (resolved == null) Resolve(); return source; } }

        // Statics survive play mode when domain reload is off: decide again for every play session.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetForPlaySession() => resolved = null;

        /// <summary>Tests: force a mode (null resolves again from the settings).</summary>
        internal static void SetForTests(BridgeLaneMode? mode) { resolved = mode; source = mode == null ? "" : "test"; }

        static void Resolve()
        {
            string value = null;
            source = "build default";
            string cli = null;
            try { cli = FromCommandLine(Environment.GetCommandLineArgs()); } catch (Exception) { }
            if (cli != null)
            {
                if (Parse(cli) == null) Debug.LogError("[Bridge] Ignoring " + CommandLineFlag + "=" + cli + ": use session or legacy.");
                else { value = cli; source = "command line"; }
            }
#if UNITY_EDITOR
            if (value == null)
            {
                var setting = UnityEditor.EditorUserSettings.GetConfigValue(EditorSettingKey);
                if (!string.IsNullOrEmpty(setting))
                {
                    if (Parse(setting) == null) Debug.LogError("[Bridge] Ignoring the editor setting " + EditorSettingKey + "=" + setting + ": use session or legacy.");
                    else { value = setting; source = "editor setting"; }
                }
            }
#endif
            if (value == null && RemoteConfig != null)
            {
                try { value = RemoteConfig(); if (value != null) source = "remote config"; }
                catch (Exception e) { Debug.LogWarning("[Bridge] Remote bridge config failed: " + e.Message); }
            }
            var mode = Parse(value) ?? BridgeLaneMode.Legacy;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (mode == BridgeLaneMode.Session)
            {
                mode = BridgeLaneMode.Legacy;
                LogLine.Do("[Bridge] Session lane requested (" + source + ") but forced to legacy on Android until M4.");
                source += ", forced legacy on Android";
            }
#endif
            resolved = mode;
            if (mode == BridgeLaneMode.Session || source != "build default")
                LogLine.Do("[Bridge] Lane: " + (mode == BridgeLaneMode.Session ? "session" : "legacy") + " (" + source + ").");
        }

        /// <summary>"--bs-bridge=session", or "--bs-bridge session". Null when absent.</summary>
        internal static string FromCommandLine(string[] args)
        {
            if (args == null) return null;
            for (int i = 0; i < args.Length; i++)
            {
                var a = args[i];
                if (a == null) continue;
                if (a.StartsWith(CommandLineFlag + "=", StringComparison.OrdinalIgnoreCase)) return a.Substring(CommandLineFlag.Length + 1);
                if (string.Equals(a, CommandLineFlag, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) return args[i + 1];
            }
            return null;
        }

        internal static BridgeLaneMode? Parse(string value)
        {
            switch ((value ?? "").Trim().ToLowerInvariant())
            {
                case "session": case "v2": case "on": case "carrier": return BridgeLaneMode.Session;
                case "legacy": case "off": return BridgeLaneMode.Legacy;
                default: return null;
            }
        }

        /// <summary>
        /// Gives the space view its link profile (the large rings) and the injection for the current lane. Call before
        /// the view's Awake (it opens its channel and reads the injection when it registers its window). Idempotent.
        /// </summary>
        public static void ApplyTo(OraView view)
        {
            if (view == null) return;
            view.linkProfile = OraChannelProfile.SdkView;
            view.customInjectedJavascript = InjectionAsset(view.customInjectedJavascript);
        }

        /// <summary>
        /// The space view's injection. Legacy: the bundle itself, the same object as before (CreateWindow's bytes are
        /// unchanged). Session: the same bundle with a one-line prelude that tells the page to open a session.
        /// </summary>
        public static TextAsset InjectionAsset(TextAsset bundle)
        {
            if (bundle == null || !Session) return bundle;
            var text = bundle.text;
            if (text.StartsWith(PagePrelude, StringComparison.Ordinal)) return bundle;
            return new TextAsset(PagePrelude + text) { name = bundle.name };
        }
    }
}
