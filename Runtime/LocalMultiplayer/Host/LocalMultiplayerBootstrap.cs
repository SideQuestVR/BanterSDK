using System;
using System.Globalization;
using System.IO;
using SideQuest.Ora;
using UnityEngine;

namespace BS.LocalMultiplayer
{
    /// <summary>
    /// Sets local multiplayer up for each Play, before any scene loads: who this editor is (MppmEnvironment)
    /// and the project's settings decide how Ora launches its browser, whether the page waits for the hub,
    /// and whether the host installs when BSStarterUpper links the scene.
    /// <list type="bullet">
    /// <item>Main editor: its Electron is the hub, so it gets <c>--relay-project</c> (and
    /// <c>--relay-state-dir</c> when space state persists) next to the web server it already runs.</item>
    /// <item>A Multiplayer Play Mode clone: no web server of its own (it loads the main editor's page), its
    /// own browser profile, and normal process priority.</item>
    /// </list>
    /// With local multiplayer off, nothing is changed and the SDK behaves exactly as before.
    /// </summary>
    internal static class LocalMultiplayerBootstrap
    {
        static bool s_enabled;
        static MppmEnvironment s_environment;
        static LocalMultiplayerSettings s_settings;
        static bool s_explainedSkip;

        // The browser arguments we set last. Ora's launch statics live exactly as long as this one (a domain
        // reload clears both), so unlike the rest it is not reset per Play: Play without a domain reload must
        // still be able to take back what an earlier Play set.
        static string s_appliedArguments;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            s_enabled = false;
            s_environment = null;
            s_settings = null;
            s_explainedSkip = false;
        }

        // AfterAssembliesLoaded rather than BeforeSceneLoad: BSStarterUpper's own BeforeSceneLoad may spawn the
        // starter (and with it OraManager.Awake, which launches the browser, and the SceneLinked raise), and
        // Unity doesn't order methods of one load type. This runs before all of them, after BSNetworkHost's reset.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void BeforePlay()
        {
            try
            {
                s_environment = MppmEnvironment.Current;
                s_settings = LocalMultiplayerSettings.Load(s_environment.MainProjectRoot);
                s_enabled = s_settings.IsEnabled;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LocalMP] Couldn't read the local multiplayer setup, so it is off for this Play: {e.Message}");
                s_enabled = false;
            }

            ConfigureOra();

            BSNetworkHost.SceneLinked -= OnSceneLinked;
            if (!s_enabled) return;
            BSNetworkHost.PageLoadGate = HubProbe.Gate;
            BSNetworkHost.SceneLinked += OnSceneLinked;
        }

        static void ConfigureOra()
        {
            if (!s_enabled)
            {
                // Put back what an earlier Play in this domain set (Play mode options without a domain reload).
                if (s_appliedArguments != null)
                {
                    if (string.Equals(OraManager.defaultExtraArguments, s_appliedArguments, StringComparison.Ordinal))
                    {
                        OraManager.defaultExtraArguments = null;
                    }
                    OraManager.staticServerDisabled = false;
                    OraManager.boostBrowserPriority = true;
                    s_appliedArguments = null;
                }
                return;
            }

            var env = s_environment;
            string arguments;
            if (env.IsClone)
            {
                // The main editor's Electron serves the page and runs the relay; this one only browses, under its
                // own profile (two Electrons can't share one), at normal priority (four real-time browsers on one
                // machine starve the editors).
                OraManager.staticServerDisabled = true;
                OraManager.boostBrowserPriority = false;
                var player = env.SlotIndex > 0
                    ? env.SlotIndex.ToString(CultureInfo.InvariantCulture)
                    : (!string.IsNullOrEmpty(env.VpId) ? env.VpId : "0");
                arguments = "--ora-profile " + QuoteArgument("mppm-" + env.ProjectHash8 + "-p" + player);
            }
            else
            {
                OraManager.staticServerDisabled = false;
                OraManager.boostBrowserPriority = true;
                arguments = "--relay-project " + QuoteArgument(env.MainProjectRoot);
                if (s_settings.persistSpaceState)
                {
                    arguments += " --relay-state-dir " + QuoteArgument(SpaceStateDirectory(env.MainProjectRoot));
                }
            }
            OraManager.defaultExtraArguments = arguments;
            s_appliedArguments = arguments;
        }

        /// <summary>Where the relay keeps the rooms' space state between Plays (RELAY.md 7).</summary>
        internal static string SpaceStateDirectory(string mainProjectRoot)
        {
            return Path.Combine(mainProjectRoot ?? "", "Library", "SideQuestLocalMultiplayer", "space-state");
        }

        /// <summary>
        /// Quotes a path for Electron's command line. Trailing separators are dropped first: a backslash right
        /// before the closing quote would escape it and swallow the arguments after it.
        /// </summary>
        internal static string QuoteArgument(string value)
        {
            var trimmed = (value ?? "").TrimEnd('\\', '/');
            return "\"" + trimmed.Replace("\"", "") + "\"";
        }

        static void OnSceneLinked(BSScene scene)
        {
            if (!s_enabled || scene == null) return;
            if (LocalMultiplayerHost.Instance != null) return;
            if (BSStarterUpper.AutoStartDisabled)
            {
                ExplainSkip("the Banter desktop controller is switched off (Altspace menu), so there is no local player to network.");
                return;
            }
            if (BSDesktopController.Instance == null)
            {
                ExplainSkip("there is no SDK desktop player in this Play, so there is no local player to network.");
                return;
            }
            LocalMultiplayerHost.Create(scene, s_environment ?? MppmEnvironment.Current, s_settings ?? new LocalMultiplayerSettings());
        }

        static void ExplainSkip(string why)
        {
            if (s_explainedSkip) return;
            s_explainedSkip = true;
            Debug.Log("[LocalMP] Local multiplayer is on, but " + why);
        }
    }
}
