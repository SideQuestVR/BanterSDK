using System;
using System.Globalization;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace BS.LocalMultiplayer.Editor
{
    /// <summary>
    /// The packages local multiplayer depends on, as the Package Manager sees them: Multiplayer Play Mode (the extra
    /// editor players), Ora (its Electron app hosts the relay) and FlexaBody (no desktop player, so not supported).
    /// No Multiplayer Play Mode API is used, so nothing here needs the package to compile.
    /// </summary>
    internal static class LocalMpPackages
    {
        internal const string MppmName = "com.unity.multiplayer.playmode";

        /// <summary>The release Unity 6000.3 to 6000.5 pair with. From 6000.6 the package is core.</summary>
        internal const string MppmPinnedVersion = "2.0.2";

        internal const string MppmWindowMenu = "Window/Multiplayer/Multiplayer Play Mode";
        internal const string OraName = "com.sidequest.ora";

        /// <summary>The first Ora whose Electron app has the relay (the BS_ORA_RELAY version define).</summary>
        internal const string OraMinimumVersion = "0.1.0";

        internal const string FlexaBodyName = "com.sidequest.flexabody";

        static AddRequest s_Install;

        /// <summary>An install request is running.</summary>
        internal static bool Installing => s_Install != null;

        /// <summary>How the last install ended; null until one has.</summary>
        internal static string InstallMessage { get; private set; }

        internal static event Action InstallFinished;

        /// <summary>The registered package with this name, or null.</summary>
        internal static PackageInfo Find(string name)
        {
            try
            {
                return PackageInfo.FindForPackageName(name);
            }
            catch (Exception)
            {
                // Fall back to the full list below.
            }
            foreach (var package in PackageInfo.GetAllRegisteredPackages())
            {
                if (package != null && string.Equals(package.name, name, StringComparison.Ordinal))
                {
                    return package;
                }
            }
            return null;
        }

        /// <summary>Ora by name, or by the package its assembly ships in (a renamed or DLL-form copy).</summary>
        internal static PackageInfo FindOra()
        {
            var ora = Find(OraName);
            if (ora != null)
            {
                return ora;
            }
            try
            {
                return PackageInfo.FindForAssembly(typeof(SideQuest.Ora.OraManager).Assembly);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Multiplayer Play Mode 2 and later is a switch for the engine's own implementation, which stays off with
        /// a 1.x package installed (the engine checks for major version 2 or later).
        /// </summary>
        internal static bool IsUsableMppm(string version)
        {
            return TryParseVersion(version, out var parsed) && parsed.Major >= 2;
        }

        /// <summary>From Unity 6000.6 Multiplayer Play Mode is a core package fixed to the editor's own version.</summary>
        internal static bool MppmIsCore(string unityVersion)
        {
            if (string.IsNullOrEmpty(unityVersion))
            {
                return false;
            }
            var parts = unityVersion.Split('.');
            if (parts.Length < 2
                || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major)
                || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor))
            {
                return false;
            }
            return major > 6000 || (major == 6000 && minor >= 6);
        }

        /// <summary>
        /// A package version as major.minor.patch, ignoring a pre-release or build suffix ("0.1.0-pre.1" is
        /// 0.1.0). False for anything else.
        /// </summary>
        internal static bool TryParseVersion(string text, out Version version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }
            var core = text.Trim();
            var cut = core.IndexOfAny(new[] { '-', '+' });
            if (cut >= 0)
            {
                core = core.Substring(0, cut);
            }
            if (core.IndexOf('.') < 0)
            {
                core += ".0";
            }
            return Version.TryParse(core, out version);
        }

        /// <summary>True when <paramref name="version"/> is at least <paramref name="minimum"/>.</summary>
        internal static bool MeetsMinimum(string version, string minimum)
        {
            return TryParseVersion(version, out var have) && TryParseVersion(minimum, out var need)
                   && Normalize(have) >= Normalize(need);
        }

        // Version treats a missing build field as -1, so "0.1" would sort below "0.1.0".
        static Version Normalize(Version v)
        {
            return new Version(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build));
        }

        /// <summary>Adds Multiplayer Play Mode at the pinned version; the editor recompiles once it is in.</summary>
        internal static void InstallMppm()
        {
            if (s_Install != null)
            {
                return;
            }
            InstallMessage = null;
            s_Install = Client.Add(MppmName + "@" + MppmPinnedVersion);
            EditorApplication.update -= PollInstall;
            EditorApplication.update += PollInstall;
        }

        static void PollInstall()
        {
            if (s_Install == null)
            {
                EditorApplication.update -= PollInstall;
                return;
            }
            if (!s_Install.IsCompleted)
            {
                return;
            }
            EditorApplication.update -= PollInstall;
            if (s_Install.Status == StatusCode.Success)
            {
                var version = s_Install.Result != null ? s_Install.Result.version : MppmPinnedVersion;
                InstallMessage = $"Installed Multiplayer Play Mode {version}. Once the editor has recompiled, open "
                               + "it and tick the players you want.";
                Debug.Log("[LocalMP] " + InstallMessage);
            }
            else
            {
                var error = s_Install.Error != null ? s_Install.Error.message : "unknown error";
                InstallMessage = "Couldn't install Multiplayer Play Mode: " + error;
                Debug.LogWarning("[LocalMP] " + InstallMessage);
            }
            s_Install = null;
            InstallFinished?.Invoke();
        }

        /// <summary>Opens Window > Multiplayer > Multiplayer Play Mode. False when the menu item doesn't exist yet.</summary>
        internal static bool OpenMppmWindow()
        {
            return EditorApplication.ExecuteMenuItem(MppmWindowMenu);
        }
    }
}
