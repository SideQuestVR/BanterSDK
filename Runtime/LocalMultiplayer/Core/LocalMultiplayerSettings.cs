using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BS.LocalMultiplayer
{
    /// <summary>
    /// Local multiplayer settings, one file per project in the main project's UserSettings folder (not version
    /// controlled). Multiplayer Play Mode clones have their own UserSettings, so they read the main project's
    /// file through <see cref="LocalIdentity.MainProjectRoot"/>. Only the main editor's window writes it.
    /// </summary>
    [Serializable]
    public sealed class LocalMultiplayerSettings
    {
        public const string ModeAuto = "auto";
        public const string ModeOn = "on";
        public const string ModeOff = "off";
        public const string MainEditorSlot = "Main Editor";

        public int schema = 1;
        /// <summary>auto: on when the Multiplayer Play Mode package is installed. on / off.</summary>
        public string mode = ModeAuto;
        /// <summary>The player who owns the world: their one-shots arrive with fromAdmin, and they may write
        /// protected space state and clear the room.</summary>
        public string worldOwnerSlot = MainEditorSlot;
        /// <summary>Players who may write protected space state too (production: community moderators).</summary>
        public List<string> moderatorSlots = new List<string>();
        /// <summary>Keep the room's space state between play sessions (production rooms keep it for 24 h).</summary>
        public bool persistSpaceState = true;
        /// <summary>Save dirty scenes on Play: Multiplayer Play Mode clones only load saved scenes.</summary>
        public bool autoSaveScenesOnPlay = true;
        /// <summary>Input System key name that shows and hides the in-game overlay.</summary>
        public string overlayKey = "F8";
        public bool overlayVisibleOnStart = true;

        public bool IsEnabled
        {
            get
            {
                if (string.Equals(mode, ModeOn, StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(mode, ModeOff, StringComparison.OrdinalIgnoreCase)) return false;
#if BANTER_MPPM
                return true;
#else
                return false;
#endif
            }
        }

        public static string ClientIdFor(string slot) => "local:" + slot;

        public PlayerRole RoleFor(string slot)
        {
            if (string.Equals(slot, worldOwnerSlot, StringComparison.Ordinal)) return PlayerRole.Owner;
            if (moderatorSlots != null && moderatorSlots.Contains(slot)) return PlayerRole.Moderator;
            return PlayerRole.Member;
        }

        public static string FilePath(string mainProjectRoot) =>
            Path.Combine(mainProjectRoot, "UserSettings", "SideQuestLocalMultiplayer.json");

        /// <summary>The project's settings, or the defaults when there is no (readable) file.</summary>
        public static LocalMultiplayerSettings Load(string mainProjectRoot)
        {
            var path = FilePath(mainProjectRoot);
            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    if (!File.Exists(path))
                    {
                        return new LocalMultiplayerSettings();
                    }
                    var settings = JsonUtility.FromJson<LocalMultiplayerSettings>(File.ReadAllText(path));
                    if (settings == null)
                    {
                        return new LocalMultiplayerSettings();
                    }
                    if (settings.moderatorSlots == null) settings.moderatorSlots = new List<string>();
                    if (string.IsNullOrEmpty(settings.worldOwnerSlot)) settings.worldOwnerSlot = MainEditorSlot;
                    if (string.IsNullOrEmpty(settings.mode)) settings.mode = ModeAuto;
                    if (string.IsNullOrEmpty(settings.overlayKey)) settings.overlayKey = "F8";
                    return settings;
                }
                catch (IOException)
                {
                    // The main editor is saving it: try again.
                    System.Threading.Thread.Sleep(20);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[LocalMP] Couldn't read {path}, using the defaults: {e.Message}");
                    return new LocalMultiplayerSettings();
                }
            }
            return new LocalMultiplayerSettings();
        }

        /// <summary>Writes the settings atomically (temp file, then replace).</summary>
        public void Save(string mainProjectRoot)
        {
            var path = FilePath(mainProjectRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonUtility.ToJson(this, true));
            if (File.Exists(path))
            {
                File.Replace(temp, path, null);
            }
            else
            {
                File.Move(temp, path);
            }
        }
    }
}
