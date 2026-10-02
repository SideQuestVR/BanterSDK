// <mirror source="Assets/Systems/Networking/NetworkService.cs" sha256="cab93afa037c3e7a7ef1181ed552a18541ae46c49b93872cbae9b18a5f804c80" mode="port" />
using System;
using System.Text.RegularExpressions;
using UnityEngine;

namespace BS.LocalMultiplayer
{
    /// <summary>
    /// Who this editor player is (<see cref="LocalIdentity"/>), and the identity rules shared with the remote
    /// players: the uid is NetworkService.HashUid of the stable user id ("local:" + slot here, "sq:..." or
    /// "guest:..." in production), so a player's uid is the same everywhere and never changes when they join
    /// or leave a room. The per-slot tint colours the orbs and the overlay only; UserData.color keeps
    /// production's values (local 4488FF, remote AAAAAA).
    /// </summary>
    internal static class LocalIdentityFactory
    {
        /// <summary>The local user's UserData.color (LocalUserService.cs:34).</summary>
        public const string LocalColor = "4488FF";
        /// <summary>Every remote user's UserData.color (NetworkService.cs:1247).</summary>
        public const string RemoteColor = "AAAAAA";

        static readonly Regex PlayerSlot = new Regex(@"^Player\s*(\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // Main Editor, Player 2, 3, 4; then a stable pick for anything else (a clone without -name).
        static readonly Color[] SlotTints =
        {
            new Color32(0x3D, 0x8B, 0xFF, 0xFF),
            new Color32(0xFF, 0x8C, 0x2E, 0xFF),
            new Color32(0x34, 0xC7, 0x59, 0xFF),
            new Color32(0xAF, 0x52, 0xDE, 0xFF),
        };
        static readonly Color[] OverflowTints =
        {
            new Color32(0xFF, 0xD6, 0x0A, 0xFF),
            new Color32(0xFF, 0x37, 0x5F, 0xFF),
            new Color32(0x64, 0xD2, 0xFF, 0xFF),
            new Color32(0xA2, 0x84, 0x5E, 0xFF),
        };

        public static LocalIdentity Build(MppmEnvironment env, LocalMultiplayerSettings settings)
        {
            if (env == null) throw new ArgumentNullException(nameof(env));
            if (settings == null) settings = new LocalMultiplayerSettings();
            var identity = new LocalIdentity
            {
                Slot = env.Slot,
                SlotIndex = env.SlotIndex,
                IsClone = env.IsClone,
                VpId = env.VpId ?? "",
                MainProcessId = env.MainProcessId,
                MainProjectRoot = env.MainProjectRoot ?? "",
                ClientId = LocalMultiplayerSettings.ClientIdFor(env.Slot),
                DisplayName = env.Slot,
                Role = settings.RoleFor(env.Slot),
                WorldOwnerClientId = LocalMultiplayerSettings.ClientIdFor(
                    string.IsNullOrEmpty(settings.worldOwnerSlot) ? LocalMultiplayerSettings.MainEditorSlot : settings.worldOwnerSlot),
            };
            identity.Uid = HashUid(identity.ClientId);
            identity.Tint = TintFor(env.SlotIndex, identity.ClientId);
            return identity;
        }

        // Salted hash so the SDK-facing uid is an opaque, stable id rather than the raw SQ/guest id.
        // Salt is identical to Banter (Utils.GetUserId) so the same account hashes to the same uid
        // across both apps. Public so LocalUserService derives the SAME uid offline — a network join
        // then only swaps in the session id, never the uid the page keys users by.
        public static string HashUid(string rawId)
        {
            if (string.IsNullOrEmpty(rawId)) return rawId;
            var hash = new Hash128();
            hash.Append(rawId);
            hash.Append("chicken and cheese no cheese");
            return hash.ToString();
        }

        /// <summary>1 for "Main Editor", N for "Player N", otherwise 0 (as MppmEnvironment counts slots).</summary>
        public static int SlotIndexOf(string slot)
        {
            if (string.IsNullOrEmpty(slot)) return 0;
            if (string.Equals(slot, LocalMultiplayerSettings.MainEditorSlot, StringComparison.Ordinal)) return 1;
            var match = PlayerSlot.Match(slot);
            return match.Success && int.TryParse(match.Groups[1].Value, out var index) ? index : 0;
        }

        /// <summary>The slot's colour; slots past 4 (or unnamed clones) pick one by <paramref name="stableId"/>.</summary>
        public static Color TintFor(int slotIndex, string stableId)
        {
            if (slotIndex >= 1 && slotIndex <= SlotTints.Length) return SlotTints[slotIndex - 1];
            return OverflowTints[MppmEnvironment.Fnv1a32(stableId ?? "") % (uint)OverflowTints.Length];
        }
    }
}
